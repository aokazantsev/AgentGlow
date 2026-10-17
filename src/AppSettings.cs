using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace AgentGlow
{
    internal sealed class AppSettings
    {
        public const int DefaultEventPort = 47651;
        public const string DefaultOpenRgbPath = @"C:\Program Files\OpenRGB\OpenRGB.exe";

        private const string EventPortKey = "eventPort";
        private const string SourcesKey = "sources";
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
        private const int ReadAttempts = 5;
        private const int ReadRetryMs = 300;
        private const int PatientReadRetries = 10;
        private const int PatientReadRetryMs = 1500;

        public static readonly string FilePath = UserDataPaths.File("settings.txt");

        public int EventPort = DefaultEventPort;
        public List<string> EnabledSources = new List<string>(SourceCatalog.AllIds());
        public List<string> DeviceNames;
        public int WorkingTimeoutMinutes = 30;
        public int DoneTimeoutMinutes = 120;
        public int DarkAfterMinutes = 30;
        public bool LaunchOpenRgb = true;
        public string OpenRgbPath = DefaultOpenRgbPath;
        public List<string> FixedDeviceNames;
        public StatusEffect FixedEffect = new StatusEffect(EffectKind.Off, 0x000000, 3);
        public readonly Dictionary<GlowStatus, StatusEffect> Effects = new Dictionary<GlowStatus, StatusEffect>();
        public string ReadError;
        public bool ReadDenied;

        public AppSettings()
        {
            foreach (GlowStatus status in StatusCatalog.ByPriority)
            {
                Effects[status] = StatusCatalog.DefaultEffect(status);
            }
        }

        public bool IsSourceEnabled(string id)
        {
            return EnabledSources.Contains(id);
        }

        private static List<string> ParseSources(string value)
        {
            var known = SourceCatalog.AllIds();
            var ids = new List<string>();
            foreach (string id in value.Split(',', '|'))
            {
                string trimmed = id.Trim().ToLowerInvariant();
                if (known.Contains(trimmed) && !ids.Contains(trimmed)) ids.Add(trimmed);
            }
            return ids;
        }

        public static bool FileExists
        {
            get { return File.Exists(FilePath); }
        }

        public static AppSettings Load()
        {
            var settings = new AppSettings();
            if (!File.Exists(FilePath)) return settings;
            string[] lines = null;
            for (int attempt = 1; lines == null; attempt++)
            {
                try
                {
                    lines = File.ReadAllLines(FilePath, Encoding.UTF8);
                }
                catch (IOException error)
                {
                    if (attempt >= ReadAttempts) settings.ReadError = error.Message;
                }
                catch (UnauthorizedAccessException error)
                {
                    settings.ReadError = error.Message;
                    settings.ReadDenied = true;
                }
                if (settings.ReadError != null) return settings;
                if (lines == null) Thread.Sleep(ReadRetryMs);
            }
            foreach (string line in lines)
            {
                int separator = line.IndexOf(KeyValueSeparator);
                if (separator <= 0) continue;
                settings.Apply(line.Substring(0, separator).Trim(), line.Substring(separator + 1).Trim());
            }
            return settings;
        }

        public static AppSettings LoadPatiently(Action<string> log)
        {
            AppSettings settings = Load();
            for (int attempt = 1; settings.ReadError != null && !settings.ReadDenied && attempt <= PatientReadRetries; attempt++)
            {
                log("settings not read: " + settings.ReadError + ", retry " + attempt);
                Thread.Sleep(PatientReadRetryMs);
                settings = Load();
            }
            return settings;
        }

        public string ReadAdvice
        {
            get { return ReadDenied ? "Нет доступа к файлу: верни права на него или удали его." : "Файл занят другой программой: подожди, пока он освободится."; }
        }

        public StatusEffect EffectFor(GlowStatus status)
        {
            return Effects[status];
        }

        public AppSettings Clone()
        {
            var copy = new AppSettings();
            copy.EventPort = EventPort;
            copy.EnabledSources = new List<string>(EnabledSources);
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

        public string Save()
        {
            var content = new StringBuilder();
            AppendLine(content, EventPortKey, EventPort.ToString(CultureInfo.InvariantCulture));
            AppendLine(content, SourcesKey, string.Join(",", EnabledSources.ToArray()));
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
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, content.ToString(), new UTF8Encoding(false));
                return null;
            }
            catch (IOException error)
            {
                return error.Message;
            }
            catch (UnauthorizedAccessException error)
            {
                return error.Message;
            }
        }

        private void Apply(string key, string value)
        {
            if (Is(key, EventPortKey)) EventPort = ParseInt(value, DefaultEventPort, 1024, 65535);
            else if (Is(key, SourcesKey)) EnabledSources = ParseSources(value);
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
            if (!Enum.TryParse(statusName, true, out status) || !Effects.ContainsKey(status)) return;
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
