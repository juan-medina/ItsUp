using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace ItsUp.Windows
{
    public class ConfigWindow : Window
    {
        private readonly Configuration _config;
        private readonly CooldownTracker _tracker;
        private readonly CooldownWindow _panel;
        private readonly JobActionRegistry _registry;
        private readonly HotbarKeybindResolver _keybindResolver;

        private uint _selectedJobId;
        private ConfigTab? _requestedTab;

        public ConfigWindow(Configuration config, CooldownTracker tracker, CooldownWindow panel, JobActionRegistry registry, HotbarKeybindResolver keybindResolver)
            : base("It's Up — Config##its#up#config", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
        {
            _config = config;
            _tracker = tracker;
            _panel = panel;
            _registry = registry;
            _keybindResolver = keybindResolver;

            Size = new Vector2(640, 480);
            SizeCondition = ImGuiCond.FirstUseEver;
            SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(520, 360),
                MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
            };
        }

        private void SelectCurrentJob()
        {
            var jobId = Services.ObjectTable.LocalPlayer?.ClassJob.RowId ?? 0;
            if (jobId == 0 || !_registry.JobActions.ContainsKey(jobId))
                jobId = _registry.Jobs.Count > 0 ? _registry.Jobs[0].RowId : 0;

            _selectedJobId = jobId;
        }

        public override void OnOpen()
        {
            SelectCurrentJob();
            _requestedTab = _config.LastOpenTab;
        }

        public override void OnClose()
        {
            _tracker.StopPreview();
            _config.Save();
        }

        public override void Draw()
        {
            using var tabBar = ImRaii.TabBar("ConfigTabBar");
            if (!tabBar) return;

            DrawTabItem(ConfigTab.Welcome, Strings.Config.TabWelcome, DrawWelcomeTab);
            DrawTabItem(ConfigTab.JobActions, Strings.Config.TabJobActions, DrawJobActionsTab);
            DrawTabItem(ConfigTab.Settings, Strings.Config.TabSettings, DrawSettingsTab);
        }

        private void DrawTabItem(ConfigTab tab, string label, Action drawContent)
        {
            var flags = ImGuiTabItemFlags.None;
            if (_requestedTab == tab)
            {
                flags |= ImGuiTabItemFlags.SetSelected;
                _requestedTab = null;
            }

            using var tabItem = ImRaii.TabItem(label, flags);
            if (!tabItem) return;

            if (_config.LastOpenTab != tab)
            {
                _config.LastOpenTab = tab;
                _config.Save();
            }

            drawContent();
        }

        private void DrawWelcomeTab()
        {
            using var child = ImRaii.Child("welcome_scroll", new Vector2(0, 0), false);
            if (!child) return;

            ImGui.Spacing();

            ImGui.TextUnformatted(Strings.Config.WelcomeTitle);
            ImGui.SameLine();
            TextMuted($"— {Strings.Config.WelcomeTagline}");

            ImGui.Spacing();

            var buttonSize = new Vector2(180 * ImGuiHelpers.GlobalScale, 28 * ImGuiHelpers.GlobalScale);
            if (ImGui.Button(Strings.Config.WelcomeConfigureButton, buttonSize))
            {
                _requestedTab = ConfigTab.JobActions;
            }

            ImGui.SameLine();
            var preview = _tracker.IsPreview;
            if (ImGui.Checkbox(Strings.Config.Preview, ref preview))
            {
                if (preview)
                {
                    _panel.SetLock(false);
                    _tracker.TogglePreview();
                }
                else
                {
                    _tracker.StopPreview();
                }
            }
            Tooltip(Strings.Config.PreviewTooltip);

            ImGui.SameLine();
            var unlocked = _panel.Unlocked;
            if (ImGui.Checkbox(Strings.Config.Unlock, ref unlocked))
            {
                if (unlocked)
                {
                    _tracker.StopPreview();
                    _panel.SetLock(true);
                }
                else
                {
                    _panel.SetLock(false);
                }
            }
            Tooltip(Strings.Config.UnlockTooltip);

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.TextWrapped(Strings.Config.WelcomeDescription);
        }

        private void DrawJobActionsTab()
        {
            var sidebarWidth = 170 * ImGuiHelpers.GlobalScale;
            using (ImRaii.Child("sidebar", new Vector2(sidebarWidth, 0), true))
                DrawSidebar();

            ImGui.SameLine();

            using (ImRaii.Child("abilities", new Vector2(0, 0), true))
                DrawAbilities();
        }

        private void DrawSettingsTab()
        {
            using var child = ImRaii.Child("settings_scroll", new Vector2(0, 0), false);
            if (!child) return;

            ImGui.Spacing();
            ImGui.TextUnformatted(Strings.Config.SectionBar);
            ImGui.Spacing();

            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(Strings.Config.GrowthDirectionLabel);
            ImGui.SameLine();

            var anchor = (int)_config.Anchor;
            ImGui.SetNextItemWidth(120 * ImGuiHelpers.GlobalScale);
            if (ImGui.Combo("##anchor", ref anchor, Strings.Config.GrowthDirectionItems))
            {
                _config.Anchor = (BarAnchor)anchor;
                _config.Save();
            }
            Tooltip(Strings.Config.GrowthDirectionTooltip);

            ImGui.SameLine();
            ImGui.Spacing();
            ImGui.SameLine();
            ImGui.TextUnformatted(Strings.Config.IconSizeLabel);
            ImGui.SameLine();

            var iconSize = (int)MathF.Round(_config.IconSize);
            ImGui.SetNextItemWidth(110 * ImGuiHelpers.GlobalScale);
            if (ImGui.SliderInt("##icon#size", ref iconSize, (int)Configuration.MinIconSize, (int)Configuration.MaxIconSize, Strings.Config.IconSizeFormat))
            {
                _config.IconSize = iconSize;
            }
            if (ImGui.IsItemDeactivatedAfterEdit()) _config.Save();
            Tooltip(Strings.Config.IconSizeTooltip);

            ImGui.Spacing();

            var unlocked = _panel.Unlocked;
            if (ImGui.Checkbox(Strings.Config.Unlock, ref unlocked))
            {
                if (unlocked)
                {
                    _tracker.StopPreview();
                    _panel.SetLock(true);
                }
                else
                {
                    _panel.SetLock(false);
                }
            }
            Tooltip(Strings.Config.UnlockTooltip);

            ImGui.SameLine();
            var preview = _tracker.IsPreview;
            if (ImGui.Checkbox(Strings.Config.Preview, ref preview))
            {
                if (preview)
                {
                    _panel.SetLock(false);
                    _tracker.TogglePreview();
                }
                else
                {
                    _tracker.StopPreview();
                }
            }
            Tooltip(Strings.Config.PreviewTooltip);

            ImGui.SameLine();
            if (ImGui.Button(Strings.Config.Reset))
                _panel.ResetPanel();
            Tooltip(Strings.Config.ResetTooltip);

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.TextUnformatted(Strings.Config.SectionDisplay);
            ImGui.Spacing();

            var showAnts = _config.ShowAnts;
            if (ImGui.Checkbox(Strings.Config.ShowAnts, ref showAnts))
            {
                _config.ShowAnts = showAnts;
                _config.Save();
            }
            Tooltip(Strings.Config.ShowAntsTooltip);

            var onlyInDuties = _config.OnlyInDuties;
            if (ImGui.Checkbox(Strings.Config.OnlyInDuties, ref onlyInDuties))
            {
                _config.OnlyInDuties = onlyInDuties;
                _config.Save();
            }
            Tooltip(Strings.Config.OnlyInDutiesTooltip);

            var showKeybinds = _config.ShowKeybinds;
            if (ImGui.Checkbox(Strings.Config.ShowKeybinds, ref showKeybinds))
            {
                _config.ShowKeybinds = showKeybinds;
                _config.Save();
            }
            Tooltip(Strings.Config.ShowKeybindsTooltip);

            if (_config.ShowKeybinds)
            {
                ImGui.SameLine();
                if (ImGui.Button(Strings.Config.RefreshKeybinds))
                    _keybindResolver.Refresh();
                Tooltip(Strings.Config.RefreshKeybindsTooltip);
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.TextUnformatted(Strings.Config.SectionDefaults);
            ImGui.Spacing();

            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(Strings.Config.DefaultsPrefix);
            ImGui.SameLine();

            var warn = _config.DefaultWarnMs;
            if (DrawSecondsInput("##default#warn", ref warn)) _config.DefaultWarnMs = warn;
            if (ImGui.IsItemDeactivatedAfterEdit()) _config.Save();

            ImGui.SameLine();
            ImGui.TextUnformatted(Strings.Config.DefaultsMiddle);
            ImGui.SameLine();
            var linger = _config.DefaultLingerMs;
            var lingerForever = _config.DefaultLingerForever;
            if (DrawLingerInput("default#linger", ref linger, ref lingerForever, _config.DefaultLingerMs, out var commitLinger))
            {
                _config.DefaultLingerMs = linger;
                _config.DefaultLingerForever = lingerForever;
            }
            if (commitLinger) _config.Save();

            ImGui.Spacing();
            TextMuted(Describe(_config.DefaultWarnMs, _config.DefaultLingerMs, _config.DefaultLingerForever));
            ImGui.Spacing();
            TextMuted(Strings.Config.DefaultsHint);
        }



        private static string Describe(int warnMs, int lingerMs, bool lingerForever)
        {
            var warn = warnMs > 0
                ? string.Format(Strings.Describe.WarnTemplate, Seconds(warnMs))
                : Strings.Describe.WarnNone;

            var linger = lingerForever
                ? Strings.Describe.LingerForever
                : string.Format(Strings.Describe.LingerTemplate, Seconds(lingerMs));

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
            if (ImGui.Combo($"##{id}mode", ref mode, Strings.Table.LingerDropdownItems))
            {
                forever = mode == 1;
                if (!forever && ms <= 0) ms = fallbackMs;
                changed = true;
                commit = true;
            }
            Tooltip(Strings.Table.LingerDropdownTooltip);

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
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(Strings.Config.SidebarHeader);
            ImGui.Separator();

            foreach (var job in _registry.Jobs)
            {
                var name = _registry.JobNames.TryGetValue(job.RowId, out var n) ? n : job.Name.ToString();
                var label = SidebarLabel(name, job.RowId);
                if (ImGui.Selectable(label, _selectedJobId == job.RowId))
                {
                    _selectedJobId = job.RowId;
                }
            }
        }

        private string SidebarLabel(string name, uint jobId)
        {
            var tracked = _config.TrackedByJob.TryGetValue(jobId, out var dict) ? dict.Count : 0;
            return tracked > 0 ? $"{name} ({tracked})" : name;
        }

        private void DrawAbilities()
        {
            if (_selectedJobId == 0 || !_registry.JobActions.TryGetValue(_selectedJobId, out var actions))
                return;

            DrawJobHeader(_selectedJobId);
            ImGui.Separator();
            DrawAbilityTable(actions);
        }

        private void DrawJobHeader(uint jobId)
        {
            var jobName = _registry.GetJobDisplayName(jobId);
            var trackedCount = _config.TrackedByJob.TryGetValue(jobId, out var dict) ? dict.Count : 0;
            var countText = trackedCount switch
            {
                0 => Strings.Config.TrackedCountNone,
                1 => Strings.Config.TrackedCountSingle,
                _ => string.Format(Strings.Config.TrackedCountMultiple, trackedCount)
            };

            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(jobName);
            ImGui.SameLine();
            TextMuted($"({countText})");
        }

        private void DrawAbilityTable(List<ActionItem> actions)
        {
            const ImGuiTableFlags flags = ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg;
            using var table = ImRaii.Table("abilities", 4, flags);
            if (!table) return;

            var scale = ImGuiHelpers.GlobalScale;
            ImGui.TableSetupColumn("##track", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn(Strings.Table.ColumnAbility, ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn(Strings.Table.ColumnHeadsUp, ImGuiTableColumnFlags.WidthFixed, 78 * scale);
            ImGui.TableSetupColumn(Strings.Table.ColumnVisible, ImGuiTableColumnFlags.WidthFixed, 220 * scale);

            ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
            DrawHeader(0, "##track", null);
            DrawHeader(1, Strings.Table.ColumnAbility, null);
            DrawHeader(2, Strings.Table.ColumnHeadsUp, Strings.Table.HeadsUpTooltip);
            DrawHeader(3, Strings.Table.ColumnVisible, Strings.Table.VisibleTooltip);

            var jobLevel = _registry.GetPlayerJobLevel(_selectedJobId);
            foreach (var item in actions)
                DrawAbilityRow(item.ActionId, item.ParentActionId, jobLevel);
        }

        private void DrawAbilityRow(uint actionId, uint parentActionId, byte jobLevel)
        {
            var isFollowup = parentActionId != 0;
            using var id = ImRaii.PushId((int)actionId);
            ImGui.TableNextRow();

            var settings = DrawTrackCell(actionId, parentActionId);
            DrawNameCell(actionId, isFollowup, jobLevel);
            DrawHeadsUpCell(settings, isFollowup);
            DrawVisibleCell(settings);
        }

        private AbilitySettings? DrawTrackCell(uint actionId, uint parentActionId)
        {
            ImGui.TableNextColumn();
            var jobTracked = _config.GetTrackedForJob(_selectedJobId);
            jobTracked.TryGetValue(actionId, out var settings);
            var track = settings != null;
            if (!ImGui.Checkbox("##track", ref track)) return settings;

            if (track)
            {
                var isFollowup = parentActionId != 0;
                settings = new AbilitySettings
                {
                    ParentActionId = parentActionId,
                    WarnMs = isFollowup ? 0 : _config.DefaultWarnMs,
                    LingerMs = _config.DefaultLingerForever ? 0 : _config.DefaultLingerMs,
                    LingerForever = _config.DefaultLingerForever
                };
                jobTracked[actionId] = settings;
            }
            else
            {
                jobTracked.Remove(actionId);
                settings = null;
            }

            _tracker.Sync();
            _config.Save();
            return settings;
        }

        private void DrawNameCell(uint actionId, bool isFollowup, byte jobLevel)
        {
            ImGui.TableNextColumn();
            if (isFollowup)
            {
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 16 * ImGuiHelpers.GlobalScale);
                TextMuted("↳ ");
                ImGui.SameLine();
            }

            var displayActionId = isFollowup ? actionId : _registry.GetTraitUpgradedActionId(actionId, jobLevel);
            DrawIcon(displayActionId);
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(_registry.NameOf(displayActionId));
        }

        private void DrawHeadsUpCell(AbilitySettings? settings, bool isFollowup)
        {
            ImGui.TableNextColumn();
            if (isFollowup)
            {
                ImGui.AlignTextToFramePadding();
                TextMuted("(?)");
                Tooltip(Strings.Table.FollowUpTooltip);
            }
            else if (settings != null)
            {
                var warn = settings.WarnMs;
                if (DrawSecondsInput("##warn", ref warn)) settings.WarnMs = warn;
                if (ImGui.IsItemDeactivatedAfterEdit()) _config.Save();
            }
        }

        private void DrawVisibleCell(AbilitySettings? settings)
        {
            ImGui.TableNextColumn();
            if (settings == null) return;

            var linger = settings.LingerMs;
            var lingerForever = settings.LingerForever;
            if (DrawLingerInput("linger", ref linger, ref lingerForever, _config.DefaultLingerMs, out var commit))
            {
                settings.LingerMs = linger;
                settings.LingerForever = lingerForever;
            }
            if (commit) _config.Save();
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
            if (_registry.ActionInfo.TryGetValue(actionId, out var info) &&
                Services.TextureProvider.TryGetFromGameIcon(info.Icon, out var texture) &&
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
