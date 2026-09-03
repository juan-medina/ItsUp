using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;
using Action = Lumina.Excel.Sheets.Action;

namespace ItsUp
{
    public enum CooldownState
    {
        Hidden,
        Warming,
        Ready,
        PressedFading,
        LingerFading
    }

    public class TrackedCooldown
    {
        public required uint ActionId { get; init; }

        public required AbilitySettings Settings { get; init; }

        public int WarnMs => Settings.WarnMs;
        public int LingerMs => Settings.LingerMs;
        public bool LingerForever => Settings.LingerForever;

        public uint ParentActionId => Settings.ParentActionId;
        public bool IsFollowup => Settings.IsFollowup;

        public string Name = string.Empty;
        public uint IconId;

        public bool Available;

        public float SecondsLeft;
        public const int PressedFadeDuration = 280;

        public CooldownState State { get; private set; } = CooldownState.Hidden;
        public DateTime StateEnteredAt { get; private set; }

        public bool Visible => State != CooldownState.Hidden;

        public void TransitionTo(CooldownState newState)
        {
            State = newState;
            StateEnteredAt = DateTime.UtcNow;
        }
    }

    public class CooldownTracker(Configuration config)
    {
        private readonly Configuration _config = config;
        private readonly List<TrackedCooldown> _cooldowns = [];
        private readonly List<TrackedCooldown> _activeCooldowns = [];

        public IReadOnlyList<TrackedCooldown> Cooldowns => _cooldowns;
        public IReadOnlyList<TrackedCooldown> ActiveCooldowns => _activeCooldowns;

        public void Sync()
        {
            var sheet = Services.DataManager.GetExcelSheet<Action>()!;

            // remove cooldown we don't track anymore
            var removed = _cooldowns.RemoveAll(cd => !_config.Tracked.ContainsKey(cd.ActionId));
            if (removed > 0)
                _activeCooldowns.RemoveAll(cd => !_config.Tracked.ContainsKey(cd.ActionId));

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
                bool available;
                if (cd.IsFollowup)
                {
                    available = cd.ParentActionId > 0 && am->GetAdjustedActionId(cd.ParentActionId) == cd.ActionId;
                    cd.SecondsLeft = 0f;
                }
                else
                {
                    var maxCharges = ActionManager.GetMaxCharges(cd.ActionId, player.Level);

                    // if the action has charges is available if we have one charge
                    //  else if is not on cd
                    available = maxCharges > 0
                        ? am->GetCurrentCharges(cd.ActionId) >= 1
                        : !am->IsRecastTimerActive(ActionType.Action, cd.ActionId);

                    var total = am->GetRecastTime(ActionType.Action, cd.ActionId);
                    var elapsed = am->GetRecastTimeElapsed(ActionType.Action, cd.ActionId);
                    cd.SecondsLeft = Math.Max(0f, total - elapsed);
                }
                var wasAvailable = cd.Available;
                cd.Available = available;

                // Evaluate state transitions
                var nextState = cd.State;

                if (!inCombat)
                {
                    nextState = CooldownState.Hidden;
                }
                else
                {
                    switch (cd.State)
                    {
                        case CooldownState.Hidden:
                            if (!available && cd.SecondsLeft > 0f && cd.SecondsLeft <= cd.WarnMs / 1000f)
                                nextState = CooldownState.Warming;
                            else if (!wasAvailable && available)
                                nextState = CooldownState.Ready;
                            break;

                        case CooldownState.Warming:
                            if (available)
                                nextState = CooldownState.Ready;
                            else if (cd.SecondsLeft > cd.WarnMs / 1000f) // e.g. reset
                                nextState = CooldownState.Hidden;
                            break;

                        case CooldownState.Ready:
                            if (!available)
                                nextState = CooldownState.PressedFading;
                            else if (!cd.LingerForever && (now - cd.StateEnteredAt).TotalMilliseconds > cd.LingerMs)
                                nextState = CooldownState.LingerFading;
                            break;

                        case CooldownState.PressedFading:
                        case CooldownState.LingerFading:
                            if ((now - cd.StateEnteredAt).TotalMilliseconds > TrackedCooldown.PressedFadeDuration)
                                nextState = CooldownState.Hidden;
                            break;
                    }
                }

                // Execute transition if changed
                if (cd.State != nextState)
                {
                    if (cd.State == CooldownState.Hidden && nextState != CooldownState.Hidden)
                        _activeCooldowns.Add(cd);
                    else if (cd.State != CooldownState.Hidden && nextState == CooldownState.Hidden)
                        _activeCooldowns.Remove(cd);

                    cd.TransitionTo(nextState);
                }
            }
        }
    }
}
