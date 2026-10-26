using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Net.Sockets;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AgentGlow
{
    internal sealed class TrayApplication : ApplicationContext
    {
        private const string AppTitle = "AgentGlow";
        private const int MaxTrayTextLength = 63;
        private const int MaxMenuThreads = 12;
        private const int HousekeepingIntervalMs = 15 * 1000;
        private const int ReconnectIntervalMs = 5 * 1000;
        private const int PreviewDurationMs = 5 * 1000;
        private const int AnimationIntervalMs = 40;
        private const int OpenRgbWatchIntervalMs = 5000;
        private const int RepairBlinkIntervalMs = 500;
        private const string RepairingText = "перезапускаю службу…";
        private static readonly TimeSpan LaunchRetryInterval = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan OpenRgbAutoRestartAfter = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan OpenRgbAutoRestartCooldown = TimeSpan.FromMinutes(10);
        private static readonly Color NeutralColor = Color.FromArgb(128, 128, 128);
        private static readonly StatusEffect DarkEffect = new StatusEffect(EffectKind.Off, 0x000000, 3);

        private readonly NotifyIcon trayIcon = new NotifyIcon();
        private readonly Control invoker = new Control();
        private readonly LightingController lighting = new LightingController();
        private readonly Timer housekeepingTimer = new Timer();
        private readonly Timer reconnectTimer = new Timer();
        private readonly Timer previewTimer = new Timer();
        private readonly Timer livenessTimer = new Timer();
        private readonly Timer animationTimer = new Timer();
        private readonly Timer repairBlinkTimer = new Timer();
        private readonly Timer openRgbWatchTimer = new Timer();
        private readonly Stopwatch animationClock = new Stopwatch();
        private readonly Dictionary<string, bool> integrationInstalled = new Dictionary<string, bool>();
        private readonly StatusHistory history = new StatusHistory();
        private SourceSet sources;
        private AppSettings settings;
        private EventListener listener;
        private readonly int eventPort;
        private StatusEffect previewEffect;
        private bool repairBlinkOn;
        private bool paused;
        private bool dark;
        private string loggedOpenRgbState;
        private bool openRgbRestartRunning;
        private DateTime? openRgbUnhealthySinceUtc;
        private DateTime lastOpenRgbAutoRestartUtc = DateTime.MinValue;
        private bool restartOnBalloonClick;
        private bool updateBalloonShown;
        private readonly Dictionary<string, int> pendingSourceEvents = new Dictionary<string, int>();
        private DateTime lastActivityUtc = DateTime.UtcNow;
        private DateTime lastLaunchAttemptUtc = DateTime.MinValue;
        private SettingsForm settingsForm;

        public TrayApplication(AppSettings loaded)
        {
            bool firstRun = !AppSettings.FileExists;
            settings = loaded;
            eventPort = loaded.EventPort;
            if (firstRun)
            {
                string saveProblem = settings.Save();
                if (saveProblem != null) AppLog.Append("settings not saved: " + saveProblem);
            }
            AppLog.Append("settings: first run=" + firstRun + ", event port=" + settings.EventPort
                + ", sources=" + string.Join(",", settings.EnabledSources.ToArray())
                + ", devices=" + (settings.DeviceNames == null ? "all" : string.Join("|", settings.DeviceNames.ToArray()))
                + ", launch OpenRGB=" + settings.LaunchOpenRgb);
            sources = SourceCatalog.Create(settings);
            invoker.CreateControl();
            trayIcon.ContextMenuStrip = new ContextMenuStrip();
            trayIcon.ContextMenuStrip.Opening += OnMenuOpening;
            trayIcon.MouseDoubleClick += OnTrayDoubleClick;
            trayIcon.BalloonTipClicked += OnBalloonClicked;
            openRgbWatchTimer.Interval = OpenRgbWatchIntervalMs;
            openRgbWatchTimer.Tick += OnOpenRgbWatchTick;
            openRgbWatchTimer.Start();
            repairBlinkTimer.Interval = RepairBlinkIntervalMs;
            repairBlinkTimer.Tick += (sender, e) =>
            {
                repairBlinkOn = !repairBlinkOn;
                UpdateTrayIcon();
            };
            trayIcon.Visible = true;

            housekeepingTimer.Interval = HousekeepingIntervalMs;
            housekeepingTimer.Tick += OnHousekeepingTick;
            housekeepingTimer.Start();
            reconnectTimer.Interval = ReconnectIntervalMs;
            reconnectTimer.Tick += OnReconnectTick;
            previewTimer.Interval = PreviewDurationMs;
            previewTimer.Tick += OnPreviewTick;
            livenessTimer.Interval = SourceSet.DefaultLivenessIntervalMs;
            livenessTimer.Tick += OnLivenessTick;
            animationTimer.Interval = AnimationIntervalMs;
            animationTimer.Tick += OnAnimationTick;

            SystemEvents.SessionEnding += OnSessionEnding;
            sources.Load();
            if (sources.LatestEventUtc > DateTime.MinValue) lastActivityUtc = sources.LatestEventUtc;
            sources.Housekeep(DateTime.UtcNow, settings);
            UpdateLivenessTimer();
            lighting.Configure(settings.DeviceNames, settings.FixedDeviceNames, settings.FixedEffect);
            StartListener();
            RefreshIntegrationStatus();
            foreach (IAgentSource source in sources.All)
            {
                if (source.Integration != null)
                {
                    AppLog.Append(source.DisplayName + " integration " + source.Integration.Location + ": "
                        + (integrationInstalled[source.Id] ? "installed" : "not installed"));
                }
            }
            Connect();
            history.Mark("start " + AppIdentity.Version);
            Refresh("start");
            UpdateUsageState();
            Usage.Start();
            UpdateWatcher.Start(version => Post(() => ShowUpdateBalloon(version)));
        }

        private void Post(Action action)
        {
            try
            {
                invoker.BeginInvoke(action);
            }
            catch (InvalidOperationException)
            {
            }
        }

        protected override void ExitThreadCore()
        {
            FlushSourceEvents();
            SystemEvents.SessionEnding -= OnSessionEnding;
            housekeepingTimer.Dispose();
            reconnectTimer.Dispose();
            previewTimer.Dispose();
            livenessTimer.Dispose();
            animationTimer.Dispose();
            repairBlinkTimer.Dispose();
            openRgbWatchTimer.Dispose();
            if (listener != null) listener.Dispose();
            if (settingsForm != null) settingsForm.Close();
            ParkLighting("exit");
            lighting.Dispose();
            trayIcon.Visible = false;
            trayIcon.Dispose();
            invoker.Dispose();
            base.ExitThreadCore();
        }

        private void OnSessionEnding(object sender, SessionEndingEventArgs e)
        {
            FlushSourceEvents();
            ParkLighting("windows session ending");
        }

        private StatusEffect ParkedEffect()
        {
            return settings.EffectFor(GlowStatus.Idle);
        }

        private void ParkLighting(string reason)
        {
            StatusEffect parked = ParkedEffect();
            lighting.Apply(parked, null);
            AppLog.Append(reason + ": lighting parked as " + parked.Kind + (lighting.IsConnected ? "" : ", OpenRGB not connected"));
            history.Mark("parked: " + reason);
        }

        private void StartListener()
        {
            listener = new EventListener(eventPort, sources, OnSourceRequest);
            try
            {
                listener.Start();
                AppLog.Append("event listener on 127.0.0.1:" + eventPort);
            }
            catch (SocketException exception)
            {
                listener = null;
                string message = "Порт " + eventPort + " занят, события приложений не принимаются: " + exception.Message;
                AppLog.Append(message);
                ShowBalloon(5000, message, ToolTipIcon.Warning);
            }
        }

        private void OnSourceRequest(IAgentSource source, EventRequest request)
        {
            try
            {
                request.Received = DateTimeOffset.Now;
                invoker.BeginInvoke(new Action<IAgentSource, EventRequest>(HandleRequest), source, request);
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void HandleRequest(IAgentSource source, EventRequest request)
        {
            DateTime now = DateTime.UtcNow;
            lastActivityUtc = now;
            CountSourceEvent(source);
            bool changed = source.Handle(request, now) || dark;
            UpdateLivenessTimer();
            string cause = source.Id + " " + source.LastEventLabel;
            history.Stamp = request.Received;
            try
            {
                if (changed) Refresh(cause);
                else RecordHistory(cause);
                if (source.LastTurnKey != null) history.Event(source.LastTurnKey, cause);
            }
            finally
            {
                history.Stamp = null;
            }
        }

        private void Connect()
        {
            bool connected = lighting.TryConnect();
            LogOpenRgbState();
            if (connected)
            {
                reconnectTimer.Stop();
                return;
            }
            if (settings.LaunchOpenRgb && !OpenRgbService.IsInstalled() && DateTime.UtcNow - lastLaunchAttemptUtc > LaunchRetryInterval)
            {
                lastLaunchAttemptUtc = DateTime.UtcNow;
                if (OpenRgbLauncher.TryStart(settings.OpenRgbPath)) AppLog.Append("OpenRGB started");
            }
            reconnectTimer.Start();
        }

        private void Refresh()
        {
            Refresh("display");
        }

        private void Refresh(string cause)
        {
            if (previewEffect != null) lighting.Apply(previewEffect, null);
            else if (paused) lighting.Apply(ParkedEffect(), null);
            else if (IsDark()) lighting.Apply(DarkEffect, null);
            else lighting.Apply(settings.EffectFor(sources.TopStatus), SecondaryEffect());
            dark = ShowsDark();
            if (!lighting.IsConnected) reconnectTimer.Start();
            UpdateAnimationTimer();
            UpdateTrayIcon();
            RecordHistory(cause);
        }

        private void RecordHistory(string cause)
        {
            history.Record(sources.Threads(), ShownState(), cause);
        }

        private string ShownState()
        {
            if (previewEffect != null) return "preview";
            if (paused) return "paused";
            if (IsDark()) return "dark";
            GlowStatus top = sources.TopStatus;
            return IsSplit() ? top + "+" + GlowStatus.Working : top.ToString();
        }

        private void UpdateAnimationTimer()
        {
            if (lighting.IsAnimating == animationTimer.Enabled) return;
            if (lighting.IsAnimating)
            {
                animationClock.Restart();
                animationTimer.Start();
            }
            else
            {
                animationTimer.Stop();
                animationClock.Stop();
            }
        }

        private void OnAnimationTick(object sender, EventArgs e)
        {
            lighting.AnimationFrame(animationClock.Elapsed.TotalSeconds);
            UpdateAnimationTimer();
        }

        private StatusEffect SecondaryEffect()
        {
            return IsSplit() ? settings.EffectFor(GlowStatus.Working) : null;
        }

        private bool ShowsDark()
        {
            return previewEffect == null && !paused && IsDark();
        }

        private bool IsDark()
        {
            GlowStatus top = sources.TopStatus;
            bool needsUser = top == GlowStatus.Error || top == GlowStatus.Permission || top == GlowStatus.Question;
            return settings.DarkAfterMinutes > 0 && !needsUser
                && DateTime.UtcNow - lastActivityUtc >= TimeSpan.FromMinutes(settings.DarkAfterMinutes);
        }

        private bool IsSplit()
        {
            GlowStatus top = sources.TopStatus;
            return !paused && previewEffect == null && !IsDark()
                && (top == GlowStatus.Done || top == GlowStatus.DoneIdle)
                && sources.Has(GlowStatus.Working);
        }

        private string StatusText()
        {
            if (paused) return "пауза";
            if (IsDark()) return "погашено, нет событий " + settings.DarkAfterMinutes + " мин";
            string text = StatusCatalog.DisplayName(sources.TopStatus);
            return IsSplit() ? text + " + " + StatusCatalog.DisplayName(GlowStatus.Working) : text;
        }

        private Color IconColor(GlowStatus status)
        {
            StatusEffect effect = settings.EffectFor(status);
            return paused || IsDark() || !EffectCatalog.UsesColor(effect.Kind) ? NeutralColor : effect.Color;
        }

        private void UpdateTrayIcon()
        {
            GlowStatus top = sources.TopStatus;
            Color left = IconColor(top);
            Color right = IsSplit() ? IconColor(GlowStatus.Working) : left;
            trayIcon.Icon = StatusIconPainter.Paint(left, right, CurrentBadge());
            string text = AppTitle + ": " + StatusText();
            string problem = AppProblem();
            if (problem != null) text += "\n" + problem;
            trayIcon.Text = text.Length > MaxTrayTextLength ? text.Substring(0, MaxTrayTextLength) : text;
        }

        private void RefreshIntegrationStatus()
        {
            foreach (IAgentSource source in sources.All)
            {
                if (source.Integration == null) continue;
                integrationInstalled[source.Id] = source.Integration.IsInstalled(eventPort);
            }
        }

        private void OnMenuOpening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            ContextMenuStrip menu = trayIcon.ContextMenuStrip;
            menu.Items.Clear();
            menu.Items.Add(Disabled("Статус: " + StatusText()));
            menu.Items.Add(Disabled("OpenRGB: " + OpenRgbText()));
            if (!lighting.IsConnected && OpenRgbService.IsInstalled())
            {
                var restartItem = new ToolStripMenuItem("Перезапустить службу OpenRGB", null, OnRestartOpenRgbClick);
                restartItem.Enabled = !openRgbRestartRunning;
                menu.Items.Add(restartItem);
            }
            RefreshIntegrationStatus();
            UpdateTrayIcon();
            foreach (IAgentSource source in sources.All)
            {
                if (source.Integration == null) continue;
                if (integrationInstalled[source.Id])
                {
                    menu.Items.Add(Disabled(source.DisplayName + ": подключено"));
                    continue;
                }
                IAgentSource missing = source;
                menu.Items.Add(new ToolStripMenuItem(source.DisplayName + " не подключён — подключить", null, (s, a) => OnInstallIntegrationClick(missing)));
            }
            List<ThreadInfo> threads = sources.ThreadsByStatus();
            if (threads.Count > 0) menu.Items.Add(new ToolStripSeparator());
            for (int i = 0; i < threads.Count && i < MaxMenuThreads; i++)
            {
                menu.Items.Add(Disabled(threads[i].Label));
            }
            menu.Items.Add(new ToolStripSeparator());
            var pauseItem = new ToolStripMenuItem("Приостановить индикацию", null, OnPauseClick);
            pauseItem.Checked = paused;
            menu.Items.Add(pauseItem);
            menu.Items.Add(new ToolStripMenuItem("Сбросить статусы сессий", null, OnResetClick));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Настройки…", null, OnSettingsClick));
            menu.Items.Add(new ToolStripMenuItem("Журнал", null, OnLogClick));
            var startupItem = new ToolStripMenuItem("Запускать при входе в Windows", null, OnStartupClick);
            startupItem.Checked = Autostart.IsEnabled();
            menu.Items.Add(startupItem);
            menu.Items.Add(new ToolStripMenuItem("Проверить обновление…", null, (s, a) =>
            {
                Usage.Hit("menu.update_check");
                UpdateForm.ShowSingle();
            }));
            menu.Items.Add(new ToolStripMenuItem("О приложении…", null, (s, a) =>
            {
                Usage.Hit("menu.about");
                AboutForm.ShowSingle();
            }));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Выход", null, OnExitClick));
            e.Cancel = false;
        }

        private void OnInstallIntegrationClick(IAgentSource source)
        {
            Usage.Hit("menu.integration_install");
            IIntegration integration = source.Integration;
            try
            {
                integration.Install(eventPort);
                integrationInstalled[source.Id] = true;
                UpdateTrayIcon();
                AppLog.Append(source.DisplayName + " integration installed into " + integration.Location);
                ShowBalloon(5000, source.DisplayName + ": подключено (" + integration.Location + "). Перезапусти " + source.DisplayName + ", чтобы подхватить.", ToolTipIcon.Info);
            }
            catch (Exception error)
            {
                MessageBox.Show("Не удалось подключить " + source.DisplayName + " (" + integration.Location + "): " + error.Message, AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static ToolStripMenuItem Disabled(string text)
        {
            var item = new ToolStripMenuItem(text);
            item.Enabled = false;
            return item;
        }

        private void OnTrayDoubleClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            Usage.Hit("tray.double_click");
            ShowSettings();
        }

        private void OnSettingsClick(object sender, EventArgs e)
        {
            Usage.Hit("menu.settings");
            ShowSettings();
        }

        private void ShowSettings()
        {
            if (settingsForm != null)
            {
                settingsForm.Activate();
                return;
            }
            settingsForm = new SettingsForm(settings.Clone(), lighting, OnSettingsPreview);
            settingsForm.FormClosed += OnSettingsClosed;
            settingsForm.Show();
        }

        private void OnSettingsClosed(object sender, FormClosedEventArgs e)
        {
            SettingsForm form = settingsForm;
            settingsForm = null;
            StopPreview();
            if (form.DialogResult != DialogResult.OK) return;
            settings = form.Result;
            string saveProblem = settings.Save();
            if (saveProblem != null)
            {
                AppLog.Append("settings not saved: " + saveProblem);
                MessageBox.Show("Не удалось сохранить " + AppSettings.FilePath + ": " + saveProblem, AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            SaveOptions(form);
            Usage.Hit("settings.save");
            lighting.Configure(settings.DeviceNames, settings.FixedDeviceNames, settings.FixedEffect);
            sources.Housekeep(DateTime.UtcNow, settings);
            UpdateUsageState();
            Refresh("housekeeping");
        }

        private void SaveOptions(SettingsForm form)
        {
            string problem = null;
            try
            {
                if (form.AutoUpdateChosen != AutoUpdateCheck.IsEnabled) AutoUpdateCheck.Set(form.AutoUpdateChosen);
                if (form.UsageStatisticsChosen != UsageConsent.IsGiven) Usage.SetConsent(form.UsageStatisticsChosen);
            }
            catch (System.IO.IOException error)
            {
                problem = error.Message;
            }
            catch (UnauthorizedAccessException error)
            {
                problem = error.Message;
            }
            if (problem == null) return;
            AppLog.Append("options not saved: " + problem);
            MessageBox.Show("Не удалось сохранить выбор проверки обновлений и статистики: " + problem, AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void OnSettingsPreview(StatusEffect effect)
        {
            Usage.Hit("settings.preview");
            StartPreview(effect);
        }

        private void StartPreview(StatusEffect effect)
        {
            previewEffect = effect;
            previewTimer.Stop();
            previewTimer.Start();
            Refresh();
        }

        private void StopPreview()
        {
            previewTimer.Stop();
            if (previewEffect == null) return;
            previewEffect = null;
            Refresh();
        }

        private void OnPreviewTick(object sender, EventArgs e)
        {
            StopPreview();
        }

        private void OnPauseClick(object sender, EventArgs e)
        {
            paused = !paused;
            Usage.Hit(paused ? "menu.pause_on" : "menu.pause_off");
            Usage.SetState("paused", paused);
            Refresh();
        }

        private void OnStartupClick(object sender, EventArgs e)
        {
            bool enabled = Autostart.IsEnabled();
            Usage.Hit(enabled ? "menu.autostart_off" : "menu.autostart_on");
            string problem = enabled ? Autostart.Disable() : Autostart.Enable(Application.ExecutablePath);
            Usage.SetState("autostart", Autostart.IsEnabled());
            if (problem != null) MessageBox.Show(problem, AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void OnResetClick(object sender, EventArgs e)
        {
            Usage.Hit("menu.reset_sessions");
            sources.Clear();
            UpdateLivenessTimer();
            Refresh("reset");
        }

        private void OnLogClick(object sender, EventArgs e)
        {
            Usage.Hit("menu.journal");
            if (!System.IO.File.Exists(AppLog.FilePath)) AppLog.Append("log created");
            try
            {
                using (Process.Start(SystemTools.Notepad, "\"" + AppLog.FilePath + "\""))
                {
                }
            }
            catch (System.ComponentModel.Win32Exception error)
            {
                AppLog.Append("notepad failed: " + error.Message);
                MessageBox.Show("Не удалось открыть Блокнот: " + error.Message + Environment.NewLine + "Журнал лежит в " + AppLog.FilePath,
                    AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OnExitClick(object sender, EventArgs e)
        {
            Usage.Hit("menu.exit");
            ExitThread();
        }

        private void LogOpenRgbState()
        {
            string state = lighting.ConnectionText;
            if (!lighting.IsConnected && !openRgbRestartRunning && !reconnectTimer.Enabled) reconnectTimer.Start();
            if (state == loggedOpenRgbState) return;
            loggedOpenRgbState = state;
            if (lighting.IsConnected) Usage.SetState("rgb_devices", lighting.DeviceCount);
            AppLog.Append(lighting.IsConnected
                ? "OpenRGB connected, devices: " + lighting.DeviceCount
                : "OpenRGB " + state + ": " + (lighting.LastError ?? "unknown reason"));
            UpdateTrayIcon();
        }

        private void OnRestartOpenRgbClick(object sender, EventArgs e)
        {
            Usage.Hit("menu.openrgb_restart");
            StartOpenRgbRestart(OpenRgbService.HasRestartTask());
        }

        private TrayBadge CurrentBadge()
        {
            if (openRgbRestartRunning) return repairBlinkOn ? TrayBadge.Repairing : TrayBadge.RepairingHidden;
            return AppProblem() == null ? TrayBadge.None : TrayBadge.Error;
        }

        private string AppProblem()
        {
            if (!lighting.IsConnected) return "OpenRGB: " + OpenRgbText();
            if (listener == null) return "порт " + eventPort + " занят — события не приходят";
            foreach (IAgentSource source in sources.All)
            {
                bool installed;
                if (source.Integration != null && integrationInstalled.TryGetValue(source.Id, out installed) && !installed)
                {
                    return source.DisplayName + " не подключён";
                }
            }
            return null;
        }

        private string OpenRgbText()
        {
            return openRgbRestartRunning ? RepairingText : lighting.ConnectionText;
        }

        private void StartOpenRgbRestart(bool useTask)
        {
            if (openRgbRestartRunning) return;
            openRgbRestartRunning = true;
            repairBlinkOn = false;
            repairBlinkTimer.Start();
            UpdateTrayIcon();
            AppLog.Append("OpenRGB service restart requested, " + (useTask ? "via scheduled task" : "via UAC"));
            System.Threading.ThreadPool.QueueUserWorkItem(state =>
            {
                string problem = useTask ? OpenRgbService.RestartWithTask() : OpenRgbService.RestartElevated(Application.ExecutablePath);
                try
                {
                    invoker.BeginInvoke(new Action(() => OnOpenRgbRestarted(problem)));
                }
                catch (InvalidOperationException)
                {
                }
            });
        }

        private void OnOpenRgbRestarted(string problem)
        {
            openRgbRestartRunning = false;
            repairBlinkTimer.Stop();
            AppLog.Append(problem == null ? "OpenRGB service restarted" : "OpenRGB service restart failed: " + problem);
            if (problem != null)
            {
                UpdateTrayIcon();
                ShowBalloon(5000, "Службу OpenRGB перезапустить не удалось: " + problem, ToolTipIcon.Warning);
                return;
            }
            Connect();
            if (lighting.IsConnected) Refresh();
            UpdateTrayIcon();
            ShowBalloon(5000, lighting.IsConnected
                ? "Служба OpenRGB перезапущена, подсветка снова работает."
                : "Служба OpenRGB перезапущена, " + AppTitle + " подключается к ней.", ToolTipIcon.Info);
        }

        private void OnHousekeepingTick(object sender, EventArgs e)
        {
            if (lighting.RefreshDeviceList())
            {
                AppLog.Append("OpenRGB device list changed, reloaded");
                Usage.SetState("rgb_devices", lighting.DeviceCount);
            }
            FlushSourceEvents();
            LogOpenRgbState();
            RefreshIntegrationStatus();
            bool changed = sources.Housekeep(DateTime.UtcNow, settings);
            UpdateLivenessTimer();
            if (ShowsDark() != dark)
            {
                AppLog.Append(dark ? "lights on" : "lights off: no events for " + settings.DarkAfterMinutes + " min");
                changed = true;
            }
            if (changed) Refresh("housekeeping");
            else UpdateTrayIcon();
        }

        private void OnLivenessTick(object sender, EventArgs e)
        {
            bool changed = sources.CheckLiveness(DateTime.UtcNow);
            UpdateLivenessTimer();
            if (changed) Refresh("liveness");
        }

        private void UpdateLivenessTimer()
        {
            int interval = sources.LivenessIntervalMs;
            if (livenessTimer.Interval != interval) livenessTimer.Interval = interval;
            livenessTimer.Enabled = sources.NeedsLivenessTimer;
        }

        private void OnReconnectTick(object sender, EventArgs e)
        {
            Connect();
            if (lighting.IsConnected) Refresh();
            else UpdateAnimationTimer();
            CheckOpenRgbHealth();
        }

        private void OnOpenRgbWatchTick(object sender, EventArgs e)
        {
            if (!lighting.IsConnected || openRgbRestartRunning) return;
            if (lighting.CheckAlive()) return;
            LogOpenRgbState();
            UpdateTrayIcon();
            reconnectTimer.Start();
        }

        private void CheckOpenRgbHealth()
        {
            bool unhealthy = false;
            if (!lighting.IsConnected && !openRgbRestartRunning)
            {
                System.ServiceProcess.ServiceControllerStatus? status = OpenRgbService.Status();
                unhealthy = status.HasValue && (lighting.NotResponding || status.Value != System.ServiceProcess.ServiceControllerStatus.Running);
            }
            if (!unhealthy)
            {
                openRgbUnhealthySinceUtc = null;
                return;
            }
            DateTime now = DateTime.UtcNow;
            if (!openRgbUnhealthySinceUtc.HasValue) openRgbUnhealthySinceUtc = now;
            if (now - openRgbUnhealthySinceUtc.Value < OpenRgbAutoRestartAfter) return;
            if (now - lastOpenRgbAutoRestartUtc < OpenRgbAutoRestartCooldown) return;
            lastOpenRgbAutoRestartUtc = now;
            if (OpenRgbService.HasRestartTask())
            {
                AppLog.Append("OpenRGB unhealthy for " + (int)(now - openRgbUnhealthySinceUtc.Value).TotalSeconds + " s (" + lighting.ConnectionText
                    + ", service " + OpenRgbService.Status() + ") — restarting service automatically");
                Usage.Hit("openrgb.auto_restart");
                ShowBalloon(5000, "OpenRGB перестал отвечать — перезапускаю его службу.", ToolTipIcon.Info);
                StartOpenRgbRestart(true);
                return;
            }
            AppLog.Append("OpenRGB unhealthy, no restart task — asking user");
            ShowBalloon(10000, "OpenRGB не отвечает. Нажми, чтобы перезапустить его службу (нужны права администратора).", ToolTipIcon.Warning);
            restartOnBalloonClick = true;
        }

        private void ShowBalloon(int timeoutMs, string text, ToolTipIcon icon)
        {
            restartOnBalloonClick = false;
            updateBalloonShown = false;
            trayIcon.ShowBalloonTip(timeoutMs, AppTitle, text, icon);
        }

        private void ShowUpdateBalloon(string version)
        {
            ShowBalloon(10000, "Вышла версия " + version + ". Нажми, чтобы скачать и установить.", ToolTipIcon.Info);
            updateBalloonShown = true;
        }

        private void OnBalloonClicked(object sender, EventArgs e)
        {
            if (updateBalloonShown)
            {
                updateBalloonShown = false;
                Usage.Hit("update.notify_open");
                UpdateForm.ShowSingle();
                return;
            }
            if (!restartOnBalloonClick) return;
            restartOnBalloonClick = false;
            if (lighting.IsConnected) return;
            Usage.Hit("notify.openrgb_restart");
            StartOpenRgbRestart(false);
        }

        private void CountSourceEvent(IAgentSource source)
        {
            string name = "source." + source.Id;
            int count;
            pendingSourceEvents.TryGetValue(name, out count);
            pendingSourceEvents[name] = count + 1;
        }

        private void FlushSourceEvents()
        {
            if (pendingSourceEvents.Count == 0) return;
            foreach (KeyValuePair<string, int> pair in pendingSourceEvents) Usage.Add(pair.Key, pair.Value);
            pendingSourceEvents.Clear();
        }

        private void UpdateUsageState()
        {
            Usage.SetState("autostart", Autostart.IsEnabled());
            Usage.SetState("auto_update", AutoUpdateCheck.IsEnabled);
            Usage.SetState("crash_reports", CrashReportConsent.IsGiven);
            foreach (SourceCatalog.Entry entry in SourceCatalog.All) Usage.SetState("source_" + entry.Id, settings.IsSourceEnabled(entry.Id));
            Usage.SetState("paused", paused);
            Usage.SetState("fixed_devices", settings.FixedDeviceNames == null ? 0 : settings.FixedDeviceNames.Count);
            Usage.SetState("idle_effect", settings.EffectFor(GlowStatus.Idle).Kind);
            Usage.SetState("effects_custom", EffectsCustomized());
            Usage.SetState("openrgb_service", OpenRgbService.IsInstalled());
        }

        private bool EffectsCustomized()
        {
            foreach (GlowStatus status in StatusCatalog.ByPriority)
            {
                if (!settings.EffectFor(status).SameAs(StatusCatalog.DefaultEffect(status))) return true;
            }
            return false;
        }
    }
}
