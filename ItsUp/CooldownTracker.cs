using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;
using Action = Lumina.Excel.Sheets.Action;

namespace ItsUp
{
    public enum SkillStatus
    {
        Down,
        CloseToUp,
        Up
    }

    public class TrackedSkill
    {
        public required uint ActionId { get; init; }
        public required AbilitySettings Settings { get; init; }

        public uint ParentActionId => Settings.ParentActionId;
        public bool IsFollowup => Settings.IsFollowup;
        public int WarnMs => Settings.WarnMs;

        public string Name { get; set; } = string.Empty;
        public uint IconId { get; set; }

        public bool Available { get; set; }
        public float SecondsLeft { get; set; }
        public SkillStatus Status { get; set; } = SkillStatus.Down;
    }

    public class CooldownTracker(Configuration config)
    {
        private readonly Configuration _config = config;
        private readonly List<TrackedSkill> _skills = [];

        public IReadOnlyList<TrackedSkill> Skills => _skills;

        public event Action<TrackedSkill, float>? CloseToUp;
        public event Action<TrackedSkill>? Up;
        public event Action<TrackedSkill>? Down;
        public event System.Action? Reset;

        public void Sync()
        {
            var sheet = Services.DataManager.GetExcelSheet<Action>()!;

            // Remove skills we don't track anymore
            _skills.RemoveAll(s => !_config.Tracked.ContainsKey(s.ActionId));

            // Add missing skills
            foreach (var (actionId, settings) in _config.Tracked)
            {
                if (_skills.Exists(s => s.ActionId == actionId)) continue;

                var skill = new TrackedSkill { ActionId = actionId, Settings = settings };
                if (sheet.TryGetRow(actionId, out var row))
                {
                    skill.Name = row.Name.ToString();
                    skill.IconId = row.Icon;
                }

                Services.Logger.Information($"Tracking skill {actionId} = \"{skill.Name}\" (icon {skill.IconId})");
                _skills.Add(skill);
            }
        }

        public unsafe void Update()
        {
            var am = ActionManager.Instance();
            var player = Services.ObjectTable.LocalPlayer;
            if (am == null || player == null) return;

            var inCombat = Services.Condition[ConditionFlag.InCombat];
            if (!inCombat)
            {
                // Out of combat: continuously re-baseline availability so a pull doesn't fire stale reveals
                var resetNeeded = false;
                foreach (var skill in _skills)
                {
                    var (isUp, _) = EvaluateAbility(am, (byte)player.Level, skill);
                    skill.Available = isUp;
                    if (skill.Status != SkillStatus.Down)
                    {
                        skill.Status = SkillStatus.Down;
                        resetNeeded = true;
                    }
                }

                if (resetNeeded)
                    Reset?.Invoke();

                return;
            }

            foreach (var skill in _skills)
            {
                var (isUp, secondsRemaining) = EvaluateAbility(am, (byte)player.Level, skill);
                var wasUp = skill.Available;
                skill.Available = isUp;
                skill.SecondsLeft = secondsRemaining;

                if (!wasUp && isUp)
                {
                    // Transition: was down, now UP
                    skill.Status = SkillStatus.Up;
                    Up?.Invoke(skill);
                }
                else if (wasUp && !isUp)
                {
                    // Transition: was up, now DOWN (used, or proc buff ended)
                    skill.Status = SkillStatus.Down;
                    Down?.Invoke(skill);
                }
                else if (!isUp && secondsRemaining > 0f && secondsRemaining <= skill.WarnMs / 1000f)
                {
                    // Approaching UP: within the leading heads-up window
                    skill.Status = SkillStatus.CloseToUp;
                    CloseToUp?.Invoke(skill, secondsRemaining);
                }
                else if (skill.Status == SkillStatus.CloseToUp && secondsRemaining > skill.WarnMs / 1000f)
                {
                    // Was close, but timer reset or pushed back
                    skill.Status = SkillStatus.Down;
                    Down?.Invoke(skill);
                }
            }
        }

        private static unsafe (bool IsUp, float SecondsRemaining) EvaluateAbility(ActionManager* am, byte playerLevel, TrackedSkill skill)
        {
            // 1. Follow-up / Proc ability
            if (skill.IsFollowup)
            {
                var isUp = skill.ParentActionId > 0 && am->GetAdjustedActionId(skill.ParentActionId) == skill.ActionId;
                return (isUp, 0f);
            }

            // 2. Charge ability
            var maxCharges = ActionManager.GetMaxCharges(skill.ActionId, playerLevel);
            if (maxCharges > 0)
            {
                var hasCharge = am->GetCurrentCharges(skill.ActionId) >= 1;
                var total = am->GetRecastTime(ActionType.Action, skill.ActionId);
                var elapsed = am->GetRecastTimeElapsed(ActionType.Action, skill.ActionId);
                return (hasCharge, Math.Max(0f, total - elapsed));
            }

            // 3. Standard cooldown ability
            var isReady = !am->IsRecastTimerActive(ActionType.Action, skill.ActionId);
            var recastTotal = am->GetRecastTime(ActionType.Action, skill.ActionId);
            var recastElapsed = am->GetRecastTimeElapsed(ActionType.Action, skill.ActionId);
            return (isReady, Math.Max(0f, recastTotal - recastElapsed));
        }
    }
}
