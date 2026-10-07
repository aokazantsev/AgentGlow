using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace ClaudeGlow
{
    internal static class ClaudeHooks
    {
        private const string HooksKey = "hooks";
        private const string MatcherKey = "matcher";
        private const string BackupSuffix = ".claudeglow.bak";
        private const int TimeoutSeconds = 2;
        private static readonly Regex OwnUrl = new Regex(@"^http://127\.0\.0\.1:\d+/hook$");

        private static readonly string[][] Events =
        {
            new[] { "UserPromptSubmit", null },
            new[] { "PreToolUse", "AskUserQuestion" },
            new[] { "PermissionRequest", null },
            new[] { "PostToolUse", null },
            new[] { "PermissionDenied", null },
            new[] { "PostToolUseFailure", null },
            new[] { "Notification", null },
            new[] { "Stop", null },
            new[] { "StopFailure", null },
            new[] { "SessionStart", null },
            new[] { "SessionEnd", null }
        };

        public static string SettingsPath
        {
            get
            {
                string configDirectory = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
                if (string.IsNullOrEmpty(configDirectory))
                {
                    configDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
                }
                return Path.Combine(configDirectory, "settings.json");
            }
        }

        public static string Url(int port)
        {
            return "http://127.0.0.1:" + port + "/hook";
        }

        public static bool AreInstalled(int port)
        {
            try
            {
                Dictionary<string, object> hooks = HooksSection(Load(), false);
                if (hooks == null) return false;
                foreach (string[] hookEvent in Events)
                {
                    if (FindGroupWithUrl(hooks, hookEvent[0], Url(port)) == null) return false;
                }
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
            catch (FormatException)
            {
                return false;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        public static void Install(int port)
        {
            Dictionary<string, object> root = Load();
            RemoveOwn(root);
            Dictionary<string, object> hooks = HooksSection(root, true);
            foreach (string[] hookEvent in Events)
            {
                var hook = new Dictionary<string, object>
                {
                    { "type", "http" },
                    { "url", Url(port) },
                    { "timeout", TimeoutSeconds }
                };
                var group = new Dictionary<string, object>();
                if (hookEvent[1] != null) group[MatcherKey] = hookEvent[1];
                group[HooksKey] = new ArrayList { hook };
                ArrayList groups = Groups(hooks, hookEvent[0], true);
                groups.Add(group);
            }
            Save(root);
        }

        public static bool Uninstall()
        {
            if (!File.Exists(SettingsPath)) return false;
            Dictionary<string, object> root = Load();
            if (!RemoveOwn(root)) return false;
            Save(root);
            return true;
        }

        private static bool RemoveOwn(Dictionary<string, object> root)
        {
            Dictionary<string, object> hooks = HooksSection(root, false);
            if (hooks == null) return false;
            bool removed = false;
            foreach (string eventName in new List<string>(hooks.Keys))
            {
                ArrayList groups = Groups(hooks, eventName, false);
                if (groups == null) continue;
                for (int i = groups.Count - 1; i >= 0; i--)
                {
                    var group = groups[i] as Dictionary<string, object>;
                    if (group == null) continue;
                    ArrayList entries = AsList(group.ContainsKey(HooksKey) ? group[HooksKey] : null);
                    if (entries == null) continue;
                    for (int j = entries.Count - 1; j >= 0; j--)
                    {
                        if (IsOwn(entries[j] as Dictionary<string, object>))
                        {
                            entries.RemoveAt(j);
                            removed = true;
                        }
                    }
                    group[HooksKey] = entries;
                    if (entries.Count == 0) groups.RemoveAt(i);
                }
                if (groups.Count == 0) hooks.Remove(eventName);
                else hooks[eventName] = groups;
            }
            if (hooks.Count == 0) root.Remove(HooksKey);
            return removed;
        }

        private static bool IsOwn(Dictionary<string, object> hook)
        {
            if (hook == null) return false;
            object type;
            object url;
            return hook.TryGetValue("type", out type) && "http".Equals(type)
                && hook.TryGetValue("url", out url) && url is string && OwnUrl.IsMatch((string)url);
        }

        private static Dictionary<string, object> FindGroupWithUrl(Dictionary<string, object> hooks, string eventName, string url)
        {
            ArrayList groups = Groups(hooks, eventName, false);
            if (groups == null) return null;
            foreach (object item in groups)
            {
                var group = item as Dictionary<string, object>;
                ArrayList entries = group == null || !group.ContainsKey(HooksKey) ? null : AsList(group[HooksKey]);
                if (entries == null) continue;
                foreach (object entry in entries)
                {
                    var hook = entry as Dictionary<string, object>;
                    object value;
                    if (hook != null && hook.TryGetValue("url", out value) && url.Equals(value)) return group;
                }
            }
            return null;
        }

        private static Dictionary<string, object> HooksSection(Dictionary<string, object> root, bool create)
        {
            object section;
            if (root.TryGetValue(HooksKey, out section))
            {
                var map = section as Dictionary<string, object>;
                if (map == null) throw new FormatException("поле hooks — не объект");
                return map;
            }
            if (!create) return null;
            var created = new Dictionary<string, object>();
            root[HooksKey] = created;
            return created;
        }

        private static ArrayList Groups(Dictionary<string, object> hooks, string eventName, bool create)
        {
            object value;
            if (hooks.TryGetValue(eventName, out value))
            {
                ArrayList list = AsList(value);
                if (list == null) throw new FormatException("hooks." + eventName + " — не массив");
                hooks[eventName] = list;
                return list;
            }
            if (!create) return null;
            var created = new ArrayList();
            hooks[eventName] = created;
            return created;
        }

        private static ArrayList AsList(object value)
        {
            var list = value as IList;
            return list == null ? null : new ArrayList(list);
        }

        private static Dictionary<string, object> Load()
        {
            string path = SettingsPath;
            if (!File.Exists(path)) return new Dictionary<string, object>();
            string text = File.ReadAllText(path, Encoding.UTF8);
            try
            {
                return JsonText.ParseObject(text);
            }
            catch (ArgumentException error)
            {
                throw new FormatException(path + " — не JSON: " + error.Message);
            }
            catch (InvalidOperationException error)
            {
                throw new FormatException(path + " — не JSON: " + error.Message);
            }
        }

        private static void Save(Dictionary<string, object> root)
        {
            string path = SettingsPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            if (File.Exists(path) && !File.Exists(path + BackupSuffix)) File.Copy(path, path + BackupSuffix);
            File.WriteAllText(path, JsonText.Write(root), new UTF8Encoding(false));
        }
    }
}
