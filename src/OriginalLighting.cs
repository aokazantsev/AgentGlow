namespace ClaudeGlow
{
    internal sealed class OriginalLighting
    {
        public readonly int ModeIndex;
        public readonly RgbMode Mode;
        public readonly uint[] Colors;

        private OriginalLighting(int modeIndex, RgbMode mode, uint[] colors)
        {
            ModeIndex = modeIndex;
            Mode = mode;
            Colors = colors;
        }

        public static OriginalLighting Capture(RgbController controller)
        {
            int index = controller.ActiveModeIndex;
            if (index < 0 || index >= controller.Modes.Count) return null;
            return new OriginalLighting(index, controller.Modes[index].Copy(), (uint[])controller.Colors.Clone());
        }
    }
}
