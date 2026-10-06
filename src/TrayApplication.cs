using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Net.Sockets;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ClaudeGlow
{
    internal sealed class TrayApplication : ApplicationContext
    {
        private const string AppTitle = "ClaudeGlow";
        private const int MaxTrayTextLength = 63;
        private const int MaxMenuSessions = 12;
        private const int HousekeepingIntervalMs = 15 * 1000;
        private const int ReconnectIntervalMs = 5 * 1000;
        private const int PreviewDurationMs = 5 * 1000;
        private const int LivenessIntervalMs = 5 * 1000;
        private const int AnimationIntervalMs = 40;
        private static readonly TimeSpan LaunchRetryInterval = TimeSpan.FromMinutes(1);
        private static readonly Color NeutralColor = Color.FromArgb(128, 128, 128);
        private static readonly StatusEffect PausedEffect = new StatusEffect(EffectKind.Original, 0xFFFFFF, 3);
        private static readonly StatusEffect DarkEffect = new StatusEffect(EffectKind.Off, 0x000000, 3);

        private readonly NotifyIcon trayIcon = new NotifyIcon();
        private readonly Control invoker = new Control();
        private readonly SessionTracker tracker = new SessionTracker();
        private readonly LightingController lighting = new LightingController();
        private readonly Timer housekeepingTimer = new Timer();
        private readonly Timer reconnectTimer = new Timer();
        private readonly Timer previewTimer = new Timer();
        private readonly Timer livenessTimer = new Timer();
        private readonly Timer animationTimer = new Timer();
        private readonly Stopwatch animationClock = new Stopwatch();
        private AppSettings settings;
        private HookListener listener;
        private StatusEffect previewEffect;
        private bool paused;
        private bool dark;
        private DateTime lastActivityUtc = DateTime.UtcNow;
        private DateTime lastLaunchAttemptUtc = DateTime.MinValue;
        private SettingsForm settingsForm;

        public TrayApplication()
        {
            bool firstRun = !AppSettings.FileExists;
            settings = AppSettings.Load();
            if (firstRun)
            {
                settings.TrySave();
                StartupRegistration.TrySetEnabled(true);
            }
            invoker.CreateControl();
            trayIcon.ContextMenuStrip = new ContextMenuStrip();
            trayIcon.ContextMenuStrip.Opening += OnMenuOpening;
            trayIcon.MouseDoubleClick += OnTrayDoubleClick;
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
            tracker.Restore(SessionStore.Load());
            if (tracker.LatestEventUtc > DateTime.MinValue) lastActivityUtc = tracker.LatestEventUtc;
            tracker.Expire(DateTime.UtcNow, settings.WorkingTimeoutMinutes, settings.DoneTimeoutMinutes);
            UpdateLivenessTimer();
            lighting.Configure(settings.DeviceNames, settings.FixedDeviceNames, settings.FixedEffect);
            StartListener();
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
            listener = new HookListener(settings.HookPort, OnHookBody);
            try
            {
                listener.Start();
            }
            catch (SocketException exception)
            {
                listener = null;
                string message = "Порт " + settings.HookPort + " занят, события Claude не принимаются: " + exception.Message;
                HookLog.Append(message);
                trayIcon.ShowBalloonTip(5000, AppTitle, message, ToolTipIcon.Warning);
            }
        }

        private void OnHookBody(string body, ProcessIdentity claudeProcess)
        {
            try
            {
                invoker.BeginInvoke(new Action<string, ProcessIdentity>(HandleHookBody), body, claudeProcess);
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void HandleHookBody(string body, ProcessIdentity claudeProcess)
        {
            HookEvent hookEvent = HookEventParser.Parse(body);
            if (hookEvent == null)
            {
                HookLog.Append("unparsed event, " + body.Length + " chars");
                return;
            }
            lastActivityUtc = DateTime.UtcNow;
            bool changed = tracker.Apply(hookEvent, claudeProcess, DateTime.UtcNow) || dark;
            HookLog.Append(hookEvent.EventName + " " + (hookEvent.NotificationType ?? hookEvent.ToolName ?? "")
                + (hookEvent.EventName == "Stop" ? "bg=" + hookEvent.BackgroundTaskCount : "")
                + " session=" + ShortId(hookEvent.SessionId)
                + " pid=" + (claudeProcess == null ? "?" : claudeProcess.ProcessId.ToString())
                + (hookEvent.AgentId == null ? "" : " agent=" + ShortId(hookEvent.AgentId))
                + " session:" + tracker.StatusOf(hookEvent.SessionId)
                + " -> " + tracker.TopStatus);
            UpdateLivenessTimer();
            SessionStore.Save(tracker.Snapshot());
            if (changed) Refresh();
        }

        private void Connect()
        {
            if (lighting.TryConnect())
            {
                reconnectTimer.Stop();
                return;
            }
            if (settings.LaunchOpenRgb && DateTime.UtcNow - lastLaunchAttemptUtc > LaunchRetryInterval)
            {
                lastLaunchAttemptUtc = DateTime.UtcNow;
                if (OpenRgbLauncher.TryStart(settings.OpenRgbPath)) HookLog.Append("OpenRGB started");
            }
            reconnectTimer.Start();
        }

        private void Refresh()
        {
            if (previewEffect != null) lighting.Apply(previewEffect, null);
            else if (paused) lighting.Apply(PausedEffect, null);
            else if (IsDark()) lighting.Apply(DarkEffect, null);
            else lighting.Apply(settings.EffectFor(tracker.TopStatus), SecondaryEffect());
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
            GlowStatus top = tracker.TopStatus;
            bool needsUser = top == GlowStatus.Error || top == GlowStatus.Permission || top == GlowStatus.Question;
            return settings.DarkAfterMinutes > 0 && !needsUser
                && DateTime.UtcNow - lastActivityUtc >= TimeSpan.FromMinutes(settings.DarkAfterMinutes);
        }

        private bool IsSplit()
        {
            GlowStatus top = tracker.TopStatus;
            return !paused && previewEffect == null && !IsDark()
                && (top == GlowStatus.Done || top == GlowStatus.DoneIdle)
                && tracker.Has(GlowStatus.Working);
        }

        private string StatusText()
        {
            if (paused) return "пауза";
            if (IsDark()) return "погашено, нет событий " + settings.DarkAfterMinutes + " мин";
            string text = StatusCatalog.DisplayName(tracker.TopStatus);
            return IsSplit() ? text + " + " + StatusCatalog.DisplayName(GlowStatus.Working) : text;
        }

        private Color IconColor(GlowStatus status)
        {
            StatusEffect effect = settings.EffectFor(status);
            return paused || IsDark() || !EffectCatalog.UsesColor(effect.Kind) ? NeutralColor : effect.Color;
        }

        private void UpdateTrayIcon()
        {
            Color left = IconColor(tracker.TopStatus);
            Color right = IsSplit() ? IconColor(GlowStatus.Working) : left;
            trayIcon.Icon = StatusIconPainter.Paint(left, right, lighting.IsConnected);
            string text = AppTitle + ": " + StatusText();
            if (!lighting.IsConnected) text += "\nOpenRGB: нет связи";
            trayIcon.Text = text.Length > MaxTrayTextLength ? text.Substring(0, MaxTrayTextLength) : text;
        }

        private void OnMenuOpening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            ContextMenuStrip menu = trayIcon.ContextMenuStrip;
            menu.Items.Clear();
            menu.Items.Add(Disabled("Статус: " + StatusText()));
            menu.Items.Add(Disabled("OpenRGB: " + (lighting.IsConnected ? "подключено" : "нет связи")));
            if (ClaudeHooks.AreInstalled(settings.HookPort))
            {
                menu.Items.Add(Disabled("Хуки Claude Code: подключены"));
            }
            else
            {
                menu.Items.Add(new ToolStripMenuItem("Хуки Claude Code не подключены — подключить", null, OnInstallHooksClick));
            }
            List<SessionState> sessions = tracker.SessionsByStatus();
            if (sessions.Count > 0) menu.Items.Add(new ToolStripSeparator());
            for (int i = 0; i < sessions.Count && i < MaxMenuSessions; i++)
            {
                menu.Items.Add(Disabled(sessions[i].Project + " — " + StatusCatalog.DisplayName(sessions[i].Status)));
            }
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Настройки…", null, OnSettingsClick));
            var pauseItem = new ToolStripMenuItem("Приостановить индикацию", null, OnPauseClick);
            pauseItem.Checked = paused;
            menu.Items.Add(pauseItem);
            var startupItem = new ToolStripMenuItem("Запускать при входе в Windows", null, OnStartupClick);
            startupItem.Checked = StartupRegistration.IsEnabled();
            menu.Items.Add(startupItem);
            menu.Items.Add(new ToolStripMenuItem("Сбросить статусы сессий", null, OnResetClick));
            menu.Items.Add(new ToolStripMenuItem("Журнал событий", null, OnLogClick));
            menu.Items.Add(new ToolStripMenuItem("О приложении…", null, (s, a) => AboutForm.ShowSingle()));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Выход", null, OnExitClick));
            e.Cancel = false;
        }

        private void OnInstallHooksClick(object sender, EventArgs e)
        {
            try
            {
                ClaudeHooks.Install(settings.HookPort);
                HookLog.Append("hooks installed into " + ClaudeHooks.SettingsPath);
                trayIcon.ShowBalloonTip(5000, AppTitle, "Хуки прописаны в " + ClaudeHooks.SettingsPath + ". Новые сессии Claude Code начнут слать события.", ToolTipIcon.Info);
            }
            catch (Exception error)
            {
                MessageBox.Show("Не удалось прописать хуки в " + ClaudeHooks.SettingsPath + ": " + error.Message, AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            tracker.Expire(DateTime.UtcNow, settings.WorkingTimeoutMinutes, settings.DoneTimeoutMinutes);
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
            if (!StartupRegistration.TrySetEnabled(!StartupRegistration.IsEnabled()))
            {
                MessageBox.Show("Не удалось изменить автозапуск", AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OnResetClick(object sender, EventArgs e)
        {
            tracker.Clear();
            SessionStore.Save(tracker.Snapshot());
            UpdateLivenessTimer();
            Refresh();
        }

        private void OnLogClick(object sender, EventArgs e)
        {
            if (!System.IO.File.Exists(HookLog.FilePath)) HookLog.Append("log created");
            using (Process.Start("notepad.exe", "\"" + HookLog.FilePath + "\""))
            {
            }
        }

        private void OnExitClick(object sender, EventArgs e)
        {
            ExitThread();
        }

        private void OnHousekeepingTick(object sender, EventArgs e)
        {
            if (lighting.RefreshDeviceList()) HookLog.Append("OpenRGB device list changed, reloaded");
            bool changed = tracker.Expire(DateTime.UtcNow, settings.WorkingTimeoutMinutes, settings.DoneTimeoutMinutes);
            if (changed) HookLog.Append("timeouts -> " + tracker.TopStatus);
            if (changed) SessionStore.Save(tracker.Snapshot());
            UpdateLivenessTimer();
            if (IsDark() != dark)
            {
                HookLog.Append(dark ? "lights on" : "lights off: no events for " + settings.DarkAfterMinutes + " min");
                changed = true;
            }
            if (changed) Refresh();
        }

        private void OnLivenessTick(object sender, EventArgs e)
        {
            bool changed = tracker.RemoveEndedProcesses();
            if (changed) HookLog.Append("claude process ended -> " + tracker.TopStatus);
            if (tracker.PromoteApprovedPermissions(DateTime.UtcNow))
            {
                HookLog.Append("permission approved, tool started -> " + tracker.TopStatus);
                changed = true;
            }
            if (changed) SessionStore.Save(tracker.Snapshot());
            UpdateLivenessTimer();
            if (changed) Refresh();
        }

        private void UpdateLivenessTimer()
        {
            livenessTimer.Enabled = tracker.HasTrackedProcesses;
        }

        private void OnReconnectTick(object sender, EventArgs e)
        {
            Connect();
            if (lighting.IsConnected) Refresh();
            else UpdateAnimationTimer();
        }

        private static string ShortId(string sessionId)
        {
            return sessionId.Length > 8 ? sessionId.Substring(0, 8) : sessionId;
        }
    }
}
