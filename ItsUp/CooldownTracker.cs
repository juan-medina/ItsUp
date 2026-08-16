using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;
using Action = Lumina.Excel.Sheets.Action;

namespace ItsUp
{
    public class TrackedCooldown
    {
        public required uint ActionId { get; init; }

        public required AbilitySettings Settings { get; init; }

        public int WarnMs => Settings.WarnMs;
        public int LingerMs => Settings.LingerMs;
        public bool LingerForever => Settings.LingerForever;

        public string Name = string.Empty;
        public uint IconId;

        public bool Available;

        public float SecondsLeft;
        public DateTime? ReadySince;

        public const int PressedFadeDuration = 280;

        public DateTime? PressedSince;
        public bool IsPressed;

        public bool IsReady => ReadySince != null;
        public bool IsWarming => !Available && SecondsLeft > 0f && SecondsLeft <= WarnMs / 1000f;
        public bool IsFading => PressedSince != null;
        public bool Visible => IsReady || IsWarming || IsFading;
    }

    public class CooldownTracker(Configuration config)
    {
        private readonly Configuration _config = config;
        private readonly List<TrackedCooldown> _cooldowns = [];

        public IReadOnlyList<TrackedCooldown> Cooldowns => _cooldowns;

        public void Sync()
        {
            var sheet = Services.DataManager.GetExcelSheet<Action>()!;

            // remove cooldown we dont track anymore
            _cooldowns.RemoveAll(cd => !_config.Tracked.ContainsKey(cd.ActionId));

            // are the missing cooldowns on what we need to track
            foreach (var (actionId, settings) in _config.Tracked)
            {
                if (_cooldowns.Exists(cd => cd.ActionId == actionId)) continue;

                var cd = new TrackedCooldown { ActionId = actionId, Settings = settings };
                if (sheet.TryGetRow(actionId, out Action row))
                {
                    cd.Name = row.Name.ToString();
                    cd.IconId = row.Icon;
                }

                Services.Logger.Information($"Added cooldown {actionId} = \"{cd.Name}\" (icon {cd.IconId})");
                _cooldowns.Add(cd);
            }

            // Same order than the config (alphabetical action name)
            _cooldowns.Sort((lhs, rhs) => string.Compare(lhs.Name, rhs.Name, StringComparison.Ordinal));
        }

        public unsafe void Update()
        {
            var am = ActionManager.Instance();
            var player = Services.ObjectTable.LocalPlayer;
            if (am == null || player == null) return;

            var inCombat = Services.Condition[ConditionFlag.InCombat];
            var now = DateTime.UtcNow;

            foreach (var cd in _cooldowns)
            {
                // Turn off the "pressed" animation after its duration
                if (cd.PressedSince is { } pressedAt &&
                    (now - pressedAt).TotalMilliseconds > TrackedCooldown.PressedFadeDuration)
                    cd.PressedSince = null;

                var maxCharges = ActionManager.GetMaxCharges(cd.ActionId, player.Level);

                // if the action has charges is available if we have one charge
                //  else if is not on cd
                var available = maxCharges > 0
                    ? am->GetCurrentCharges(cd.ActionId) >= 1
                    : !am->IsRecastTimerActive(ActionType.Action, cd.ActionId);

                var total = am->GetRecastTime(ActionType.Action, cd.ActionId);
                var elapsed = am->GetRecastTimeElapsed(ActionType.Action, cd.ActionId);
                cd.SecondsLeft = Math.Max(0f, total - elapsed);

                // Out of combat we keep re-baselining, so the next pull starts clean
                if (!inCombat)
                {
                    cd.ReadySince = null;
                    cd.PressedSince = null;
                    cd.Available = available;
                    continue;
                }

                if (!cd.Available && available) cd.ReadySince = now;

                if (cd.Available && !available)
                {
                    // pressed now
                    cd.ReadySince = null;
                    cd.PressedSince = now;
                    cd.IsPressed = true;
                }

                // we should hide now
                if (!cd.LingerForever && cd.ReadySince is { } since &&
                    (now - since).TotalMilliseconds > cd.LingerMs)
                {
                    cd.ReadySince = null;
                    cd.PressedSince = now;
                    cd.IsPressed = false;
                }

                cd.Available = available;
            }
        }
    }
}
