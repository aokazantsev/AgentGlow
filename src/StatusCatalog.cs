namespace ClaudeGlow
{
    internal static class StatusCatalog
    {
        public static readonly GlowStatus[] ByPriority =
        {
            GlowStatus.Error,
            GlowStatus.Permission,
            GlowStatus.Question,
            GlowStatus.DoneIdle,
            GlowStatus.Done,
            GlowStatus.Working,
            GlowStatus.Idle
        };

        public static string DisplayName(GlowStatus status)
        {
            switch (status)
            {
                case GlowStatus.Error: return "Ошибка";
                case GlowStatus.Permission: return "Нужно разрешение";
                case GlowStatus.Question: return "Задал вопрос";
                case GlowStatus.DoneIdle: return "Закончил, жду больше минуты";
                case GlowStatus.Done: return "Закончил";
                case GlowStatus.Working: return "Работаю";
                default: return "Нет активных сессий";
            }
        }

        public static StatusEffect DefaultEffect(GlowStatus status)
        {
            switch (status)
            {
                case GlowStatus.Error: return new StatusEffect(EffectKind.Runner, 0xFF0000, 4);
                case GlowStatus.Permission: return new StatusEffect(EffectKind.Runner, 0xFF00FF, 4);
                case GlowStatus.Question: return new StatusEffect(EffectKind.Runner, 0xFF6000, 4);
                case GlowStatus.DoneIdle: return new StatusEffect(EffectKind.Pulse, 0x00FF00, 1);
                case GlowStatus.Done: return new StatusEffect(EffectKind.Static, 0x00FF00, 3);
                case GlowStatus.Working: return new StatusEffect(EffectKind.Static, 0x0040FF, 3);
                default: return new StatusEffect(EffectKind.Off, 0x000000, 3);
            }
        }
    }
}
