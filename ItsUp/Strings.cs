namespace ItsUp
{
    internal static class Strings
    {
        internal static class Config
        {
            // --- Default timings sentence (flows into inline controls) ---
            internal const string DefaultsPrefix = "Abilities show";
            internal const string DefaultsMiddle = "before ready, visible";

            // --- Growth direction ---
            internal const string GrowthDirectionLabel = "Growth direction";
            internal const string GrowthDirectionItems = "Right\0Both\0Left\0";
            internal const string GrowthDirectionTooltip =
                "Which direction the bar grows as abilities come back.\n" +
                "Right = grows rightward, Left = grows leftward, Both = grows in both directions.";

            // --- Toggles ---
            internal const string Unlock = "Unlock";
            internal const string UnlockTooltip = "Drag to reposition, corner grip to resize. /itsup move";

            internal const string Preview = "Preview";
            internal const string PreviewTooltip = "Runs a simulated combat loop so you can see the bar in action.";

            // --- Reset ---
            internal const string ResetPosition = "Reset position";
            internal const string ResetPositionTooltip = "Resets icon size and position to defaults.";
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
                "Open settings.\n/itsup move \u2192 Unlock the bar to reposition it.";
        }

        internal const string SlotPreviewLabel = "It's Up";
    }
}
