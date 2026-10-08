using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace AgentGlow
{
    internal sealed class SettingsForm : Form
    {
        private const int Gap = 12;
        private const int RowHeight = 32;
        private const int NameWidth = 210;
        private const int EffectWidth = 170;
        private const int ColorWidth = 60;
        private const int SpeedWidth = 56;
        private const int TestWidth = 96;

        private readonly AppSettings draft;
        private readonly Action<StatusEffect> preview;
        private readonly Dictionary<GlowStatus, ComboBox> effectBoxes = new Dictionary<GlowStatus, ComboBox>();
        private readonly Dictionary<GlowStatus, Button> colorButtons = new Dictionary<GlowStatus, Button>();
        private readonly Dictionary<GlowStatus, NumericUpDown> speedBoxes = new Dictionary<GlowStatus, NumericUpDown>();
        private readonly CheckedListBox deviceList = new CheckedListBox();
        private readonly CheckedListBox fixedList = new CheckedListBox();
        private readonly ComboBox fixedEffectBox = new ComboBox();
        private readonly Button fixedColorButton = new Button();
        private readonly NumericUpDown workingTimeoutBox = new NumericUpDown();
        private readonly NumericUpDown doneTimeoutBox = new NumericUpDown();
        private readonly NumericUpDown darkAfterBox = new NumericUpDown();
        private readonly NumericUpDown eventPortBox = new NumericUpDown();
        private readonly CheckBox launchOpenRgbBox = new CheckBox();

        public AppSettings Result;

        public SettingsForm(AppSettings settings, LightingController lighting, Action<StatusEffect> preview)
        {
            draft = settings;
            this.preview = preview;
            Text = "AgentGlow — настройки";
            Font = new Font("Segoe UI", 9f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            AutoScaleMode = AutoScaleMode.Dpi;

            int y = Gap;
            y = AddStatusRows(y);
            y = AddDevices(y + Gap, lighting);
            y = AddFixedDevices(y + Gap, lighting);
            y = AddBehavior(y + Gap);
            AddButtons(y + Gap);
        }

        private int AddStatusRows(int top)
        {
            int x = Gap + NameWidth;
            AddLabel("Эффект", x, top, EffectWidth);
            AddLabel("Цвет", x + EffectWidth + 8, top, ColorWidth);
            AddLabel("Скорость", x + EffectWidth + ColorWidth + 16, top, SpeedWidth + 20);
            int y = top + 22;
            foreach (GlowStatus status in StatusCatalog.ByPriority)
            {
                AddStatusRow(status, y);
                y += RowHeight;
            }
            return y;
        }

        private void AddStatusRow(GlowStatus status, int y)
        {
            StatusEffect effect = draft.EffectFor(status);
            AddLabel(StatusCatalog.DisplayName(status), Gap, y + 4, NameWidth);

            int x = Gap + NameWidth;
            var effectBox = new ComboBox();
            effectBox.DropDownStyle = ComboBoxStyle.DropDownList;
            effectBox.SetBounds(x, y, EffectWidth, 24);
            foreach (EffectKind kind in EffectCatalog.All)
            {
                effectBox.Items.Add(EffectCatalog.DisplayName(kind));
            }
            effectBox.SelectedIndex = Array.IndexOf(EffectCatalog.All, effect.Kind);
            effectBox.Tag = status;
            effectBox.SelectedIndexChanged += OnEffectChanged;
            Controls.Add(effectBox);
            effectBoxes[status] = effectBox;

            x += EffectWidth + 8;
            var colorButton = new Button();
            colorButton.SetBounds(x, y, ColorWidth, 24);
            colorButton.FlatStyle = FlatStyle.Flat;
            colorButton.BackColor = effect.Color;
            colorButton.Tag = status;
            colorButton.Click += OnColorClick;
            Controls.Add(colorButton);
            colorButtons[status] = colorButton;

            x += ColorWidth + 8;
            var speedBox = new NumericUpDown();
            speedBox.SetBounds(x, y, SpeedWidth, 24);
            speedBox.Minimum = StatusEffect.MinSpeed;
            speedBox.Maximum = StatusEffect.MaxSpeed;
            speedBox.Value = effect.SpeedLevel;
            Controls.Add(speedBox);
            speedBoxes[status] = speedBox;

            x += SpeedWidth + 8;
            var testButton = new Button();
            testButton.SetBounds(x, y, TestWidth, 24);
            testButton.Text = "Проверить";
            testButton.Tag = status;
            testButton.Click += OnTestClick;
            Controls.Add(testButton);

            UpdateRowState(status);
        }

        private int AddDevices(int top, LightingController lighting)
        {
            AddLabel("Устройства (ничего не отмечено — все видеокарты):", Gap, top, 420);
            deviceList.SetBounds(Gap, top + 22, FormContentWidth(), 84);
            deviceList.CheckOnClick = true;
            var names = new List<string>(lighting.ControllerNames());
            if (draft.DeviceNames != null)
            {
                foreach (string saved in draft.DeviceNames)
                {
                    if (!names.Contains(saved)) names.Add(saved);
                }
            }
            foreach (string name in names)
            {
                bool isChecked = draft.DeviceNames == null ? lighting.IsGpu(name) : draft.DeviceNames.Contains(name);
                deviceList.Items.Add(name, isChecked);
            }
            if (names.Count == 0) AddLabel("OpenRGB не подключён — список появится после подключения", Gap, top + 110, 420);
            Controls.Add(deviceList);
            return top + 22 + 84 + (names.Count == 0 ? 22 : 0);
        }

        private int AddFixedDevices(int top, LightingController lighting)
        {
            AddLabel("Постоянный свет (не участвуют в индикации):", Gap, top, 300);
            fixedEffectBox.DropDownStyle = ComboBoxStyle.DropDownList;
            fixedEffectBox.SetBounds(Gap + NameWidth + 100, top - 3, EffectWidth, 24);
            foreach (EffectKind kind in EffectCatalog.All)
            {
                fixedEffectBox.Items.Add(EffectCatalog.DisplayName(kind));
            }
            fixedEffectBox.SelectedIndex = Array.IndexOf(EffectCatalog.All, draft.FixedEffect.Kind);
            fixedEffectBox.SelectedIndexChanged += OnFixedEffectChanged;
            Controls.Add(fixedEffectBox);
            fixedColorButton.SetBounds(fixedEffectBox.Right + 8, top - 3, ColorWidth, 24);
            fixedColorButton.FlatStyle = FlatStyle.Flat;
            fixedColorButton.BackColor = draft.FixedEffect.Color;
            fixedColorButton.Click += OnColorClick;
            Controls.Add(fixedColorButton);
            UpdateFixedColorState();

            fixedList.SetBounds(Gap, top + 26, FormContentWidth(), 64);
            fixedList.CheckOnClick = true;
            var names = new List<string>(lighting.ControllerNames());
            if (draft.FixedDeviceNames != null)
            {
                foreach (string saved in draft.FixedDeviceNames)
                {
                    if (!names.Contains(saved)) names.Add(saved);
                }
            }
            foreach (string name in names)
            {
                fixedList.Items.Add(name, draft.FixedDeviceNames != null && draft.FixedDeviceNames.Contains(name));
            }
            Controls.Add(fixedList);
            return top + 26 + 64;
        }

        private void OnFixedEffectChanged(object sender, EventArgs e)
        {
            UpdateFixedColorState();
        }

        private void UpdateFixedColorState()
        {
            fixedColorButton.Visible = EffectCatalog.UsesColor(SelectedFixedKind());
        }

        private EffectKind SelectedFixedKind()
        {
            int index = fixedEffectBox.SelectedIndex;
            return index < 0 ? EffectKind.Original : EffectCatalog.All[index];
        }

        private int AddBehavior(int top)
        {
            int y = top;
            AddNumeric("Сбрасывать «Работаю» без событий через, мин (0 — никогда):", workingTimeoutBox, y, draft.WorkingTimeoutMinutes, 0, 1440);
            y += RowHeight;
            AddNumeric("Сбрасывать «Закончил» через, мин (0 — никогда):", doneTimeoutBox, y, draft.DoneTimeoutMinutes, 0, 1440);
            y += RowHeight;
            AddNumeric("Гасить, если ни от одного треда нет событий, мин (0 — никогда):", darkAfterBox, y, draft.DarkAfterMinutes, 0, 1440);
            y += RowHeight;
            AddNumeric("Порт приёма событий (применится после перезапуска):", eventPortBox, y, draft.EventPort, 1024, 65535);
            y += RowHeight;
            launchOpenRgbBox.Text = "Запускать OpenRGB, если он не запущен";
            launchOpenRgbBox.Checked = draft.LaunchOpenRgb;
            launchOpenRgbBox.SetBounds(Gap, y, 400, 24);
            Controls.Add(launchOpenRgbBox);
            return y + RowHeight;
        }

        private void AddButtons(int top)
        {
            var cancel = new Button();
            cancel.Text = "Отмена";
            cancel.DialogResult = DialogResult.Cancel;
            cancel.Click += OnCancelClick;
            cancel.SetBounds(Gap + FormContentWidth() - 100, top, 100, 28);
            var save = new Button();
            save.Text = "Сохранить";
            save.SetBounds(cancel.Left - 108, top, 100, 28);
            save.Click += OnSaveClick;
            Controls.Add(save);
            Controls.Add(cancel);
            AcceptButton = save;
            CancelButton = cancel;
            ClientSize = new Size(FormContentWidth() + 2 * Gap, top + 28 + Gap);
        }

        private void AddNumeric(string caption, NumericUpDown box, int y, int value, int min, int max)
        {
            AddLabel(caption, Gap, y + 4, FormContentWidth() - 90);
            box.Minimum = min;
            box.Maximum = max;
            box.Value = Math.Max(min, Math.Min(max, value));
            box.SetBounds(Gap + FormContentWidth() - 80, y, 80, 24);
            Controls.Add(box);
        }

        private void AddLabel(string text, int x, int y, int width)
        {
            var label = new Label();
            label.Text = text;
            label.AutoEllipsis = true;
            label.SetBounds(x, y, width, 20);
            Controls.Add(label);
        }

        private static int FormContentWidth()
        {
            return NameWidth + EffectWidth + ColorWidth + SpeedWidth + TestWidth + 24;
        }

        private void OnEffectChanged(object sender, EventArgs e)
        {
            UpdateRowState((GlowStatus)((Control)sender).Tag);
        }

        private void UpdateRowState(GlowStatus status)
        {
            EffectKind kind = SelectedKind(status);
            colorButtons[status].Enabled = EffectCatalog.UsesColor(kind);
            colorButtons[status].Visible = EffectCatalog.UsesColor(kind);
            speedBoxes[status].Enabled = EffectCatalog.UsesSpeed(kind);
        }

        private void OnColorClick(object sender, EventArgs e)
        {
            var button = (Button)sender;
            using (var dialog = new ColorDialog())
            {
                dialog.Color = button.BackColor;
                dialog.FullOpen = true;
                if (dialog.ShowDialog(this) == DialogResult.OK) button.BackColor = dialog.Color;
            }
        }

        private void OnTestClick(object sender, EventArgs e)
        {
            preview(EffectFromRow((GlowStatus)((Control)sender).Tag));
        }

        private void OnSaveClick(object sender, EventArgs e)
        {
            foreach (GlowStatus status in StatusCatalog.ByPriority)
            {
                draft.Effects[status] = EffectFromRow(status);
            }
            var names = new List<string>();
            foreach (object item in deviceList.CheckedItems)
            {
                names.Add((string)item);
            }
            draft.DeviceNames = names.Count == 0 ? null : names;
            var fixedNames = new List<string>();
            foreach (object item in fixedList.CheckedItems)
            {
                fixedNames.Add((string)item);
            }
            draft.FixedDeviceNames = fixedNames.Count == 0 ? null : fixedNames;
            draft.FixedEffect = new StatusEffect(SelectedFixedKind(), StatusEffect.ToRgb(fixedColorButton.BackColor), 3);
            draft.WorkingTimeoutMinutes = (int)workingTimeoutBox.Value;
            draft.DoneTimeoutMinutes = (int)doneTimeoutBox.Value;
            draft.DarkAfterMinutes = (int)darkAfterBox.Value;
            draft.EventPort = (int)eventPortBox.Value;
            draft.LaunchOpenRgb = launchOpenRgbBox.Checked;
            Result = draft;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void OnCancelClick(object sender, EventArgs e)
        {
            Close();
        }

        private StatusEffect EffectFromRow(GlowStatus status)
        {
            return new StatusEffect(SelectedKind(status), StatusEffect.ToRgb(colorButtons[status].BackColor),
                (int)speedBoxes[status].Value);
        }

        private EffectKind SelectedKind(GlowStatus status)
        {
            int index = effectBoxes[status].SelectedIndex;
            return index < 0 ? EffectKind.Original : EffectCatalog.All[index];
        }
    }
}
