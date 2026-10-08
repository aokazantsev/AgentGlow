namespace AgentGlow
{
    internal static class EffectCatalog
    {
        public static readonly EffectKind[] All =
        {
            EffectKind.Original,
            EffectKind.Off,
            EffectKind.Static,
            EffectKind.Pulse,
            EffectKind.Runner,
            EffectKind.Breathing,
            EffectKind.Flashing,
            EffectKind.SpectrumCycle,
            EffectKind.Rainbow,
            EffectKind.ChaseFade,
            EffectKind.Chase
        };

        public static string DisplayName(EffectKind kind)
        {
            switch (kind)
            {
                case EffectKind.Original: return "Исходная подсветка";
                case EffectKind.Off: return "Выключено";
                case EffectKind.Static: return "Ровный цвет";
                case EffectKind.Pulse: return "Плавная пульсация";
                case EffectKind.Runner: return "Бегущая строка";
                case EffectKind.Breathing: return "Дыхание";
                case EffectKind.Flashing: return "Мигание";
                case EffectKind.SpectrumCycle: return "Перелив спектра";
                case EffectKind.Rainbow: return "Радуга";
                case EffectKind.ChaseFade: return "Бегущее затухание";
                case EffectKind.Chase: return "Бегущий огонь";
                default: return kind.ToString();
            }
        }

        public static string[] ModeNames(EffectKind kind)
        {
            switch (kind)
            {
                case EffectKind.Off: return new[] { "Off" };
                case EffectKind.Static: return new[] { "Static", "Direct" };
                case EffectKind.Pulse:
                case EffectKind.Runner:
                    return new[] { "Direct", "Static" };
                case EffectKind.Breathing: return new[] { "Breathing" };
                case EffectKind.Flashing: return new[] { "Flashing", "Double Flash" };
                case EffectKind.SpectrumCycle: return new[] { "Spectrum Cycle", "Color Cycle" };
                case EffectKind.Rainbow: return new[] { "Rainbow", "Rainbow Wave" };
                case EffectKind.ChaseFade: return new[] { "Chase Fade" };
                case EffectKind.Chase: return new[] { "Chase" };
                default: return new string[0];
            }
        }

        public static bool UsesColor(EffectKind kind)
        {
            return kind == EffectKind.Static
                || kind == EffectKind.Pulse
                || kind == EffectKind.Runner
                || kind == EffectKind.Breathing
                || kind == EffectKind.Flashing
                || kind == EffectKind.ChaseFade
                || kind == EffectKind.Chase;
        }

        public static bool IsAnimated(EffectKind kind)
        {
            return kind == EffectKind.Pulse || kind == EffectKind.Runner;
        }

        public static bool UsesSpeed(EffectKind kind)
        {
            return kind == EffectKind.Pulse
                || kind == EffectKind.Runner
                || kind == EffectKind.Breathing
                || kind == EffectKind.Flashing
                || kind == EffectKind.SpectrumCycle
                || kind == EffectKind.Rainbow
                || kind == EffectKind.ChaseFade
                || kind == EffectKind.Chase;
        }
    }
}
