using System;
using System.IO;
using System.Text;
using System.Threading;

namespace AgentGlow
{
    internal static class AppLog
    {
        private const long MaxBytes = 512 * 1024;
        private const int WriteAttempts = 10;
        private const int WriteRetryMs = 50;
        private const int SharingViolation = 32;
        private const int LockViolation = 33;
        private static readonly object Gate = new object();

        public static readonly string FilePath = UserDataPaths.File("log.txt");
        private static readonly string PreviousFilePath = UserDataPaths.File("log.old.txt");

        public static void Append(string line)
        {
            AppendTo(FilePath, PreviousFilePath, MaxBytes, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + line);
        }

        public static void AppendTo(string filePath, string previousFilePath, long maxBytes, string line)
        {
            byte[] bytes = new UTF8Encoding(false).GetBytes(line + Environment.NewLine);
            lock (Gate)
            {
                for (int attempt = 1; attempt <= WriteAttempts; attempt++)
                {
                    try
                    {
                        Rotate(filePath, previousFilePath, maxBytes);
                        using (var stream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Read))
                        {
                            stream.Write(bytes, 0, bytes.Length);
                        }
                        return;
                    }
                    catch (DirectoryNotFoundException)
                    {
                        if (attempt > 1) return;
                        TryCreateDirectory();
                        continue;
                    }
                    catch (IOException error)
                    {
                        if (!IsSharingViolation(error)) return;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        return;
                    }
                    Thread.Sleep(WriteRetryMs);
                }
            }
        }

        private static bool IsSharingViolation(IOException error)
        {
            int code = error.HResult & 0xFFFF;
            return code == SharingViolation || code == LockViolation;
        }

        private static void TryCreateDirectory()
        {
            try
            {
                UserDataPaths.Ensure();
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static void Rotate(string filePath, string previousFilePath, long maxBytes)
        {
            try
            {
                var info = new FileInfo(filePath);
                if (!info.Exists || info.Length <= maxBytes) return;
                File.Delete(previousFilePath);
                File.Move(filePath, previousFilePath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
