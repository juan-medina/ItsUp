using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace ItsUp
{
    public class CooldownWatcher(Configuration config, ICooldownListener listener)
    {
        private class WatchedAction
        {
            public required uint ActionId { get; init; }
            public required AbilitySettings Settings { get; init; }
            public bool Available { get; set; }
            public float SecondsLeft { get; set; }
            public CooldownState State { get; set; } = CooldownState.Hidden;
            public DateTime StateEnteredAt { get; set; }

            public void TransitionTo(CooldownState newState)
            {
                State = newState;
                StateEnteredAt = DateTime.UtcNow;
            }
        }

        private readonly Configuration _config = config;
        private readonly ICooldownListener _listener = listener;
        private readonly List<WatchedAction> _watched = [];

        public void Sync()
        {
            _watched.RemoveAll(w => !_config.Tracked.ContainsKey(w.ActionId));

            foreach (var (actionId, settings) in _config.Tracked)
            {
                if (_watched.Exists(w => w.ActionId == actionId)) continue;
                _watched.Add(new WatchedAction { ActionId = actionId, Settings = settings });
            }
        }

        public unsafe void Update()
        {
            var am = ActionManager.Instance();
            var player = Services.ObjectTable.LocalPlayer;
            if (am == null || player == null) return;

            var inCombat = Services.Condition[ConditionFlag.InCombat];
            var now = DateTime.UtcNow;

            foreach (var w in _watched)
            {
                var availability = AbilityEvaluator.Evaluate(am, (byte)player.Level, w.ActionId, w.Settings.ParentActionId);
                var available = availability.IsUp;
                w.SecondsLeft = availability.SecondsRemaining;
                var wasAvailable = w.Available;
                w.Available = available;

                var nextState = w.State;
                DismissReason? dismissReason = null;

                if (!inCombat)
                {
                    if (w.State != CooldownState.Hidden)
                        dismissReason = DismissReason.CombatEnded;
                    nextState = CooldownState.Hidden;
                }
                else
                {
                    switch (w.State)
                    {
                        case CooldownState.Hidden:
                            if (!available && w.SecondsLeft > 0f && w.SecondsLeft <= w.Settings.WarnMs / 1000f)
                                nextState = CooldownState.Warming;
                            else if (!wasAvailable && available)
                                nextState = CooldownState.Ready;
                            break;

                        case CooldownState.Warming:
                            if (available)
                                nextState = CooldownState.Ready;
                            else if (w.SecondsLeft > w.Settings.WarnMs / 1000f) // e.g. reset
                            {
                                nextState = CooldownState.Hidden;
                                dismissReason = DismissReason.CombatEnded;
                            }
                            else
                            {
                                _listener.OnUpcoming(w.ActionId, w.SecondsLeft);
                            }
                            break;

                        case CooldownState.Ready:
                            if (!available)
                            {
                                nextState = CooldownState.PressedFading;
                                dismissReason = DismissReason.Pressed;
                            }
                            else if (!w.Settings.LingerForever && (now - w.StateEnteredAt).TotalMilliseconds > w.Settings.LingerMs)
                            {
                                nextState = CooldownState.LingerFading;
                                dismissReason = DismissReason.LingerExpired;
                            }
                            break;

                        case CooldownState.PressedFading:
                        case CooldownState.LingerFading:
                            if ((now - w.StateEnteredAt).TotalMilliseconds > TrackedCooldown.PressedFadeDuration)
                                nextState = CooldownState.Hidden;
                            break;
                    }
                }

                if (w.State != nextState)
                {
                    w.TransitionTo(nextState);

                    if (nextState == CooldownState.Warming)
                        _listener.OnUpcoming(w.ActionId, w.SecondsLeft);
                    else if (nextState == CooldownState.Ready)
                        _listener.OnNowUp(w.ActionId);
                    else if (dismissReason.HasValue)
                        _listener.OnDismissed(w.ActionId, dismissReason.Value);
                }
            }
        }
    }
}
