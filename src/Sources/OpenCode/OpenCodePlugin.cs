using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace AgentGlow.Sources.OpenCode
{
    internal static class OpenCodePlugin
    {
        public const int CurrentVersion = 2;

        private const string ResourceName = "AgentGlow.Plugin.js";
        private const string PortToken = "{{PORT}}";
        private const string FileName = "agentglow.js";
        private static readonly Regex Header = new Regex(@"^// AgentGlow plugin version=(\d+) port=(\d+)", RegexOptions.CultureInvariant);

        public static string PluginPath
        {
            get { return Path.Combine(ConfigDirectory, "plugins", FileName); }
        }

        private static string ConfigDirectory
        {
            get
            {
                string baseDirectory = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
                if (string.IsNullOrEmpty(baseDirectory))
                {
                    baseDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
                }
                return Path.Combine(baseDirectory, "opencode");
            }
        }

        public static bool IsInstalled(int port)
        {
            try
            {
                Match header = ReadHeader();
                return header != null
                    && int.Parse(header.Groups[1].Value) == CurrentVersion
                    && int.Parse(header.Groups[2].Value) == port;
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

        public static void Install(int port)
        {
            string path = PluginPath;
            if (File.Exists(path) && ReadHeader() == null)
            {
                throw new IOException(path + " — чужой файл, AgentGlow его не трогает");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string text = LoadTemplate().Replace(PortToken, port.ToString()).Replace("\r\n", "\n");
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }

        public static bool Uninstall()
        {
            string path = PluginPath;
            if (!File.Exists(path) || ReadHeader() == null) return false;
            File.Delete(path);
            return true;
        }

        private static Match ReadHeader()
        {
            string path = PluginPath;
            if (!File.Exists(path)) return null;
            string firstLine;
            using (var reader = new StreamReader(path, Encoding.UTF8))
            {
                firstLine = reader.ReadLine();
            }
            if (firstLine == null) return null;
            Match match = Header.Match(firstLine);
            return match.Success ? match : null;
        }

        private static string LoadTemplate()
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName))
            {
                if (stream == null) throw new InvalidOperationException("в программу не вшит файл плагина " + ResourceName);
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    return reader.ReadToEnd();
                }
            }
        }
    }
}
