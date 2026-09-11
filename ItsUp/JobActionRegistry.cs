using System;
using System.Collections.Frozen;
using System.Collections.Generic;
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
        private const int ActionCategoryAbility = 4;
        private const int IsleSprintActionId = 29581;

        public IReadOnlyList<ClassJob> Jobs { get; }
        public FrozenDictionary<uint, string> JobAbbreviations { get; }
        public FrozenDictionary<uint, List<ActionItem>> JobActions { get; }
        public FrozenDictionary<uint, (string Name, uint Icon)> ActionInfo { get; }

        private readonly FrozenDictionary<uint, FrozenSet<uint>> _eligibleJobsByCategory;
        private readonly FrozenDictionary<uint, uint> _parentActionByFollowupAction;

        public JobActionRegistry()
        {
            var allActions = Services.DataManager.GetExcelSheet<Action>()!.ToList();
            ActionInfo = allActions.ToFrozenDictionary(
                action => action.RowId,
                action => (action.Name.ToString(), (uint)action.Icon));

            Jobs = LoadPlayableCombatJobs();
            JobAbbreviations = Jobs.ToFrozenDictionary(job => job.RowId, job => job.Abbreviation.ToString());

            _eligibleJobsByCategory = BuildJobEligibilityByCategory(Jobs);
            _parentActionByFollowupAction = BuildFollowupParentMap();
            JobActions = BuildAllJobActions(allActions);
        }

        private static List<ClassJob> LoadPlayableCombatJobs() =>
            [.. Services.DataManager.GetExcelSheet<ClassJob>()!
                .Where(job => job.Role > 0 && job.ItemSoulCrystal.Value.RowId > 0)
                .OrderBy(job => job.Name.ToString(), StringComparer.Ordinal)];

        private static FrozenDictionary<uint, uint> BuildFollowupParentMap()
        {
            var parentByFollowup = new Dictionary<uint, uint>();
            var replaceSheet = Services.DataManager.GetSubrowExcelSheet<ReplaceAction>();
            if (replaceSheet == null) return parentByFollowup.ToFrozenDictionary();

            foreach (var subrowCollection in replaceSheet)
            {
                foreach (var row in subrowCollection)
                {
                    var parentActionId = row.Action.RowId;
                    if (parentActionId == 0) continue;

                    foreach (var followupRef in row.ReplaceActions)
                    {
                        var followupActionId = followupRef.RowId;
                        if (followupActionId != 0 && followupActionId != parentActionId)
                            parentByFollowup[followupActionId] = parentActionId;
                    }
                }
            }

            return parentByFollowup.ToFrozenDictionary();
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

        private bool IsCategoryEligibleForJob(uint categoryId, uint jobId) =>
            jobId != 0 && _eligibleJobsByCategory.TryGetValue(categoryId, out var jobs) && jobs.Contains(jobId);

        private bool IsEligibleJobAction(Action action, ClassJob job)
        {
            if (action.IsPvP || !action.IsPlayerAction || action.RowId == IsleSprintActionId)
                return false;

            if (_parentActionByFollowupAction.ContainsKey(action.RowId))
                return false;

            var belongsToJobOrParentClass = action.ClassJob.RowId == job.RowId
                || action.ClassJob.RowId == job.ClassJobParent.RowId;

            var isAbilityOrLongCooldown = action.ActionCategory.RowId == ActionCategoryAbility
                || action.Recast100ms > 100;

            return belongsToJobOrParentClass && isAbilityOrLongCooldown;
        }

        private bool IsEligibleRoleAction(Action action, ClassJob job) =>
            action.IsRoleAction
            && action.ClassJobLevel != 0
            && IsCategoryEligibleForJob(action.ClassJobCategory.RowId, job.RowId);

        private List<uint> GetFollowupActionsForParent(uint parentActionId)
        {
            var followups = _parentActionByFollowupAction
                .Where(kvp => kvp.Value == parentActionId)
                .Select(kvp => kvp.Key)
                .ToList();

            followups.Sort(CompareByName);
            return followups;
        }

        private FrozenDictionary<uint, List<ActionItem>> BuildAllJobActions(List<Action> allActions)
        {
            var actionsByJob = new Dictionary<uint, List<ActionItem>>();

            foreach (var job in Jobs)
            {
                var jobActionIds = allActions
                    .Where(action => IsEligibleJobAction(action, job))
                    .Select(action => action.RowId)
                    .ToList();
                jobActionIds.Sort(CompareByName);

                var items = new List<ActionItem>();
                foreach (var rootActionId in jobActionIds)
                {
                    items.Add(new ActionItem(rootActionId, ParentActionId: 0, IsRoleAction: false));

                    foreach (var followupActionId in GetFollowupActionsForParent(rootActionId))
                        items.Add(new ActionItem(followupActionId, ParentActionId: rootActionId, IsRoleAction: false));
                }

                var roleActionIds = allActions
                    .Where(action => IsEligibleRoleAction(action, job))
                    .Select(action => action.RowId)
                    .ToList();
                roleActionIds.Sort(CompareByName);

                foreach (var roleActionId in roleActionIds)
                    items.Add(new ActionItem(roleActionId, ParentActionId: 0, IsRoleAction: true));

                actionsByJob[job.RowId] = items;
            }

            return actionsByJob.ToFrozenDictionary();
        }

        private int CompareByName(uint lhsActionId, uint rhsActionId) =>
            string.Compare(NameOf(lhsActionId), NameOf(rhsActionId), StringComparison.Ordinal);

        public string NameOf(uint actionId) =>
            ActionInfo.TryGetValue(actionId, out var info) && info.Name.Length > 0 ? info.Name : $"#{actionId}";
    }
}
