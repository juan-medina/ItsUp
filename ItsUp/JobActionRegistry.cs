using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Lumina.Excel.Sheets;
using Action = Lumina.Excel.Sheets.Action;

namespace ItsUp
{
    public record ActionItem(uint ActionId, uint ParentActionId = 0, bool IsRoleAction = false)
    {
        public bool IsFollowup => ParentActionId != 0;
    }

    public class JobActionRegistry
    {
        private enum ActionCategoryId : uint
        {
            Ability = 4,
        }

        private enum ReplacementType : sbyte
        {
            None = 0,
            WeaponSkillBranch = 1,
            StatusProc = 2,
            TraitUpgrade = 3,
            GaugeState = 4,
            ActiveZoneEffect = 7,
            ComboStep = 8,
            ProcChain = 9,
            PvPCombo = 10,
            StancePhase = 12,
        }

        private const uint IsleSprintActionId = 29581;

        // Spells/weapon kills with cooldown > 10s (excludes standard 2.5s GCDs)
        private const int MinLongCooldownSeconds = 10;
        private const int TicksPerSecond100ms = 10;
        private const int MinLongCooldownTicks = MinLongCooldownSeconds * TicksPerSecond100ms;

        public IReadOnlyList<ClassJob> Jobs { get; }
        public FrozenDictionary<uint, string> JobNames { get; }
        public FrozenDictionary<uint, string> JobAbbreviations { get; }
        public FrozenDictionary<uint, List<ActionItem>> JobActions { get; }
        public FrozenDictionary<uint, (string Name, uint Icon)> ActionInfo { get; }

        public JobActionRegistry()
        {
            var allActions = Services.DataManager.GetExcelSheet<Action>()!.ToList();
            ActionInfo = allActions.ToFrozenDictionary(
                action => action.RowId,
                action => (action.Name.ToString(), (uint)action.Icon));

            var textInfo = CultureInfo.InvariantCulture.TextInfo;
            Jobs = LoadPlayableCombatJobs();
            JobNames = Jobs.ToFrozenDictionary(job => job.RowId, job => textInfo.ToTitleCase(job.Name.ToString()));
            JobAbbreviations = Jobs.ToFrozenDictionary(job => job.RowId, job => job.Abbreviation.ToString());

            JobActions = BuildAllJobActions(Jobs, allActions, ActionInfo);
        }

        private static List<ClassJob> LoadPlayableCombatJobs() =>
            [.. Services.DataManager.GetExcelSheet<ClassJob>()!
                .Where(job => job.Role > 0 && job.ItemSoulCrystal.Value.RowId > 0)
                .OrderBy(job => job.Name.ToString(), StringComparer.Ordinal)];

        private static (FrozenDictionary<uint, List<uint>> FollowupsByParent, FrozenSet<uint> NonRootActions) LoadReplacements()
        {
            var followupsByParent = new Dictionary<uint, List<uint>>();
            var nonRootActions = new HashSet<uint>();
            var replaceSheet = Services.DataManager.GetSubrowExcelSheet<ReplaceAction>();
            if (replaceSheet == null)
                return (followupsByParent.ToFrozenDictionary(), nonRootActions.ToFrozenSet());

            foreach (var subrowCollection in replaceSheet)
            {
                foreach (var row in subrowCollection)
                {
                    var parentActionId = row.Action.RowId;
                    if (parentActionId == 0) continue;

                    for (var i = 0; i < 4; i++)
                    {
                        var type = i switch
                        {
                            0 => row.Type1,
                            1 => row.Type2,
                            2 => row.Type3,
                            _ => row.Type4
                        };

                        var targetActionId = row.ReplaceActions[i].RowId;
                        if (targetActionId == 0 || targetActionId == parentActionId) continue;

                        nonRootActions.Add(targetActionId);

                        if (type != (sbyte)ReplacementType.TraitUpgrade && type != (sbyte)ReplacementType.None)
                        {
                            if (!followupsByParent.TryGetValue(parentActionId, out var list))
                            {
                                list = [];
                                followupsByParent[parentActionId] = list;
                            }

                            if (!list.Contains(targetActionId))
                                list.Add(targetActionId);
                        }
                    }
                }
            }

            return (followupsByParent.ToFrozenDictionary(), nonRootActions.ToFrozenSet());
        }

        private static FrozenDictionary<uint, FrozenSet<uint>> BuildJobEligibilityByCategory(IReadOnlyList<ClassJob> jobs)
        {
            var categoryProperties = typeof(ClassJobCategory).GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var jobPropertyBindings = new List<(PropertyInfo Property, uint JobId)>();

            foreach (var job in jobs)
            {
                var abbreviation = job.Abbreviation.ToString();
                if (abbreviation.Length == 0) continue;

                var property = categoryProperties.FirstOrDefault(p => p.Name == abbreviation && p.PropertyType == typeof(bool));
                if (property != null)
                    jobPropertyBindings.Add((property, job.RowId));
            }

            var eligibleJobsByCategory = new Dictionary<uint, FrozenSet<uint>>();
            foreach (var category in Services.DataManager.GetExcelSheet<ClassJobCategory>()!)
            {
                object boxedCategory = category;
                eligibleJobsByCategory[category.RowId] = jobPropertyBindings
                    .Where(binding => (bool)binding.Property.GetValue(boxedCategory)!)
                    .Select(binding => binding.JobId)
                    .ToFrozenSet();
            }

            return eligibleJobsByCategory.ToFrozenDictionary();
        }

