namespace ClaudeGlow
{
    internal sealed class RgbMode
    {
        public const uint ColorModePerLed = 1;
        public const uint ColorModeModeSpecific = 2;
        private const uint FlagHasSpeed = 1;

        public string Name;
        public int Value;
        public uint Flags;
        public uint SpeedMin;
        public uint SpeedMax;
        public uint BrightnessMin;
        public uint BrightnessMax;
        public uint ColorsMin;
        public uint ColorsMax;
        public uint Speed;
        public uint Brightness;
        public uint Direction;
        public uint ColorMode;
        public uint[] Colors;

        public bool HasSpeed
        {
            get { return (Flags & FlagHasSpeed) != 0; }
        }

        public RgbMode Copy()
        {
            var copy = (RgbMode)MemberwiseClone();
            copy.Colors = (uint[])Colors.Clone();
            return copy;
        }
    }
}
