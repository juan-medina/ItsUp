using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Configuration;
using Dalamud.Plugin;

namespace ItsUp
{
    public enum BarAnchor
    {
        Left,
        Centre,
        Right,
    }

    [Serializable]
    public class AbilitySettings
    {
        public int WarnMs { get; set; }
        public int LingerMs { get; set; }
        public bool LingerForever { get; set; }
        public uint ParentActionId { get; set; }
        public bool IsFollowup => ParentActionId != 0;
    }

    [Serializable]
    public class Configuration : IPluginConfiguration
    {
        public int Version { get; set; } = 2;
        public int DefaultWarnMs { get; set; } = 5000;
        public int DefaultLingerMs { get; set; } = 5000;
        public bool DefaultLingerForever { get; set; } = true;

        public Dictionary<uint, Dictionary<uint, AbilitySettings>> TrackedByJob { get; set; } = [];

        // Retained for migration from Version 1 configs
        public Dictionary<uint, AbilitySettings>? Tracked { get; set; }

        public const float MinIconSize = 24f;
        public const float MaxIconSize = 128f;
        public const float DefaultIconSize = 48f;

        public BarAnchor Anchor { get; set; } = BarAnchor.Centre;
        public float AnchorX { get; set; }
        public float AnchorY { get; set; }
        public float IconSize { get; set; } = DefaultIconSize;
        public bool ShowAnts { get; set; } = true;

        [NonSerialized] private IDalamudPluginInterface _pluginInterface = null!;
        public void Initialize(IDalamudPluginInterface pluginInterface) => _pluginInterface = pluginInterface;
        public void Save() => _pluginInterface.SavePluginConfig(this);

        public Dictionary<uint, AbilitySettings> GetTrackedForJob(uint jobId)
        {
            if (!TrackedByJob.TryGetValue(jobId, out var tracked))
            {
                tracked = [];
                TrackedByJob[jobId] = tracked;
            }
            return tracked;
        }

        public void MigrateIfNeeded(IReadOnlyDictionary<uint, List<ActionItem>> jobActions)
        {
            if (Tracked != null && Tracked.Count > 0)
            {
                foreach (var (actionId, settings) in Tracked)
                {
                    foreach (var (jobId, items) in jobActions)
                    {
                        if (items.Any(i => i.ActionId == actionId))
                        {
                            var jobTracked = GetTrackedForJob(jobId);
                            jobTracked[actionId] = new AbilitySettings
                            {
                                WarnMs = settings.WarnMs,
                                LingerMs = settings.LingerMs,
                                LingerForever = settings.LingerForever,
                                ParentActionId = settings.ParentActionId
                            };
                        }
                    }
                }

                Tracked = null;
                Version = 2;
                Save();
            }
        }
    }
}
