using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace ClaudeGlow
{
    internal sealed class SetupForm : Form
    {
        private const string OpenRgbUrl = "https://openrgb.org";

        private readonly Label status = new Label();
        private readonly ProgressBar progress = new ProgressBar();
        private readonly CheckBox hooks = new CheckBox();
        private readonly CheckBox autostart = new CheckBox();
        private readonly Button install = new Button();

        public SetupForm()
        {
            Text = "Установка " + AppIdentity.Name + " " + AppIdentity.Version;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = SystemFonts.MessageBoxFont;
            ClientSize = new Size(560, 360);
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            Controls.Add(layout);
            layout.Controls.Add(new Label
            {
                AutoSize = true,
                MaximumSize = new Size(520, 0),
                Text = "Подсветка RGB-устройств показывает, что делает Claude Code: работает, закончил, ждёт ответа "
                    + "или разрешения. Глянул на подсветку — понял, нужно ли разворачивать окно.\r\n\r\n"
                    + "Программа ставится в %LOCALAPPDATA%\\Programs\\ClaudeGlow, права администратора не нужны."
            });

            if (!OpenRgbPresence.IsInstalled())
            {
                var link = new LinkLabel
                {
                    AutoSize = true,
                    MaximumSize = new Size(520, 0),
                    Margin = new Padding(0, 12, 0, 0),
                    Text = "OpenRGB не найден. ClaudeGlow управляет подсветкой через него — поставь OpenRGB с " + OpenRgbUrl
                        + " и включи в нём SDK-сервер."
                };
                link.LinkArea = new LinkArea(link.Text.IndexOf(OpenRgbUrl, StringComparison.Ordinal), OpenRgbUrl.Length);
                link.LinkClicked += (sender, e) => Process.Start(OpenRgbUrl);
                layout.Controls.Add(link);
            }

            hooks.AutoSize = true;
            hooks.MaximumSize = new Size(520, 0);
            hooks.Checked = true;
            hooks.Margin = new Padding(0, 14, 0, 0);
            hooks.Text = "Прописать хуки в настройки Claude Code (~\\.claude\\settings.json)";
            layout.Controls.Add(hooks);

            autostart.AutoSize = true;
            autostart.Checked = true;
            autostart.Text = "Запускать при входе в Windows";
            layout.Controls.Add(autostart);

            progress.Dock = DockStyle.Fill;
            progress.Margin = new Padding(0, 16, 0, 4);
            layout.Controls.Add(progress);
            status.AutoSize = true;
            status.MaximumSize = new Size(520, 0);
            layout.Controls.Add(status);

            install.Text = "Установить";
            install.AutoSize = true;
            install.Anchor = AnchorStyles.Right;
            install.Click += OnInstall;
            layout.Controls.Add(install);
            AcceptButton = install;
        }

        private void OnInstall(object sender, EventArgs e)
        {
            install.Enabled = false;
            hooks.Enabled = false;
            autostart.Enabled = false;
            var installation = new Installation(hooks.Checked, autostart.Checked, Report);
            var worker = new Thread(() =>
            {
                Exception failure = null;
                try
                {
                    installation.Run();
                }
                catch (Exception error)
                {
                    failure = error;
                }
                BeginInvoke(new Action(() => Finish(installation, failure)));
            });
            worker.IsBackground = true;
            worker.Start();
        }

        private void Report(int percent, string message)
        {
            BeginInvoke(new Action(() =>
            {
                progress.Value = Math.Max(0, Math.Min(100, percent));
                status.Text = message;
            }));
        }

        private void Finish(Installation installation, Exception failure)
        {
            if (failure != null)
            {
                status.Text = "Ошибка: " + failure.Message;
                install.Enabled = true;
                MessageBox.Show(this, failure.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            string message = AppIdentity.Name + " установлен и запущен — кружок в трее.";
            if (installation.Notes.Count > 0) message += "\r\n\r\n" + string.Join("\r\n\r\n", installation.Notes);
            MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            Close();
        }
    }
}
