using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Lumina.Excel.Sheets;

namespace ItsUp
{
    /// <summary>
    /// Which jobs each <c>ClassJobCategory</c> covers.
    ///
    /// The sheet stores this as one bool column per job abbreviation, so it is read by reflection
    /// once at load rather than hand-maintained. Going through the category rather than
    /// <c>Action.ClassJob</c> is what makes base-class and role actions resolve correctly: a
    /// Conjurer action's category includes White Mage, which a direct job comparison would miss.
    /// </summary>
    public static class JobEligibility
    {
        private static FrozenDictionary<uint, FrozenSet<uint>> _jobsByCategory =
            FrozenDictionary<uint, FrozenSet<uint>>.Empty;

        public static void Build()
        {
            var properties = typeof(ClassJobCategory).GetProperties(BindingFlags.Public | BindingFlags.Instance);

            var columns = new List<(PropertyInfo Property, uint JobId)>();
            foreach (var job in Services.DataManager.GetExcelSheet<ClassJob>()!)
            {
                var abbreviation = job.Abbreviation.ToString();
                if (abbreviation.Length == 0) continue;

                var property = properties.FirstOrDefault(p => p.Name == abbreviation && p.PropertyType == typeof(bool));
                if (property != null) columns.Add((property, job.RowId));
            }

            var map = new Dictionary<uint, FrozenSet<uint>>();
            foreach (var category in Services.DataManager.GetExcelSheet<ClassJobCategory>()!)
            {
                object boxed = category;
                map[category.RowId] = columns
                    .Where(c => (bool)c.Property.GetValue(boxed)!)
                    .Select(c => c.JobId)
                    .ToFrozenSet();
            }

            _jobsByCategory = map.ToFrozenDictionary();
            Services.Logger.Information($"Resolved job eligibility for {_jobsByCategory.Count} action categories.");
        }

        public static bool CanUse(uint categoryId, uint jobId) =>
            jobId != 0 && _jobsByCategory.TryGetValue(categoryId, out var jobs) && jobs.Contains(jobId);
    }
}
