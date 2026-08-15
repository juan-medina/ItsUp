using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Lumina.Excel.Sheets;
using Action = Lumina.Excel.Sheets.Action;

namespace ItsUp.Windows
{
    public class ConfigWindow : Window
    {
        /// <summary>Proc-gated GCDs the eligibility filter misses. 33 is Red Mage.</summary>
        private static readonly Dictionary<uint, uint[]> JobActionWhitelist = new()
        {
            { 33, [7444, 7445, 37018, 37023, 37024, 37025, 37026, 37027, 37028] },
        };

        private enum View { Role, Job }

        private readonly Configuration _config;
        private readonly CooldownTracker _tracker;
        private readonly CooldownWindow _panel;

        private readonly FrozenDictionary<uint, string> _actionNames;
        private readonly FrozenDictionary<uint, uint> _actionIcons;
        private readonly FrozenDictionary<uint, string> _jobAbbreviations;
        private readonly FrozenDictionary<uint, List<uint>> _jobActionIds;
        private readonly List<uint> _roleActionIds;
        private readonly List<ClassJob> _jobs;

        private View _view = View.Role;
        private uint _selectedJobId;

        public ConfigWindow(Configuration config, CooldownTracker tracker, CooldownWindow panel)
            : base("It's Up — Settings##itsupconfig", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
        {
            _config = config;
            _tracker = tracker;
            _panel = panel;

            Size = new Vector2(620, 460);
            SizeCondition = ImGuiCond.FirstUseEver;
            SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(520, 360),
                MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
            };

            // Everything below is sheet data that never changes, so it is built once here rather
            // than re-filtered every frame.
            var allActions = Services.DataManager.GetExcelSheet<Action>()!.ToList();
            _actionNames = allActions.ToFrozenDictionary(a => a.RowId, a => a.Name.ToString());
            _actionIcons = allActions.ToFrozenDictionary(a => a.RowId, a => (uint)a.Icon);

            _jobs = Services.DataManager.GetExcelSheet<ClassJob>()!
                .Where(j => j.Role > 0 && j.ItemSoulCrystal.Value.RowId > 0)
                .OrderBy(j => j.Name.ToString(), StringComparer.Ordinal)
                .ToList();

            _jobAbbreviations = _jobs.ToFrozenDictionary(j => j.RowId, j => j.Abbreviation.ToString());

            var byJob = new Dictionary<uint, List<uint>>();
            foreach (var job in _jobs)
            {
                // The recast filter is what keeps 2.5s GCD spells out of the list — without it the
                // panel would flicker on every cast.
                var ids = allActions
                    .Where(a => !a.IsPvP
                                && (a.ClassJob.RowId == job.RowId || a.ClassJob.RowId == job.ClassJobParent.RowId)
                                && a.IsPlayerAction
                                && (a.ActionCategory.RowId == 4 || a.Recast100ms > 100)
                                && a.RowId != 29581)
                    .Select(a => a.RowId)
                    .ToList();

                if (JobActionWhitelist.TryGetValue(job.RowId, out var whitelist))
                    ids.AddRange(whitelist.Where(id => !ids.Contains(id) && _actionNames.ContainsKey(id)));

                ids.Sort(CompareByName);
                byJob[job.RowId] = ids;
            }
            _jobActionIds = byJob.ToFrozenDictionary();

            _roleActionIds = allActions
                .Where(a => a.IsRoleAction && a.ClassJobLevel != 0)
                .Select(a => a.RowId)
                .ToList();
            _roleActionIds.Sort(CompareByName);
        }

        private int CompareByName(uint lhs, uint rhs) =>
            string.Compare(NameOf(lhs), NameOf(rhs), StringComparison.Ordinal);

        /// <summary>A saved id can outlive its sheet row. Show it anyway, so it can be unticked.</summary>
        private string NameOf(uint actionId) =>
            _actionNames.TryGetValue(actionId, out var name) && name.Length > 0 ? name : $"#{actionId}";

        /// <summary>Open on the job you are actually playing rather than an arbitrary first entry.</summary>
        private void SelectCurrentJob()
        {
            var jobId = Services.ObjectTable.LocalPlayer?.ClassJob.RowId ?? 0;
            if (jobId == 0 || !_jobActionIds.ContainsKey(jobId)) return;

            _view = View.Job;
            _selectedJobId = jobId;
        }

