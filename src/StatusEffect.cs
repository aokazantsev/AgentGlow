using System;
using System.Drawing;
using System.Globalization;

namespace AgentGlow
{
    internal sealed class StatusEffect
    {
        public const int MinSpeed = 1;
        public const int MaxSpeed = 5;
        private const char FieldSeparator = ';';

        public readonly EffectKind Kind;
        public readonly int Rgb;
        public readonly int SpeedLevel;

        public StatusEffect(EffectKind kind, int rgb, int speedLevel)
        {
            Kind = kind;
            Rgb = rgb & 0xFFFFFF;
            SpeedLevel = Math.Max(MinSpeed, Math.Min(MaxSpeed, speedLevel));
        }

        public Color Color
        {
            get { return Color.FromArgb((Rgb >> 16) & 0xFF, (Rgb >> 8) & 0xFF, Rgb & 0xFF); }
        }

        public bool SameAs(StatusEffect other)
        {
            return other != null && Kind == other.Kind && Rgb == other.Rgb && SpeedLevel == other.SpeedLevel;
        }

        public string Serialize()
        {
            return Kind.ToString() + FieldSeparator + Rgb.ToString("X6") + FieldSeparator
                + SpeedLevel.ToString(CultureInfo.InvariantCulture);
        }

        public static StatusEffect Parse(string text, StatusEffect fallback)
        {
            string[] fields = text.Split(FieldSeparator);
            if (fields.Length != 3) return fallback;
            EffectKind kind;
            int rgb;
            int speed;
            if (!Enum.TryParse(fields[0].Trim(), true, out kind)) return fallback;
            if (!int.TryParse(fields[1].Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rgb)) return fallback;
            if (!int.TryParse(fields[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out speed)) return fallback;
            return new StatusEffect(kind, rgb, speed);
        }

        public static int ToRgb(Color color)
        {
            return (color.R << 16) | (color.G << 8) | color.B;
        }
    }
}
