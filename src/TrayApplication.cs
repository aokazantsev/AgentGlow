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
        private const int LivenessIntervalMs = 5 * 1000;
        private const int AnimationIntervalMs = 40;
        private const int OpenRgbWatchIntervalMs = 5000;
        private const int RepairBlinkIntervalMs = 500;
        private const string RepairingText = "перезапускаю службу…";
        private static readonly TimeSpan LaunchRetryInterval = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan OpenRgbAutoRestartAfter = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan OpenRgbAutoRestartCooldown = TimeSpan.FromMinutes(10);
        private static readonly Color NeutralColor = Color.FromArgb(128, 128, 128);
        private static readonly StatusEffect PausedEffect = new StatusEffect(EffectKind.Original, 0xFFFFFF, 3);
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
        private SourceSet sources;
        private AppSettings settings;
        private EventListener listener;
        private StatusEffect previewEffect;
        private bool repairBlinkOn;
        private bool paused;
        private bool dark;
        private string loggedOpenRgbState;
        private bool openRgbRestartRunning;
        private DateTime? openRgbUnhealthySinceUtc;
        private DateTime lastOpenRgbAutoRestartUtc = DateTime.MinValue;
        private bool restartOnBalloonClick;
        private DateTime lastActivityUtc = DateTime.UtcNow;
        private DateTime lastLaunchAttemptUtc = DateTime.MinValue;
        private SettingsForm settingsForm;

        public TrayApplication()
        {
            AppLog.Append("start " + AppIdentity.Version + ", user=" + Environment.UserDomainName + "\\" + Environment.UserName
                + ", os=" + Environment.OSVersion.VersionString + ", exe=" + Application.ExecutablePath);
            bool firstRun = !AppSettings.FileExists;
            settings = AppSettings.Load();
            if (firstRun)
            {
                settings.TrySave();
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
            trayIcon.BalloonTipClosed += (sender, e) => restartOnBalloonClick = false;
            trayIcon.Visible = true;

            housekeepingTimer.Interval = HousekeepingIntervalMs;
            housekeepingTimer.Tick += OnHousekeepingTick;
            housekeepingTimer.Start();
            reconnectTimer.Interval = ReconnectIntervalMs;
            reconnectTimer.Tick += OnReconnectTick;
            previewTimer.Interval = PreviewDurationMs;
            previewTimer.Tick += OnPreviewTick;
            livenessTimer.Interval = LivenessIntervalMs;
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
            Refresh();
        }

        protected override void ExitThreadCore()
        {
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
            lighting.RestoreOriginals();
            lighting.Dispose();
            trayIcon.Visible = false;
            trayIcon.Dispose();
            invoker.Dispose();
            base.ExitThreadCore();
        }

        private void OnSessionEnding(object sender, SessionEndingEventArgs e)
        {
            lighting.RestoreOriginals();
        }

        private void StartListener()
        {
            listener = new EventListener(settings.EventPort, sources, OnSourceRequest);
            try
            {
                listener.Start();
                AppLog.Append("event listener on 127.0.0.1:" + settings.EventPort);
            }
            catch (SocketException exception)
            {
                listener = null;
                string message = "Порт " + settings.EventPort + " занят, события приложений не принимаются: " + exception.Message;
                AppLog.Append(message);
                trayIcon.ShowBalloonTip(5000, AppTitle, message, ToolTipIcon.Warning);
            }
        }

        private void OnSourceRequest(IAgentSource source, EventRequest request)
        {
            try
            {
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
            bool changed = source.Handle(request, now) || dark;
            UpdateLivenessTimer();
            if (changed) Refresh();
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
            if (previewEffect != null) lighting.Apply(previewEffect, null);
            else if (paused) lighting.Apply(PausedEffect, null);
            else if (IsDark()) lighting.Apply(DarkEffect, null);
            else lighting.Apply(settings.EffectFor(sources.TopStatus), SecondaryEffect());
            dark = previewEffect == null && !paused && IsDark();
            if (!lighting.IsConnected) reconnectTimer.Start();
            UpdateAnimationTimer();
            UpdateTrayIcon();
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
                integrationInstalled[source.Id] = source.Integration.IsInstalled(settings.EventPort);
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
            menu.Items.Add(new ToolStripMenuItem("Проверить обновление…", null, (s, a) => UpdateForm.ShowSingle()));
            menu.Items.Add(new ToolStripMenuItem("О приложении…", null, (s, a) => AboutForm.ShowSingle()));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Выход", null, OnExitClick));
            e.Cancel = false;
        }

        private void OnInstallIntegrationClick(IAgentSource source)
        {
            IIntegration integration = source.Integration;
            try
            {
                integration.Install(settings.EventPort);
                integrationInstalled[source.Id] = true;
                UpdateTrayIcon();
                AppLog.Append(source.DisplayName + " integration installed into " + integration.Location);
                trayIcon.ShowBalloonTip(5000, AppTitle, source.DisplayName + ": подключено (" + integration.Location + "). Перезапусти " + source.DisplayName + ", чтобы подхватить.", ToolTipIcon.Info);
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
            if (e.Button == MouseButtons.Left) ShowSettings();
        }

        private void OnSettingsClick(object sender, EventArgs e)
        {
            ShowSettings();
        }

        private void ShowSettings()
        {
            if (settingsForm != null)
            {
                settingsForm.Activate();
                return;
            }
            settingsForm = new SettingsForm(settings.Clone(), lighting, StartPreview);
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
            if (!settings.TrySave())
            {
                MessageBox.Show("Не удалось сохранить settings.txt", AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            lighting.Configure(settings.DeviceNames, settings.FixedDeviceNames, settings.FixedEffect);
            sources.Housekeep(DateTime.UtcNow, settings);
            Refresh();
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
            Refresh();
        }

        private void OnStartupClick(object sender, EventArgs e)
        {
            string problem = Autostart.IsEnabled() ? Autostart.Disable() : Autostart.Enable(Application.ExecutablePath);
            if (problem != null) MessageBox.Show(problem, AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void OnResetClick(object sender, EventArgs e)
        {
            sources.Clear();
            UpdateLivenessTimer();
            Refresh();
        }

        private void OnLogClick(object sender, EventArgs e)
        {
            if (!System.IO.File.Exists(AppLog.FilePath)) AppLog.Append("log created");
            using (Process.Start("notepad.exe", "\"" + AppLog.FilePath + "\""))
            {
            }
        }

        private void OnExitClick(object sender, EventArgs e)
        {
            ExitThread();
        }

        private void LogOpenRgbState()
        {
            string state = lighting.ConnectionText;
            if (!lighting.IsConnected && !openRgbRestartRunning && !reconnectTimer.Enabled) reconnectTimer.Start();
            if (state == loggedOpenRgbState) return;
            loggedOpenRgbState = state;
            AppLog.Append(lighting.IsConnected
                ? "OpenRGB connected, devices: " + lighting.DeviceCount
                : "OpenRGB " + state + ": " + (lighting.LastError ?? "unknown reason"));
            UpdateTrayIcon();
        }

        private void OnRestartOpenRgbClick(object sender, EventArgs e)
        {
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
            if (listener == null) return "порт " + settings.EventPort + " занят — события не приходят";
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
                trayIcon.ShowBalloonTip(5000, AppTitle, "Службу OpenRGB перезапустить не удалось: " + problem, ToolTipIcon.Warning);
                return;
            }
            Connect();
            if (lighting.IsConnected) Refresh();
            UpdateTrayIcon();
            trayIcon.ShowBalloonTip(5000, AppTitle, lighting.IsConnected
                ? "Служба OpenRGB перезапущена, подсветка снова работает."
                : "Служба OpenRGB перезапущена, " + AppTitle + " подключается к ней.", ToolTipIcon.Info);
        }

        private void OnHousekeepingTick(object sender, EventArgs e)
        {
            if (lighting.RefreshDeviceList()) AppLog.Append("OpenRGB device list changed, reloaded");
            LogOpenRgbState();
            RefreshIntegrationStatus();
            bool changed = sources.Housekeep(DateTime.UtcNow, settings);
            UpdateLivenessTimer();
            if (IsDark() != dark)
            {
                AppLog.Append(dark ? "lights on" : "lights off: no events for " + settings.DarkAfterMinutes + " min");
                changed = true;
            }
            if (changed) Refresh();
            else UpdateTrayIcon();
        }

        private void OnLivenessTick(object sender, EventArgs e)
        {
            bool changed = sources.CheckLiveness(DateTime.UtcNow);
            UpdateLivenessTimer();
            if (changed) Refresh();
        }

        private void UpdateLivenessTimer()
        {
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
                trayIcon.ShowBalloonTip(5000, AppTitle, "OpenRGB перестал отвечать — перезапускаю его службу.", ToolTipIcon.Info);
                StartOpenRgbRestart(true);
                return;
            }
            AppLog.Append("OpenRGB unhealthy, no restart task — asking user");
            restartOnBalloonClick = true;
            trayIcon.ShowBalloonTip(10000, AppTitle, "OpenRGB не отвечает. Нажми, чтобы перезапустить его службу (нужны права администратора).", ToolTipIcon.Warning);
        }

        private void OnBalloonClicked(object sender, EventArgs e)
        {
            if (!restartOnBalloonClick) return;
            restartOnBalloonClick = false;
            StartOpenRgbRestart(false);
        }
    }
}