        public override void OnOpen() => SelectCurrentJob();

        public override void OnClose() => _config.Save();

        public override void Draw()
        {
            DrawDefaults();
            ImGui.Separator();

            using (ImRaii.Child("sidebar", new Vector2(ImGui.GetContentRegionAvail().X * 0.22f, 0), true))
                DrawSidebar();

            ImGui.SameLine();

            using (ImRaii.Child("abilities", new Vector2(0, 0), true))
                DrawAbilities();
        }

        private void DrawDefaults()
        {
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("New abilities start with");
            ImGui.SameLine();

            ImGui.TextUnformatted("heads-up");
            ImGui.SameLine();
            var warn = _config.DefaultWarnMs;
            if (DrawSecondsInput("##defaultwarn", ref warn)) _config.DefaultWarnMs = warn;
            if (ImGui.IsItemDeactivatedAfterEdit()) _config.Save();

            ImGui.SameLine();
            ImGui.TextUnformatted("stays up");
            ImGui.SameLine();
            var linger = _config.DefaultLingerMs;
            if (DrawSecondsInput("##defaultlinger", ref linger)) _config.DefaultLingerMs = linger;
            if (ImGui.IsItemDeactivatedAfterEdit()) _config.Save();

            // Spelling the current values out as a sentence is how "0 = until you press it" gets
            // taught, rather than leaving it buried in a tooltip.
            TextMuted(Describe(_config.DefaultWarnMs, _config.DefaultLingerMs));

            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Bar anchor");
            ImGui.SameLine();

            var anchor = (int)_config.Anchor;
            ImGui.SetNextItemWidth(110 * ImGuiHelpers.GlobalScale);
            if (ImGui.Combo("##anchor", ref anchor, "Left\0Centre\0Right\0"))
            {
                _config.Anchor = (BarAnchor)anchor;
                _config.Save();
            }
            Tooltip("Which point of the bar stays pinned as icons come and go.\n\n"
                    + "Jobs track different numbers of abilities, and the bar is only as wide as what is\n"
                    + "on screen, so without a pinned point it would shift every time the count changed.");

            ImGui.SameLine();
            if (ImGui.Button(_panel.Unlocked ? "Lock panel" : "Move panel"))
                _panel.ToggleLock();
            Tooltip("Unlocked, the panel gains a background you can drag and previews every icon this\n"
                    + "job tracks. The gold marker is the point that stays put. Same as /itsup.");
        }

        private static string Describe(int warnMs, int lingerMs)
        {
            var warn = warnMs > 0
                ? $"A dimmed countdown appears {Seconds(warnMs)}s before it comes back"
                : "No countdown before it comes back";

            var linger = lingerMs > 0
                ? $"the reminder clears itself {Seconds(lingerMs)}s later."
                : "the reminder stays until you press it.";

            return $"{warn}, then {linger}";
        }

        private static string Seconds(int ms) => (ms / 1000f).ToString("0.#");

        /// <summary>Timings are stored in ms, but nobody reads cooldowns in ms, so the UI is seconds.</summary>
        private static bool DrawSecondsInput(string label, ref int ms)
        {
            var seconds = ms / 1000f;
            ImGui.SetNextItemWidth(72 * ImGuiHelpers.GlobalScale);
            if (!ImGui.InputFloat(label, ref seconds, 0f, 0f, "%.1f s")) return false;

            ms = Math.Max(0, (int)MathF.Round(seconds * 1000f));
            return true;
        }

        private void DrawSidebar()
        {
            // The count is what replaces the old flat "Tracked" list: you can still see at a glance
            // which jobs are set up, without a list that grows past managing.
            if (ImGui.Selectable(SidebarLabel("Role", _roleActionIds), _view == View.Role))
                _view = View.Role;

            foreach (var job in _jobs)
            {
                var label = SidebarLabel(_jobAbbreviations[job.RowId], _jobActionIds[job.RowId]);
                if (ImGui.Selectable(label, _view == View.Job && _selectedJobId == job.RowId))
                {
                    _view = View.Job;
                    _selectedJobId = job.RowId;
                }
            }
        }

