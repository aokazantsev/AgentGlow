using System.Collections.Generic;

namespace ClaudeGlow
{
    internal sealed class SetupField
    {
        public string Key;
        public string Label;
        public string Value;
        public string Hint;
        public readonly List<string> Suggestions = new List<string>();
    }
}
