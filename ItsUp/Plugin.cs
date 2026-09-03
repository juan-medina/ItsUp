using System;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ItsUp.Windows;
using System.IO;
using Dalamud.Interface.ManagedFontAtlas;

namespace ItsUp
{
    public sealed class Plugin : IDalamudPlugin
    {
        public static string Name => "It's Up";

        private const string CommandName = "/itsup";
        private const float BakedFontSize = 92.0f;
        private const int HorizontalOversample = 2;
        private const int VerticalOversample = 2;
        private const string FontFile = "BebasNeue-Regular.ttf";
        private static readonly SafeFontConfig FontConfig = new SafeFontConfig
        {
            SizePx = BakedFontSize,
            OversampleH = HorizontalOversample,
            OversampleV = VerticalOversample,
            GlyphRanges = [0x0030, 0x0039, 0] // Just 0 to 9 digits
        };

        private readonly IDalamudPluginInterface _pluginInterface;
        private readonly WindowSystem _windowSystem = new("ItsUp");
        private readonly Configuration _config;
        private readonly CooldownTracker _tracker;
        private readonly CooldownWatcher _watcher;
        private readonly CooldownWindow _window;
        private readonly ConfigWindow _configWindow;
        private readonly string _numberFontPath;

        public static IFontHandle? NumberFont { get; private set; }

        public Plugin(IDalamudPluginInterface pluginInterface)
        {
            Services.Initialize(pluginInterface);
            _pluginInterface = pluginInterface;

            var existing = pluginInterface.GetPluginConfig() as Configuration;
            _config = existing ?? new Configuration();
            _config.Initialize(pluginInterface);

            _tracker = new CooldownTracker(_config);
            _watcher = new CooldownWatcher(_config, _tracker);
            _tracker.SetWatcher(_watcher);
            _tracker.Sync();

            _window = new CooldownWindow(_tracker, _config);
            _configWindow = new ConfigWindow(_config, _tracker, _window);
            _windowSystem.AddWindow(_window);
            _windowSystem.AddWindow(_configWindow);

            Services.CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
            {
                HelpMessage = "Open settings.\n/itsup move → Unlocks the panel so you can drag it."
            });

            _pluginInterface.UiBuilder.Draw += DrawUI;
            _pluginInterface.UiBuilder.OpenConfigUi += OpenConfig;
            _pluginInterface.UiBuilder.OpenMainUi += OpenConfig;
            Services.Framework.Update += OnUpdate;

            _numberFontPath = Path.Combine(_pluginInterface.AssemblyLocation.DirectoryName!, "Assets", FontFile);
            NumberFont = _pluginInterface.UiBuilder.FontAtlas.NewDelegateFontHandle(e => e.OnPreBuild(BuildNumberFont));
        }

        private void BuildNumberFont(IFontAtlasBuildToolkitPreBuild tk)
        {
            if (File.Exists(_numberFontPath))
            {
                tk.AddFontFromFile(_numberFontPath, FontConfig);
            }
        }

        private void OnUpdate(IFramework framework)
        {
            _watcher.Update();
            _tracker.Update();
        }

        private void DrawUI() => _windowSystem.Draw();

        private void OpenConfig() => _configWindow.IsOpen = true;

        private void OnCommand(string command, string args)
        {
            if (args.Trim().Equals("move", StringComparison.OrdinalIgnoreCase))
                ToggleLock();
            else
                OpenConfig();
        }

        private void ToggleLock() => _window.ToggleLock();

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
