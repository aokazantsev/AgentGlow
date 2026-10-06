using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace ClaudeGlow
{
    internal sealed class AppSettings
    {
        public const int DefaultHookPort = 47651;
        public const string DefaultOpenRgbPath = @"C:\Program Files\OpenRGB\OpenRGB.exe";

        private const string HookPortKey = "hookPort";
        private const string DevicesKey = "devices";
        private const string WorkingTimeoutKey = "workingTimeoutMinutes";
        private const string DoneTimeoutKey = "doneTimeoutMinutes";
        private const string LaunchOpenRgbKey = "launchOpenRgb";
        private const string OpenRgbPathKey = "openRgbPath";
        private const string EffectKeyPrefix = "effect.";
        private const string FixedDevicesKey = "fixedDevices";
        private const string DarkAfterKey = "darkAfterMinutes";
        private const string FixedEffectKey = "fixedEffect";
        private const char KeyValueSeparator = '=';
        private const char DeviceSeparator = '|';

        private static readonly string FilePath = UserDataPaths.File("settings.txt");

        public int HookPort = DefaultHookPort;
        public List<string> DeviceNames;
        public int WorkingTimeoutMinutes = 30;
        public int DoneTimeoutMinutes;
        public int DarkAfterMinutes = 30;
        public bool LaunchOpenRgb = true;
        public string OpenRgbPath = DefaultOpenRgbPath;
        public List<string> FixedDeviceNames;
        public StatusEffect FixedEffect = new StatusEffect(EffectKind.Off, 0x000000, 3);
        public readonly Dictionary<GlowStatus, StatusEffect> Effects = new Dictionary<GlowStatus, StatusEffect>();

        public AppSettings()
        {
            foreach (GlowStatus status in StatusCatalog.ByPriority)
            {
                Effects[status] = StatusCatalog.DefaultEffect(status);
            }
        }

        public static bool FileExists
        {
            get { return File.Exists(FilePath); }
        }

        public static AppSettings Load()
        {
            var settings = new AppSettings();
            if (!File.Exists(FilePath)) return settings;
            try
            {
                foreach (string line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    int separator = line.IndexOf(KeyValueSeparator);
                    if (separator <= 0) continue;
                    settings.Apply(line.Substring(0, separator).Trim(), line.Substring(separator + 1).Trim());
                }
            }
            catch (IOException)
            {
            }
            return settings;
        }

        public StatusEffect EffectFor(GlowStatus status)
        {
            return Effects[status];
        }

        public AppSettings Clone()
        {
            var copy = new AppSettings();
            copy.HookPort = HookPort;
            copy.DeviceNames = DeviceNames == null ? null : new List<string>(DeviceNames);
            copy.WorkingTimeoutMinutes = WorkingTimeoutMinutes;
            copy.DoneTimeoutMinutes = DoneTimeoutMinutes;
            copy.DarkAfterMinutes = DarkAfterMinutes;
            copy.LaunchOpenRgb = LaunchOpenRgb;
            copy.OpenRgbPath = OpenRgbPath;
            copy.FixedDeviceNames = FixedDeviceNames == null ? null : new List<string>(FixedDeviceNames);
            copy.FixedEffect = FixedEffect;
            foreach (KeyValuePair<GlowStatus, StatusEffect> pair in Effects)
            {
                copy.Effects[pair.Key] = pair.Value;
            }
            return copy;
        }

        public bool TrySave()
        {
            var content = new StringBuilder();
            AppendLine(content, HookPortKey, HookPort.ToString(CultureInfo.InvariantCulture));
            if (DeviceNames != null) AppendLine(content, DevicesKey, string.Join(DeviceSeparator.ToString(), DeviceNames));
            AppendLine(content, WorkingTimeoutKey, WorkingTimeoutMinutes.ToString(CultureInfo.InvariantCulture));
            AppendLine(content, DoneTimeoutKey, DoneTimeoutMinutes.ToString(CultureInfo.InvariantCulture));
            AppendLine(content, DarkAfterKey, DarkAfterMinutes.ToString(CultureInfo.InvariantCulture));
            AppendLine(content, LaunchOpenRgbKey, LaunchOpenRgb ? "1" : "0");
            AppendLine(content, OpenRgbPathKey, OpenRgbPath);
            if (FixedDeviceNames != null) AppendLine(content, FixedDevicesKey, string.Join(DeviceSeparator.ToString(), FixedDeviceNames));
            AppendLine(content, FixedEffectKey, FixedEffect.Serialize());
            foreach (GlowStatus status in StatusCatalog.ByPriority)
            {
                AppendLine(content, EffectKeyPrefix + status, Effects[status].Serialize());
            }
            try
            {
                File.WriteAllText(FilePath, content.ToString(), new UTF8Encoding(false));
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        private void Apply(string key, string value)
        {
            if (Is(key, HookPortKey)) HookPort = ParseInt(value, DefaultHookPort, 1024, 65535);
            else if (Is(key, DevicesKey)) DeviceNames = ParseDevices(value);
            else if (Is(key, WorkingTimeoutKey)) WorkingTimeoutMinutes = ParseInt(value, WorkingTimeoutMinutes, 0, 1440);
            else if (Is(key, DoneTimeoutKey)) DoneTimeoutMinutes = ParseInt(value, DoneTimeoutMinutes, 0, 1440);
            else if (Is(key, DarkAfterKey)) DarkAfterMinutes = ParseInt(value, DarkAfterMinutes, 0, 1440);
            else if (Is(key, LaunchOpenRgbKey)) LaunchOpenRgb = value == "1";
            else if (Is(key, OpenRgbPathKey)) OpenRgbPath = value.Length == 0 ? DefaultOpenRgbPath : value;
            else if (Is(key, FixedDevicesKey)) FixedDeviceNames = ParseDevices(value);
            else if (Is(key, FixedEffectKey)) FixedEffect = StatusEffect.Parse(value, FixedEffect);
            else if (key.StartsWith(EffectKeyPrefix, StringComparison.OrdinalIgnoreCase)) ApplyEffect(key.Substring(EffectKeyPrefix.Length), value);
        }

        private void ApplyEffect(string statusName, string value)
        {
            GlowStatus status;
            if (!Enum.TryParse(statusName, true, out status)) return;
            Effects[status] = StatusEffect.Parse(value, Effects[status]);
        }

        private static List<string> ParseDevices(string value)
        {
            var names = new List<string>();
            foreach (string name in value.Split(DeviceSeparator))
            {
                string trimmed = name.Trim();
                if (trimmed.Length > 0) names.Add(trimmed);
            }
            return names.Count == 0 ? null : names;
        }

        private static int ParseInt(string value, int fallback, int min, int max)
        {
            int parsed;
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)) return fallback;
            return Math.Max(min, Math.Min(max, parsed));
        }

        private static bool Is(string key, string expected)
        {
            return string.Equals(key, expected, StringComparison.OrdinalIgnoreCase);
        }

        private static void AppendLine(StringBuilder content, string key, string value)
        {
            content.Append(key).Append(KeyValueSeparator).Append(value).Append(Environment.NewLine);
        }
    }
}
