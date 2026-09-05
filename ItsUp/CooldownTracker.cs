using System;
using System.Collections.Generic;
using System.Linq;
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
        private class SimulatedSkill
        {
            public required TrackedSkill Skill { get; init; }
            public float CooldownLeft { get; set; }
            public float TotalCooldown { get; set; }
            public float UpTimeLeft { get; set; }
            public bool IsUp { get; set; }
        }

        private readonly Configuration _config = config;
        private readonly List<TrackedSkill> _skills = [];
        private readonly List<SimulatedSkill> _simulatedSkills = [];
        private readonly Random _random = new();
        private DateTime _lastSimTime;

        public IReadOnlyList<TrackedSkill> Skills => _skills;
        public bool IsPreview { get; private set; }

        public event Action<TrackedSkill, float>? CloseToUp;
        public event Action<TrackedSkill>? Up;
        public event Action<TrackedSkill>? Down;
        public event System.Action? Reset;

        public void TogglePreview()
        {
            if (IsPreview)
                StopPreview();
            else
                StartPreview();
        }

        public void StopPreview()
        {
            if (!IsPreview) return;
            IsPreview = false;
            _simulatedSkills.Clear();
            Reset?.Invoke();
        }

        public void StartPreview()
        {
            if (IsPreview) return;

            var player = Services.ObjectTable.LocalPlayer;
            var currentJobId = player?.ClassJob.RowId ?? 0;
            var parentJobId = player?.ClassJob.Value.ClassJobParent.RowId ?? 0;

            var sheet = Services.DataManager.GetExcelSheet<Action>()!;

            // 1. Try to find skills tracked on the current job
            var pool = _skills.Where(s =>
            {
                if (!sheet.TryGetRow(s.ActionId, out var row)) return false;
                return row.ClassJob.RowId == currentJobId ||
                       (parentJobId > 0 && row.ClassJob.RowId == parentJobId) ||
                       row.IsRoleAction;
            }).ToList();

            // 2. If none tracked for this job, pick random eligible abilities for this job
            if (pool.Count == 0)
            {
                var candidateActions = sheet
                    .Where(a => !a.IsPvP
                                && (a.ClassJob.RowId == currentJobId || (parentJobId > 0 && a.ClassJob.RowId == parentJobId) || a.IsRoleAction)
                                && a.IsPlayerAction
                                && (a.ActionCategory.RowId == 4 || a.Recast100ms > 100))
                    .OrderBy(_ => _random.Next())
                    .Take(3)
                    .ToList();

                foreach (var action in candidateActions)
                {
                    pool.Add(new()
                    {
                        ActionId = action.RowId,
                        Name = action.Name.ToString(),
                        IconId = action.Icon,
                        Settings = new AbilitySettings
                        {
                            WarnMs = _config.DefaultWarnMs,
                            LingerMs = _config.DefaultLingerMs,
                            LingerForever = false
                        }
                    });
                }
            }

            if (pool.Count == 0) return;

            var selected = pool.OrderBy(_ => _random.Next()).Take(Math.Min(pool.Count, 3)).ToList();

            _simulatedSkills.Clear();
            _lastSimTime = DateTime.UtcNow;
            IsPreview = true;
            Reset?.Invoke();

            for (var i = 0; i < selected.Count; i++)
            {
                var skill = selected[i];
                var sim = new SimulatedSkill
                {
                    Skill = skill,
                    TotalCooldown = (float)_random.Next(8, 14)
                };

                if (i == 0)
                {
                    // Starts in warning window, ready in ~2.5s
                    var warnSecs = Math.Max(3f, skill.WarnMs / 1000f);
                    sim.CooldownLeft = 2.5f;
                    sim.TotalCooldown = warnSecs + 2f;
                }
                else if (i == 1)
                {
                    // Starts ready immediately
                    sim.IsUp = true;
                    sim.UpTimeLeft = 3.0f;
                    Up?.Invoke(skill);
                }
                else
                {
                    // Starts on cooldown, recovers later
                    sim.CooldownLeft = 6.0f;
                }

                _simulatedSkills.Add(sim);
            }
        }

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
            if (inCombat)
            {
                if (IsPreview)
                {
                    StopPreview();
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

                return;
            }

            if (IsPreview)
            {
                UpdatePreviewSimulation();
                return;
            }

            // Out of combat: continuously re-baseline availability so a pull doesn't fire stale reveals
            var resetNeeded = false;
            foreach (var skill in _skills)
            {
                var (isUp, _) = EvaluateAbility(am, (byte)player.Level, skill);
                // Follow-ups are never passively available — they require the parent to be used
                skill.Available = !skill.IsFollowup && isUp;
                if (skill.Status != SkillStatus.Down)
                {
                    skill.Status = SkillStatus.Down;
                    resetNeeded = true;
                }
            }

            if (resetNeeded)
                Reset?.Invoke();
        }

        private void UpdatePreviewSimulation()
        {
            var now = DateTime.UtcNow;
            var delta = (float)(now - _lastSimTime).TotalSeconds;
            _lastSimTime = now;
            if (delta <= 0f) return;
            if (delta > 0.5f) delta = 0.5f;

            foreach (var sim in _simulatedSkills)
            {
                if (sim.IsUp)
                {
                    sim.UpTimeLeft -= delta;
                    if (sim.UpTimeLeft <= 0f)
                    {
                        // Simulated player presses the ability!
                        sim.IsUp = false;
                        Down?.Invoke(sim.Skill);
                        sim.TotalCooldown = (float)_random.Next(8, 15);
                        sim.CooldownLeft = sim.TotalCooldown;
                    }
                }
                else
                {
                    sim.CooldownLeft -= delta;
                    if (sim.CooldownLeft <= 0f)
                    {
                        // Ability came back!
                        sim.IsUp = true;
                        sim.UpTimeLeft = (float)(2.0 + _random.NextDouble() * 2.5); // 2.0s to 4.5s up
                        Up?.Invoke(sim.Skill);
                    }
                    else if (!sim.Skill.IsFollowup && sim.CooldownLeft <= sim.Skill.WarnMs / 1000f)
                    {
                        // In leading warning window
                        CloseToUp?.Invoke(sim.Skill, sim.CooldownLeft);
                    }
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
