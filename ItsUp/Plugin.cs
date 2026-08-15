using System;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ItsUp.Windows;

namespace ItsUp
{
    public sealed class Plugin : IDalamudPlugin
    {
        public string Name => "It's Up";

        private const string CommandName = "/itsup";

        private readonly IDalamudPluginInterface _pluginInterface;
        private readonly WindowSystem _windowSystem = new("ItsUp");
        private readonly Configuration _config;
        private readonly CooldownTracker _tracker;
        private readonly CooldownWindow _window;
        private readonly ConfigWindow _configWindow;

        public Plugin(IDalamudPluginInterface pluginInterface)
        {
            Services.Initialize(pluginInterface);
            _pluginInterface = pluginInterface;

            JobEligibility.Build();

            var existing = pluginInterface.GetPluginConfig() as Configuration;
            _config = existing ?? new Configuration();
            _config.Initialize(pluginInterface);

            _tracker = new CooldownTracker(_config);
            _tracker.Sync();

            _window = new CooldownWindow(_tracker, _config);
            _configWindow = new ConfigWindow(_config, _tracker, _window);
            _windowSystem.AddWindow(_window);
            _windowSystem.AddWindow(_configWindow);

            Services.CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
            {
                HelpMessage = "Toggle move mode so you can drag the cooldown panel. \"/itsup config\" opens settings."
            });

            _pluginInterface.UiBuilder.Draw += DrawUI;
            _pluginInterface.UiBuilder.OpenConfigUi += OpenConfig;
            // The panel is always open and has no controls of its own, so the settings window is the
            // only thing "open the plugin" can usefully mean.
            _pluginInterface.UiBuilder.OpenMainUi += OpenConfig;
            Services.Framework.Update += OnUpdate;

            // Nothing is tracked out of the box, so a fresh install would otherwise look broken.
            if (existing == null) _configWindow.IsOpen = true;
        }

        private void OnUpdate(IFramework framework) => _tracker.Update();

        private void DrawUI() => _windowSystem.Draw();

        private void OpenConfig() => _configWindow.IsOpen = true;

        private void OnCommand(string command, string args)
        {
            if (args.Trim().Equals("config", StringComparison.OrdinalIgnoreCase))
                _configWindow.IsOpen = !_configWindow.IsOpen;
            else
                _window.ToggleLock();
        }

        public void Dispose()
        {
            Services.Framework.Update -= OnUpdate;
            _pluginInterface.UiBuilder.OpenMainUi -= OpenConfig;
            _pluginInterface.UiBuilder.OpenConfigUi -= OpenConfig;
            _pluginInterface.UiBuilder.Draw -= DrawUI;

            _windowSystem.RemoveAllWindows();
            Services.CommandManager.RemoveHandler(CommandName);
        }
    }
}
