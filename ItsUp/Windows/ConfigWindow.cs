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
        private enum View { Role, Job }

        private readonly Configuration _config;
        private readonly CooldownTracker _tracker;
        private readonly CooldownWindow _panel;

        public record ActionItem(uint ActionId, uint ParentActionId = 0)
        {
            public bool IsFollowup => ParentActionId != 0;
        }

        private readonly FrozenDictionary<uint, string> _actionNames;
        private readonly FrozenDictionary<uint, uint> _actionIcons;
        private readonly FrozenDictionary<uint, string> _jobAbbreviations;
        private readonly FrozenDictionary<uint, List<ActionItem>> _jobActions;
        private readonly List<ActionItem> _roleActions;
        private readonly List<ClassJob> _jobs;

        private const int ActionType = 4;
        private const int IsleSprint = 29581;

        private View _view = View.Role;
        private uint _selectedJobId;

        public ConfigWindow(Configuration config, CooldownTracker tracker, CooldownWindow panel)
            : base("It's Up — Settings##its#up#config", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
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

            // built once here rather in every frame.
            var allActions = Services.DataManager.GetExcelSheet<Action>()!.ToList();
            _actionNames = allActions.ToFrozenDictionary(a => a.RowId, a => a.Name.ToString());
            _actionIcons = allActions.ToFrozenDictionary(a => a.RowId, a => (uint)a.Icon);

            _jobs = [.. Services.DataManager.GetExcelSheet<ClassJob>()!
                .Where(j => j.Role > 0 && j.ItemSoulCrystal.Value.RowId > 0)
                .OrderBy(j => j.Name.ToString(), StringComparer.Ordinal)];

            _jobAbbreviations = _jobs.ToFrozenDictionary(j => j.RowId, j => j.Abbreviation.ToString());

            // Build followup relationships from Lumina ActionIndirection
            var followupsByBase = new Dictionary<uint, List<uint>>();
            var isFollowup = new HashSet<uint>();

            void AddFollowup(uint baseId, uint childId)
            {
                if (baseId == 0 || childId == 0 || baseId == childId) return;
                if (!followupsByBase.TryGetValue(baseId, out var list))
                {
                    list = [];
                    followupsByBase[baseId] = list;
                }
                if (!list.Contains(childId))
                {
                    list.Add(childId);
                    isFollowup.Add(childId);
                }
            }

            var replaceSheet = Services.DataManager.GetSubrowExcelSheet<ReplaceAction>();
            if (replaceSheet != null)
            {
                foreach (var subrowCollection in replaceSheet)
                {
                    foreach (var row in subrowCollection)
                    {
                        var baseId = row.Action.RowId;
                        if (baseId == 0) continue;
                        foreach (var targetRef in row.ReplaceActions)
                        {
                            var targetId = targetRef.RowId;
                            if (targetId == 0 || targetId == baseId) continue;
                            AddFollowup(baseId, targetId);
                            Services.Logger.Information($"[ReplaceAction] Followup: {NameOf(baseId)} ({baseId}) -> {NameOf(targetId)} ({targetId})");
                        }
                    }
                }
            }

            var byJob = new Dictionary<uint, List<ActionItem>>();
            foreach (var job in _jobs)
            {
                // Get non-pvp player actions for that job with a cd bigger than 10s
                var jobActionIds = allActions
                    .Where(a => !a.IsPvP
                                && (a.ClassJob.RowId == job.RowId || a.ClassJob.RowId == job.ClassJobParent.RowId)
                                && a.IsPlayerAction
                                && (a.ActionCategory.RowId == ActionType || a.Recast100ms > 100))
                    .Select(a => a.RowId);

                // remove island sprint and exclude follow-ups from the root list
                var rootIds = jobActionIds.Where(id => id != IsleSprint && !isFollowup.Contains(id)).ToList();
                rootIds.Sort(CompareByName);

                var items = new List<ActionItem>();
                foreach (var rootId in rootIds)
                {
                    items.Add(new ActionItem(rootId, 0));
                    if (followupsByBase.TryGetValue(rootId, out var children))
                    {
                        foreach (var childId in children)
                            items.Add(new ActionItem(childId, rootId));
                    }
                }

                byJob[job.RowId] = items;
            }
            _jobActions = byJob.ToFrozenDictionary();

            var roleIds = allActions
                .Where(a => a.IsRoleAction && a.ClassJobLevel != 0)
                .Select(a => a.RowId)
                .ToList();
            roleIds.Sort(CompareByName);
            _roleActions = [.. roleIds.Select(id => new ActionItem(id, 0))];
        }

        private int CompareByName(uint lhs, uint rhs) =>
            string.Compare(NameOf(lhs), NameOf(rhs), StringComparison.Ordinal);

        private string NameOf(uint actionId) =>
            _actionNames.TryGetValue(actionId, out var name) && name.Length > 0 ? name : $"#{actionId}";

        private void SelectCurrentJob()
        {
            var jobId = Services.ObjectTable.LocalPlayer?.ClassJob.RowId ?? 0;
            if (jobId == 0 || !_jobActions.ContainsKey(jobId)) return;

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
            if (DrawSecondsInput("##default#warn", ref warn)) _config.DefaultWarnMs = warn;
            if (ImGui.IsItemDeactivatedAfterEdit()) _config.Save();

            ImGui.SameLine();
            ImGui.TextUnformatted("visible");
            ImGui.SameLine();
            var linger = _config.DefaultLingerMs;
            var lingerForever = _config.DefaultLingerForever;
            if (DrawLingerInput("default#linger", ref linger, ref lingerForever, _config.DefaultLingerMs, out var commitLinger))
            {
                _config.DefaultLingerMs = linger;
                _config.DefaultLingerForever = lingerForever;
            }
            if (commitLinger) _config.Save();

            TextMuted(Describe(_config.DefaultWarnMs, _config.DefaultLingerMs, _config.DefaultLingerForever));

            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Panel anchor");
            ImGui.SameLine();

            var anchor = (int)_config.Anchor;
            ImGui.SetNextItemWidth(110 * ImGuiHelpers.GlobalScale);
            if (ImGui.Combo("##anchor", ref anchor, "Left\0Centre\0Right\0"))
            {
                _config.Anchor = (BarAnchor)anchor;
                _config.Save();
            }
            Tooltip("The panel stays pinned as icons flows from the anchor.");

            ImGui.SameLine();
            if (ImGui.Button(_panel.Unlocked ? "Lock panel" : "Move panel"))
                _panel.ToggleLock();
            Tooltip(_panel.Unlocked ? "Unlocked, click to locked" : "Locked, click to unlock and move and drag the panel" + ".Same as /itsup.");

            ImGui.SameLine();
            if (ImGui.Button("Reset panel"))
                _panel.ResetPanel();
            Tooltip("Restores the default icon size and position.");
        }

        private static string Describe(int warnMs, int lingerMs, bool lingerForever)
        {
            var warn = warnMs > 0
                ? $"A dimmed countdown appears {Seconds(warnMs)}s before it comes back"
                : "No countdown before it comes back";

            var linger = lingerForever
                ? "the reminder stays until you press it."
                : $"the reminder clears itself {Seconds(lingerMs)}s later.";

            return $"{warn}, then {linger}";
        }

        private static string Seconds(int ms) => (ms / 1000f).ToString("0.#");

        private static bool DrawSecondsInput(string label, ref int ms)
        {
            var seconds = ms / 1000f;
            ImGui.SetNextItemWidth(72 * ImGuiHelpers.GlobalScale);
            if (!ImGui.InputFloat(label, ref seconds, 0f, 0f, "%.1f s")) return false;

            ms = Math.Max(0, (int)MathF.Round(seconds * 1000f));
            return true;
        }

        private static bool DrawLingerInput(string id, ref int ms, ref bool forever, int fallbackMs, out bool commit)
        {
            commit = false;
            var changed = false;

            var mode = forever ? 1 : 0;
            ImGui.SetNextItemWidth(120 * ImGuiHelpers.GlobalScale);
            if (ImGui.Combo($"##{id}mode", ref mode, "for\0until pressed\0"))
            {
                forever = mode == 1;
                if (!forever && ms <= 0) ms = fallbackMs;
                changed = true;
                commit = true;
            }
            Tooltip("\"Until pressed\" keeps it visible until you press it again.");

            if (!forever)
            {
                ImGui.SameLine();
                if (DrawSecondsInput($"##{id}", ref ms)) changed = true;
                if (ImGui.IsItemDeactivatedAfterEdit()) commit = true;
            }

            return changed;
        }

        private void DrawSidebar()
        {
            if (ImGui.Selectable(SidebarLabel("Role", _roleActions), _view == View.Role))
                _view = View.Role;

            foreach (var job in _jobs)
            {
                var label = SidebarLabel(_jobAbbreviations[job.RowId], _jobActions[job.RowId]);
                if (ImGui.Selectable(label, _view == View.Job && _selectedJobId == job.RowId))
                {
                    _view = View.Job;
                    _selectedJobId = job.RowId;
                }
            }
        }

        private string SidebarLabel(string name, List<ActionItem> actions)
        {
            var tracked = actions.Count(a => _config.Tracked.ContainsKey(a.ActionId));
            return tracked > 0 ? $"{name} ({tracked})" : name;
        }

        private void DrawAbilities() =>
            DrawAbilityTable(_view == View.Job && _jobActions.TryGetValue(_selectedJobId, out var actions)
                ? actions
                : _roleActions);

        private void DrawAbilityTable(List<ActionItem> actions)
        {
            const ImGuiTableFlags flags = ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg;
            using var table = ImRaii.Table("abilities", 4, flags);
            if (!table) return;

            var scale = ImGuiHelpers.GlobalScale;
            ImGui.TableSetupColumn("##track", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("Ability", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Heads-up", ImGuiTableColumnFlags.WidthFixed, 78 * scale);
            ImGui.TableSetupColumn("Visible", ImGuiTableColumnFlags.WidthFixed, 220 * scale);

            ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
            DrawHeader(0, "##track", null);
            DrawHeader(1, "Ability", null);
            DrawHeader(2, "Heads-up", "How long before the ability will show.");
            DrawHeader(3, "Visible", "How long the reminder stays on screen.");

            foreach (var root in actions.Where(a => !a.IsFollowup))
            {
                DrawAbilityRow(root.ActionId, isFollowup: false);

                foreach (var followup in actions.Where(a => a.ParentActionId == root.ActionId))
                {
                    DrawAbilityRow(followup.ActionId, isFollowup: true);
                }
            }
        }

        private void DrawAbilityRow(uint actionId, bool isFollowup)
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
                        WarnMs = isFollowup ? 0 : _config.DefaultWarnMs,
                        LingerMs = _config.DefaultLingerForever ? 0 : _config.DefaultLingerMs,
                        LingerForever = _config.DefaultLingerForever
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
            var scale = ImGuiHelpers.GlobalScale;
            if (isFollowup)
            {
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 16 * scale);
                TextMuted("↳ ");
                ImGui.SameLine();
            }
            DrawIcon(actionId);
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();

            ImGui.TextUnformatted(NameOf(actionId));

            ImGui.TableNextColumn();
            if (isFollowup)
            {
                ImGui.AlignTextToFramePadding();
                TextMuted("(?)");
                Tooltip("Follow-up actions become available when their trigger ability is used,\nso a countdown before return does not apply.");
            }
            else if (settings != null)
            {
                var warn = settings.WarnMs;
                if (DrawSecondsInput("##warn", ref warn)) settings.WarnMs = warn;
                if (ImGui.IsItemDeactivatedAfterEdit()) _config.Save();
            }

            ImGui.TableNextColumn();
            if (settings != null)
            {
                var linger = settings.LingerMs;
                var lingerForever = settings.LingerForever;
                if (DrawLingerInput("linger", ref linger, ref lingerForever, _config.DefaultLingerMs, out var commit))
                {
                    settings.LingerMs = linger;
                    settings.LingerForever = lingerForever;
                }
                if (commit) _config.Save();
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

        private static void TextMuted(string text)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, ImGui.GetColorU32(ImGuiCol.Text, 0.75f)))
                ImGui.TextUnformatted(text);
        }
    }
}
