using System.Reflection;

namespace ItsUp
{
    internal static class Strings
    {
        internal static class Config
        {
            // --- Tabs ---
            internal const string TabWelcome = "Welcome";
            internal const string TabJobActions = "Job Actions";
            internal const string TabSettings = "Settings";

            // --- Welcome tab ---
            internal static string WelcomeTitle =>
                Services.PluginInterface?.Manifest.Name ?? "It's Up";

            internal static string WelcomeTagline =>
                Services.PluginInterface?.Manifest.Punchline ?? "Yes. Still up. Press it.";

            internal static string WelcomeDescription =>
                Services.PluginInterface?.Manifest.Description?.Trim()
                ?? typeof(Plugin).Assembly.GetCustomAttribute<AssemblyDescriptionAttribute>()?.Description?.Trim()
                ?? string.Empty;

            internal const string WelcomeConfigureButton = "Configure Actions \u2192";
            internal const string WelcomePreviewButton = "Try Preview Mode";
            internal const string WelcomeUnlockButton = "Unlock Bar";

            // --- Section headers in Settings tab ---
            internal const string SectionBar = "Notification Bar";
            internal const string SectionDisplay = "Display Options";
            internal const string SectionDefaults = "Default Timings";
            internal const string DefaultsHint =
                "Default timings are automatically applied when tracking new abilities under Job Actions.";

            // --- Job Actions header ---
            internal const string TrackedCountSingle = "1 ability tracked";
            internal const string TrackedCountMultiple = "{0} abilities tracked";
            internal const string TrackedCountNone = "No abilities tracked";
            internal const string SidebarHeader = "Jobs";

            // --- Default timings sentence (flows into inline controls) ---
            internal const string DefaultsPrefix = "Abilities show";
            internal const string DefaultsMiddle = "before ready, visible";

            // --- Growth direction ---
            internal const string GrowthDirectionLabel = "Growth direction";
            internal const string GrowthDirectionItems = "Right\0Both\0Left\0";
            internal const string GrowthDirectionTooltip =
                "Which direction the bar grows as abilities come back.\n" +
                "Right = grows rightward, Left = grows leftward, Both = grows in both directions.";

            // --- Icon size ---
            internal const string IconSizeLabel = "Icon size";
            internal const string IconSizeFormat = "%d px";
            internal const string IconSizeTooltip = "The size of each notification icon in pixels (24 to 128).";

            // --- Toggles ---
            internal const string Unlock = "Unlock";
            internal const string UnlockTooltip = "Drag to reposition, corner grip to resize. /itsup move";

            internal const string Preview = "Preview";
            internal const string PreviewTooltip =
                "Runs a simulated combat loop so you can see the bar in action. /itsup preview";

            internal const string ShowAnts = "Marching ants";
            internal const string ShowAntsTooltip =
                "Show animated marching ants border around ready abilities.";

            internal const string OnlyInDuties = "Only in duties";
            internal const string OnlyInDutiesTooltip =
                "Only show notifications while in a duty (dungeons, trials, raids, solo duties, and field operations).\n" +
                "Hides the bar during overworld combat and on training dummies.";

            internal const string ShowKeybinds = "Keybind hints";
            internal const string ShowKeybindsTooltip =
                "Show hotbar keybind shortcuts on ability icons.";

            internal const string RefreshKeybinds = "Refresh Hotkeys";
            internal const string RefreshKeybindsTooltip =
                "Re-scan Hotbars for keybind shortcuts.";

            // --- Reset ---
            internal const string Reset = "Reset";
            internal const string ResetTooltip =
                "Reset the display location and icon size. /itsup reset";
        }

        internal static class Describe
        {
            internal const string WarnTemplate = "Countdown starts {0}s before ready";
            internal const string WarnNone = "No countdown";
            internal const string LingerTemplate = "clears after {0}s.";
            internal const string LingerForever = "stays until pressed.";
        }

        internal static class Table
        {
            internal const string ColumnAbility = "Ability";
            internal const string ColumnHeadsUp = "Heads-up";
            internal const string ColumnVisible = "Visible";

            internal const string HeadsUpTooltip = "Seconds before the ability is ready to start showing a countdown.";
            internal const string VisibleTooltip = "How long the ready notification stays on screen.";

            internal const string FollowUpTooltip =
                "Follow-ups become available when the parent ability is used.\n" +
                "A countdown does not apply.";

            internal const string LingerForDropdown = "for";
            internal const string LingerUntilPressedDropdown = "until pressed";
            internal const string LingerDropdownItems = "for\0until pressed\0";
            internal const string LingerDropdownTooltip =
                "\"Until pressed\" keeps the notification visible until you use the ability.";
        }

        internal static class Command
        {
            internal const string HelpMessage =
                "Open config.\n" +
                "/itsup move \u2192 Unlock the bar to reposition it.\n" +
                "/itsup preview \u2192 Toggle preview mode.\n" +
                "/itsup reset \u2192 Reset the bar position and size.";
        }

        internal const string SlotPreviewLabel = "It's Up";
    }
}