        private string SidebarLabel(string name, List<uint> ids)
        {
            var tracked = ids.Count(_config.Tracked.ContainsKey);
            return tracked > 0 ? $"{name} ({tracked})" : name;
        }

        private void DrawAbilities() =>
            DrawAbilityTable(_view == View.Job && _jobActionIds.TryGetValue(_selectedJobId, out var ids)
                ? ids
                : _roleActionIds);

        private void DrawAbilityTable(List<uint> ids)
        {
            const ImGuiTableFlags flags = ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg;
            using var table = ImRaii.Table("abilities", 4, flags);
            if (!table) return;

            var scale = ImGuiHelpers.GlobalScale;
            ImGui.TableSetupColumn("##track", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("Ability", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Heads-up", ImGuiTableColumnFlags.WidthFixed, 78 * scale);
            ImGui.TableSetupColumn("Stays up", ImGuiTableColumnFlags.WidthFixed, 78 * scale);

            // Built by hand rather than TableHeadersRow() so the two timing columns can carry tooltips.
            ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
            DrawHeader(0, "##track", null);
            DrawHeader(1, "Ability", null);
            DrawHeader(2, "Heads-up", "How long before the ability comes back the dimmed countdown icon appears.\n0 s = no countdown, it just shows up when it is ready.");
            DrawHeader(3, "Stays up", "How long the reminder stays on screen once the ability is back.\n0 s = it nags until you press it.");

            foreach (var actionId in ids)
            {
                using var id = ImRaii.PushId((int)actionId);
                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                _config.Tracked.TryGetValue(actionId, out var settings);
                var track = settings != null;
                if (ImGui.Checkbox("##track", ref track))
                {
                    if (track)
                    {
                        settings = new AbilitySettings
                        {
                            WarnMs = _config.DefaultWarnMs,
                            LingerMs = _config.DefaultLingerMs
                        };
                        _config.Tracked[actionId] = settings;
                    }
                    else
                    {
                        _config.Tracked.Remove(actionId);
                        settings = null;
                    }

                    _tracker.Sync();
                    _config.Save();
                }

                ImGui.TableNextColumn();
                DrawIcon(actionId);
                ImGui.SameLine();
                ImGui.AlignTextToFramePadding();

                ImGui.TextUnformatted(NameOf(actionId));

                ImGui.TableNextColumn();
                if (settings != null)
                {
                    var warn = settings.WarnMs;
                    if (DrawSecondsInput("##warn", ref warn)) settings.WarnMs = warn;
                    if (ImGui.IsItemDeactivatedAfterEdit()) _config.Save();
                }

                ImGui.TableNextColumn();
                if (settings != null)
                {
                    var linger = settings.LingerMs;
                    if (DrawSecondsInput("##linger", ref linger)) settings.LingerMs = linger;
                    if (ImGui.IsItemDeactivatedAfterEdit()) _config.Save();
                }
            }
        }

        private static void DrawHeader(int column, string label, string? tooltip)
        {
            ImGui.TableSetColumnIndex(column);
            ImGui.TableHeader(label);
            if (tooltip != null && ImGui.IsItemHovered()) ImGui.SetTooltip(tooltip);
        }

        private void DrawIcon(uint actionId)
        {
            var size = new Vector2(ImGui.GetFrameHeight());
            if (_actionIcons.TryGetValue(actionId, out var iconId) &&
                Services.TextureProvider.TryGetFromGameIcon(iconId, out var texture) &&
                texture.TryGetWrap(out var wrap, out _))
                ImGui.Image(wrap.Handle, size);
            else
                ImGui.Dummy(size);
        }

        private static void Tooltip(string text)
        {
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(text);
        }

        /// <summary>
        /// Secondary text that still has to be readable. Deliberately not <c>ImGui.TextDisabled</c>:
        /// the theme's disabled colour is faint enough to be illegible against the game background.
        /// </summary>
        private static void TextMuted(string text)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, ImGui.GetColorU32(ImGuiCol.Text, 0.75f)))
                ImGui.TextUnformatted(text);
        }
    }
}
