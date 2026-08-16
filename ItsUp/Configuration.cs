using System;
using System.Collections.Generic;
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
    }

    [Serializable]
    public class Configuration : IPluginConfiguration
    {
        public int Version { get; set; } = 1;
        public int DefaultWarnMs { get; set; } = 5000;
        public int DefaultLingerMs { get; set; } = 5000;
        public bool DefaultLingerForever { get; set; } = true;

        public Dictionary<uint, AbilitySettings> Tracked { get; set; } = [];
        public BarAnchor Anchor { get; set; } = BarAnchor.Centre;
        public float AnchorX { get; set; }
        public float AnchorY { get; set; }
        public float IconSize { get; set; } = 48f;

        [NonSerialized] private IDalamudPluginInterface _pluginInterface = null!;
        public void Initialize(IDalamudPluginInterface pluginInterface) => _pluginInterface = pluginInterface;
        public void Save() => _pluginInterface.SavePluginConfig(this);
    }
}
