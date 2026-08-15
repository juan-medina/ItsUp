using System;
using System.Collections.Generic;
using Dalamud.Configuration;
using Dalamud.Plugin;

namespace ItsUp
{
    /// <summary>Which point of the bar stays put as icons come and go.</summary>
    public enum BarAnchor
    {
        Left,
        Centre,
        Right,
    }

    /// <summary>Per-ability timings. Handed to the tracker by reference, so edits are live.</summary>
    [Serializable]
    public class AbilitySettings
    {
        /// <summary>How long before it comes back the dim warning appears.</summary>
        public int WarnMs { get; set; } = 3000;

        /// <summary>How long the reminder nags once it is back. 0 means until you press it.</summary>
        public int LingerMs { get; set; }
    }

    [Serializable]
    public class Configuration : IPluginConfiguration
    {
        public int Version { get; set; } = 1;

        /// <summary>Seeded into a new entry when an ability is first ticked in the picker.</summary>
        public int DefaultWarnMs { get; set; } = 3000;

        public int DefaultLingerMs { get; set; }

        public Dictionary<uint, AbilitySettings> Tracked { get; set; } = new();

        /// <summary>
        /// The pinned point, in screen pixels, and which edge of the bar sits on it. Position is
        /// derived from this every frame rather than read from the ImGui ini, so a job tracking
        /// eight abilities and one tracking three both stay put.
        /// </summary>
        public BarAnchor Anchor { get; set; } = BarAnchor.Centre;

        public float AnchorX { get; set; }

        public float AnchorY { get; set; }

        [NonSerialized] private IDalamudPluginInterface _pluginInterface = null!;

        public void Initialize(IDalamudPluginInterface pluginInterface) => _pluginInterface = pluginInterface;

        public void Save() => _pluginInterface.SavePluginConfig(this);
    }
}
