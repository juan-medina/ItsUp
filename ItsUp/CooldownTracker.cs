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

        /// <summary>Live reference into the config, so editing a timing takes effect without a resync.</summary>
        public required AbilitySettings Settings { get; init; }

        public int WarnMs => Settings.WarnMs;
        public int LingerMs => Settings.LingerMs;

        public string Name = string.Empty;
        public uint IconId;
        public uint ClassJobCategoryId;

        public bool Available;
        public bool WasAvailable = true;
        public float SecondsLeft;
        public DateTime? ReadySince;

        /// <summary>Whether the job you are currently on can press this at all.</summary>
        public bool Usable;

        public bool IsReady => ReadySince != null;
        public bool IsWarming => !Available && SecondsLeft > 0f && SecondsLeft <= WarnMs / 1000f;
        public bool Visible => Usable && (IsReady || IsWarming);
    }

    public class CooldownTracker
    {
        private readonly Configuration _config;
        private readonly List<TrackedCooldown> _tracked = new();

        public CooldownTracker(Configuration config) => _config = config;

        public IReadOnlyList<TrackedCooldown> Tracked => _tracked;

        /// <summary>
        /// Bring the tracked list in line with the config. Entries that survive keep their edge state,
        /// so ticking a box mid-fight does not disturb reminders already on screen.
        /// New entries get their name and icon resolved, logged so a wrong id is obvious.
        /// </summary>
        public void Sync()
        {
            var sheet = Services.DataManager.GetExcelSheet<Action>()!;

            _tracked.RemoveAll(cd => !_config.Tracked.ContainsKey(cd.ActionId));

            foreach (var (actionId, settings) in _config.Tracked)
            {
                if (_tracked.Exists(cd => cd.ActionId == actionId)) continue;

                var cd = new TrackedCooldown { ActionId = actionId, Settings = settings };
                if (sheet.TryGetRow(actionId, out Action row))
                {
                    cd.Name = row.Name.ToString();
                    cd.IconId = row.Icon;
                    cd.ClassJobCategoryId = row.ClassJobCategory.RowId;
                }

                Services.Logger.Information($"Tracking {actionId} = \"{cd.Name}\" (icon {cd.IconId})");
                _tracked.Add(cd);
            }

            // Panel order follows the picker's order rather than dictionary order, which is not stable
            // across removals.
            _tracked.Sort((lhs, rhs) => string.Compare(lhs.Name, rhs.Name, StringComparison.Ordinal));
        }

        public unsafe void Update()
        {
            var am = ActionManager.Instance();
            var player = Services.ObjectTable.LocalPlayer;
            if (am == null || player == null) return;

            var inCombat = Services.Condition[ConditionFlag.InCombat];
            var jobId = player.ClassJob.RowId;
            var now = DateTime.UtcNow;

            foreach (var cd in _tracked)
            {
                // Abilities belonging to another job never fire an edge anyway, but gating here keeps
                // them out of the panel and out of move mode's preview.
                cd.Usable = JobEligibility.CanUse(cd.ClassJobCategoryId, jobId);
                if (!cd.Usable)
                {
                    cd.ReadySince = null;
                    cd.Available = cd.WasAvailable = true;
                    continue;
                }

                // Deliberately not GetActionStatus: it also reports "unusable" while you are casting,
                // which would fire a fresh reveal on every single hard cast.
                var maxCharges = ActionManager.GetMaxCharges(cd.ActionId, player.Level);
                var available = maxCharges == 0
                    ? !am->IsRecastTimerActive(ActionType.Action, cd.ActionId)
                    : am->GetCurrentCharges(cd.ActionId) >= 1;

                var total = am->GetRecastTime(ActionType.Action, cd.ActionId);
                var elapsed = am->GetRecastTimeElapsed(ActionType.Action, cd.ActionId);
                cd.SecondsLeft = Math.Max(0f, total - elapsed);

                // Out of combat we keep re-baselining, so the next pull starts clean instead of
                // firing reveals for everything that happened to be on cooldown.
                if (!inCombat)
                {
                    cd.ReadySince = null;
                    cd.Available = cd.WasAvailable = available;
                    continue;
                }

                if (!cd.WasAvailable && available) cd.ReadySince = now;   // it came back
                if (cd.WasAvailable && !available) cd.ReadySince = null;  // you pressed it

                if (cd.ReadySince is { } since && cd.LingerMs > 0 &&
                    (now - since).TotalMilliseconds > cd.LingerMs)
                    cd.ReadySince = null;                                 // nagged long enough

                cd.Available = cd.WasAvailable = available;
            }
        }
    }
}