        private static FrozenDictionary<uint, List<ActionItem>> BuildAllJobActions(
            IReadOnlyList<ClassJob> jobs,
            List<Action> allActions,
            FrozenDictionary<uint, (string Name, uint Icon)> actionInfo)
        {
            var eligibleJobsByCategory = BuildJobEligibilityByCategory(jobs);
            var (followupsByParent, nonRootActions) = LoadReplacements();

            var jobsByClassId = new Dictionary<uint, List<uint>>();
            var jobActionsByJob = new Dictionary<uint, List<uint>>();
            var roleActionsByJob = new Dictionary<uint, List<uint>>();

            foreach (var job in jobs)
            {
                jobActionsByJob[job.RowId] = [];
                roleActionsByJob[job.RowId] = [];

                if (!jobsByClassId.TryGetValue(job.RowId, out var list))
                    jobsByClassId[job.RowId] = list = [];
                list.Add(job.RowId);

                var parentId = job.ClassJobParent.RowId;
                if (parentId > 0 && parentId != job.RowId)
                {
                    if (!jobsByClassId.TryGetValue(parentId, out var pList))
                        jobsByClassId[parentId] = pList = [];
                    pList.Add(job.RowId);
                }
            }

            foreach (var action in allActions)
            {
                if (action.IsRoleAction)
                {
                    if (action.ClassJobLevel != 0 &&
                        eligibleJobsByCategory.TryGetValue(action.ClassJobCategory.RowId, out var eligibleJobs))
                    {
                        foreach (var jobId in eligibleJobs)
                        {
                            if (roleActionsByJob.TryGetValue(jobId, out var roleList))
                                roleList.Add(action.RowId);
                        }
                    }
                    continue;
                }

                if (action.IsPvP || !action.IsPlayerAction || action.RowId == IsleSprintActionId || nonRootActions.Contains(action.RowId))
                    continue;

                var isAbilityOrLongCooldown = action.ActionCategory.RowId == (uint)ActionCategoryId.Ability
                    || action.Recast100ms > MinLongCooldownTicks;

                if (!isAbilityOrLongCooldown)
                    continue;

                if (jobsByClassId.TryGetValue(action.ClassJob.RowId, out var targetJobs))
                {
                    foreach (var jobId in targetJobs)
                        jobActionsByJob[jobId].Add(action.RowId);
                }
            }

            int CompareByName(uint lhs, uint rhs)
            {
                var lhsName = actionInfo.TryGetValue(lhs, out var lInfo) ? lInfo.Name : string.Empty;
                var rhsName = actionInfo.TryGetValue(rhs, out var rInfo) ? rInfo.Name : string.Empty;
                return string.Compare(lhsName, rhsName, StringComparison.Ordinal);
            }

            var actionsByJob = new Dictionary<uint, List<ActionItem>>();

            foreach (var job in jobs)
            {
                var jobActionIds = jobActionsByJob[job.RowId];
                jobActionIds.Sort(CompareByName);

                var items = new List<ActionItem>();
                foreach (var rootActionId in jobActionIds)
                {
                    items.Add(new ActionItem(rootActionId, ParentActionId: 0, IsRoleAction: false));

                    if (followupsByParent.TryGetValue(rootActionId, out var followups))
                    {
                        var sortedFollowups = followups.OrderBy(id => id, Comparer<uint>.Create(CompareByName));
                        foreach (var followupActionId in sortedFollowups)
                            items.Add(new ActionItem(followupActionId, ParentActionId: rootActionId, IsRoleAction: false));
                    }
                }

                var roleActionIds = roleActionsByJob[job.RowId];
                roleActionIds.Sort(CompareByName);

                foreach (var roleActionId in roleActionIds)
                    items.Add(new ActionItem(roleActionId, ParentActionId: 0, IsRoleAction: true));

                actionsByJob[job.RowId] = items;
            }

            return actionsByJob.ToFrozenDictionary();
        }

        public string NameOf(uint actionId) =>
            ActionInfo.TryGetValue(actionId, out var info) && info.Name.Length > 0 ? info.Name : $"#{actionId}";

        public string GetJobDisplayName(uint jobId)
        {
            if (JobNames.TryGetValue(jobId, out var name))
            {
                var abbrev = JobAbbreviations.TryGetValue(jobId, out var ab) ? ab : string.Empty;
                return abbrev.Length > 0 ? $"{name} ({abbrev})" : name;
            }
            return $"Job #{jobId}";
        }
    }
}
