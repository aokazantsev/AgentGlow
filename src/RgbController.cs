using System;
using System.Collections.Generic;

namespace AgentGlow
{
    internal sealed class RgbController
    {
        public const int GpuType = 2;

        public int Index;
        public int Type;
        public string Name;
        public int ActiveModeIndex;
        public readonly List<RgbMode> Modes = new List<RgbMode>();
        public uint[] Colors;

        public int FindMode(string[] names)
        {
            foreach (string name in names)
            {
                for (int i = 0; i < Modes.Count; i++)
                {
                    if (string.Equals(Modes[i].Name, name, StringComparison.OrdinalIgnoreCase)) return i;
                }
            }
            return -1;
        }
    }
}
