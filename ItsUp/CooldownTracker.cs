using System;
using System.Collections.Generic;
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

    public enum DismissReason
    {
        Pressed,
        LingerExpired,
        CombatEnded
    }

    public interface ICooldownListener
    {
        void OnUpcoming(uint actionId, float secondsLeft);
        void OnNowUp(uint actionId);
        void OnDismissed(uint actionId, DismissReason reason);
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

    public class CooldownTracker(Configuration config) : ICooldownListener
    {
        private readonly Configuration _config = config;
        private readonly List<TrackedCooldown> _cooldowns = [];
        private readonly List<TrackedCooldown> _activeCooldowns = [];
        private CooldownWatcher? _watcher;

        public IReadOnlyList<TrackedCooldown> Cooldowns => _cooldowns;
        public IReadOnlyList<TrackedCooldown> ActiveCooldowns => _activeCooldowns;

        public void SetWatcher(CooldownWatcher watcher) => _watcher = watcher;

        public void Sync()
        {
            var sheet = Services.DataManager.GetExcelSheet<Action>()!;

            // Remove cooldowns we don't track anymore
            var removed = _cooldowns.RemoveAll(cd => !_config.Tracked.ContainsKey(cd.ActionId));
            if (removed > 0)
                _activeCooldowns.RemoveAll(cd => !_config.Tracked.ContainsKey(cd.ActionId));

            // Add missing cooldowns that we need to track
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

            _watcher?.Sync();
        }

        public void OnUpcoming(uint actionId, float secondsLeft)
        {
            var cd = _cooldowns.Find(c => c.ActionId == actionId);
            if (cd == null) return;

            cd.SecondsLeft = secondsLeft;
            if (cd.State != CooldownState.Warming)
            {
                if (cd.State == CooldownState.Hidden)
                    _activeCooldowns.Add(cd);
                cd.TransitionTo(CooldownState.Warming);
            }
        }

        public void OnNowUp(uint actionId)
        {
            var cd = _cooldowns.Find(c => c.ActionId == actionId);
            if (cd == null) return;

            if (cd.State == CooldownState.Hidden)
                _activeCooldowns.Add(cd);
            cd.TransitionTo(CooldownState.Ready);
        }

        public void OnDismissed(uint actionId, DismissReason reason)
        {
            var cd = _cooldowns.Find(c => c.ActionId == actionId);
            if (cd == null) return;

            switch (reason)
            {
                case DismissReason.Pressed:
                    cd.TransitionTo(CooldownState.PressedFading);
                    break;
                case DismissReason.LingerExpired:
                    cd.TransitionTo(CooldownState.LingerFading);
                    break;
                case DismissReason.CombatEnded:
                    cd.TransitionTo(CooldownState.Hidden);
                    _activeCooldowns.Remove(cd);
                    break;
            }
        }

        public void Update()
        {
            var now = DateTime.UtcNow;

            for (var i = _activeCooldowns.Count - 1; i >= 0; i--)
            {
                var cd = _activeCooldowns[i];
                if (cd.State == CooldownState.PressedFading || cd.State == CooldownState.LingerFading)
                {
                    if ((now - cd.StateEnteredAt).TotalMilliseconds > TrackedCooldown.PressedFadeDuration)
                    {
                        cd.TransitionTo(CooldownState.Hidden);
                        _activeCooldowns.RemoveAt(i);
                    }
                }
            }
        }
    }
}
