using System;

namespace ClaudeGlow
{
    internal sealed class UpdateRelease
    {
        public readonly Version Version;
        public readonly string VersionText;
        public readonly string DownloadUrl;
        public readonly long Size;

        public UpdateRelease(string versionText, string downloadUrl, long size)
        {
            VersionText = versionText;
            Version = UpdateChecker.ParseVersion(versionText);
            DownloadUrl = downloadUrl;
            Size = size;
        }
    }
}
