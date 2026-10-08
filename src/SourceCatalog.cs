using System;
using System.Collections.Generic;
using AgentGlow.Sources.Claude;
using AgentGlow.Sources.Generic;
using AgentGlow.Sources.OpenCode;

namespace AgentGlow
{
    internal static class SourceCatalog
    {
        public sealed class Entry
        {
            public readonly string Id;
            public readonly string DisplayName;
            public readonly IIntegration Integration;
            public readonly Func<IAgentSource> Create;

            public Entry(string id, string displayName, IIntegration integration, Func<IAgentSource> create)
            {
                Id = id;
                DisplayName = displayName;
                Integration = integration;
                Create = create;
            }
        }

        public static readonly Entry[] All =
        {
            new Entry("claude", "Claude Code", new ClaudeIntegration(), () => new ClaudeSource()),
            new Entry("opencode", "OpenCode", new OpenCodeIntegration(), () => new OpenCodeSource()),
            new Entry("generic", "Другие приложения", null, () => new GenericSource())
        };

        public static List<string> AllIds()
        {
            var ids = new List<string>();
            foreach (Entry entry in All)
            {
                ids.Add(entry.Id);
            }
            return ids;
        }

        public static SourceSet Create(AppSettings settings)
        {
            var sources = new List<IAgentSource>();
            foreach (Entry entry in All)
            {
                if (settings.IsSourceEnabled(entry.Id)) sources.Add(entry.Create());
            }
            return new SourceSet(sources);
        }
    }
}
