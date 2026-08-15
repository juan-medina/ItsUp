using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

namespace ItsUp.Windows
{
    public class CooldownWindow : Window
    {
        private const float IconSize = 48f;
        private const float IconGap = 6f;

        private const uint ColourReady = 0xFF00D7FF;   // gold, ABGR
        private const uint ColourDim = 0xA0000000;
        private const uint ColourPreview = 0xC0000000;
        private const uint ColourText = 0xFFFFFFFF;

        private const ImGuiWindowFlags LockedFlags =
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoMouseInputs |
            ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.AlwaysAutoResize;

        // NoMove in move mode too: ImGui's own dragging pins the top-left corner, which is exactly
        // what the anchor exists to avoid. Dragging is handled here and moves the anchor instead.
        // NoTitleBar as well — the slot itself is labelled, so a title bar above it is redundant.
        private const ImGuiWindowFlags UnlockedFlags =
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.AlwaysAutoResize |
            ImGuiWindowFlags.NoMove;

        private const uint ColourAnchor = 0xFF00D7FF;  // gold, ABGR

        private readonly CooldownTracker _tracker;
        private readonly Configuration _config;
        private bool _unlocked;

        /// <summary>Window origin to content origin. Measured while drawing so no style lookup is needed.</summary>
        private Vector2 _contentOffset;

        private BarAnchor _lastAnchor;
        private bool _anchorDirty;
        private bool _dragging;

        public CooldownWindow(CooldownTracker tracker, Configuration config)
            : base("It's Up##itsup")
        {
            _tracker = tracker;
            _config = config;
            _lastAnchor = config.Anchor;
            Flags = LockedFlags;
            IsOpen = true;
            RespectCloseHotkey = false;
        }

        public bool Unlocked => _unlocked;

        /// <summary>Move mode: show a single draggable slot in place of the live bar.</summary>
        public void ToggleLock() => SetLock(!_unlocked);

        public void SetLock(bool unlocked)
        {
            _unlocked = unlocked;
            Flags = _unlocked ? UnlockedFlags : LockedFlags;
            IsOpen = true;
            _dragging = false;
        }

        private float AnchorFactor => _config.Anchor switch
        {
            BarAnchor.Centre => 0.5f,
            BarAnchor.Right => 1f,
            _ => 0f,
        };

        private int IconCount() => _tracker.Tracked.Count(cd => cd.Visible);

        private static float ContentWidth(int icons) =>
            icons <= 0 ? IconSize : icons * IconSize + (icons - 1) * IconGap;

        public override void PreDraw()
        {
            EnsureAnchorPlaced();

            // Move mode positions a single icon-sized slot rather than the live bar, so the anchor
            // point sits on that slot's pinned edge instead of the full (and usually wider) bar.
            var width = _unlocked ? IconSize : ContentWidth(IconCount());
            KeepBarStillWhenAnchorChanges(width);

            // Every frame, in both modes. Leaving ImGui to hold the position even briefly means the
            // top-left corner wins the moment the icon count changes.
            var origin = new Vector2(_config.AnchorX - AnchorFactor * width, _config.AnchorY);
            ImGui.SetNextWindowPos(origin - _contentOffset, ImGuiCond.Always);
        }

        /// <summary>An unset anchor would pin the bar to the top-left corner, so seed it mid-screen.</summary>
        private void EnsureAnchorPlaced()
        {
            if (_config.AnchorX != 0f || _config.AnchorY != 0f) return;

            var viewport = ImGuiHelpers.MainViewport;
            _config.AnchorX = viewport.Pos.X + viewport.Size.X * 0.5f;
            _config.AnchorY = viewport.Pos.Y + viewport.Size.Y * 0.72f;
            _config.Save();
        }

        /// <summary>
        /// Switching Left/Centre/Right re-pins a different edge. Shift the anchor by the difference
        /// so the bar stays where it looks, instead of jumping the moment the setting changes.
        /// </summary>
        private void KeepBarStillWhenAnchorChanges(float width)
        {
            if (_config.Anchor == _lastAnchor) return;

            var previous = _lastAnchor switch
            {
                BarAnchor.Centre => 0.5f,
                BarAnchor.Right => 1f,
                _ => 0f,
            };

            _config.AnchorX += (AnchorFactor - previous) * width;
            _lastAnchor = _config.Anchor;
            _config.Save();
        }

        public override void Draw()
        {
            var drawList = ImGui.GetWindowDrawList();
            var origin = ImGui.GetCursorScreenPos();
            _contentOffset = origin - ImGui.GetWindowPos();

            if (_unlocked)
            {
                // Move mode positions one slot, not a preview of the whole bar — dragging it moves
                // the anchor directly, and the arrow(s) show which way the real bar grows from here.
                ImGui.Dummy(new Vector2(IconSize, IconSize));
                DrawSlotPreview(drawList, origin);
                HandleDrag();
                HandleDirectionCycle();
                return;
            }

            var drawn = false;

            foreach (var cd in _tracker.Tracked)
            {
                if (!cd.Visible) continue;

                if (drawn) ImGui.SameLine(0, IconGap);
                drawn = true;

                DrawEntry(drawList, cd);
            }
        }

        /// <summary>
        /// A single icon-sized slot with a border, the plugin's name, and an arrow for every
        /// direction the bar grows in from this point (both ways for Centre) — what gets dragged
        /// around in move mode, now that it stands in for the (removed) window title.
        /// </summary>
        private void DrawSlotPreview(ImDrawListPtr drawList, Vector2 pos)
        {
            var size = new Vector2(IconSize, IconSize);
            drawList.AddRect(pos, pos + size, ColourAnchor, 3f);

            const string label = "It's Up";
            var textSize = ImGui.CalcTextSize(label);
            drawList.AddText(new Vector2(pos.X + (IconSize - textSize.X) / 2f, pos.Y + 6f), ColourText, label);

            // Pushed down below the label rather than through the slot's vertical centre.
            var midY = pos.Y + IconSize - 14f;
            var left = pos.X + 6f;
            var right = pos.X + IconSize - 6f;

            drawList.AddLine(new Vector2(left, midY), new Vector2(right, midY), ColourAnchor, 2f);

            // Left anchor pins the left edge, so the bar grows rightward from here (and vice versa).
            if (_config.Anchor is BarAnchor.Left or BarAnchor.Centre)
                drawList.AddTriangleFilled(
                    new Vector2(right - 6f, midY - 5f),
                    new Vector2(right - 6f, midY + 5f),
                    new Vector2(right, midY),
                    ColourAnchor);

            if (_config.Anchor is BarAnchor.Right or BarAnchor.Centre)
                drawList.AddTriangleFilled(
                    new Vector2(left + 6f, midY - 5f),
                    new Vector2(left + 6f, midY + 5f),
                    new Vector2(left, midY),
                    ColourAnchor);
        }

        /// <summary>
        /// Dragging moves the anchor, not the window — the window is always derived from the anchor.
        /// The grab is latched on mouse-down inside the panel so a fast drag that outruns the cursor
        /// does not drop it.
        /// </summary>
        private void HandleDrag()
        {
            // IsWindowHovered rather than a rectangle test, so clicking the settings window where it
            // overlaps the panel does not grab the panel underneath.
            if (!_dragging && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && ImGui.IsWindowHovered())
                _dragging = true;

            if (_dragging)
            {
                var delta = ImGui.GetIO().MouseDelta;
                if (delta != Vector2.Zero)
                {
                    _config.AnchorX += delta.X;
                    _config.AnchorY += delta.Y;
                    _anchorDirty = true;
                }
            }

            if (ImGui.IsMouseDown(ImGuiMouseButton.Left)) return;

            _dragging = false;

            // Save on release rather than every frame of the drag.
            if (!_anchorDirty) return;
            _config.Save();
            _anchorDirty = false;
        }

        /// <summary>
        /// Right-click cycles Left → Centre → Right → Left. Setting <see cref="Configuration.Anchor"/>
        /// here is enough — <see cref="KeepBarStillWhenAnchorChanges"/> picks it up next frame and
        /// shifts the anchor so the slot stays put and gets saved, same as the config window's combo.
        /// </summary>
        private void HandleDirectionCycle()
        {
            if (!ImGui.IsWindowHovered() || !ImGui.IsMouseClicked(ImGuiMouseButton.Right)) return;

            _config.Anchor = _config.Anchor switch
            {
                BarAnchor.Left => BarAnchor.Centre,
                BarAnchor.Centre => BarAnchor.Right,
                _ => BarAnchor.Left,
            };
        }

        private static void DrawEntry(ImDrawListPtr drawList, TrackedCooldown cd)
        {
            var pos = ImGui.GetCursorScreenPos();
            var size = new Vector2(IconSize, IconSize);

            if (Services.TextureProvider.TryGetFromGameIcon(cd.IconId, out var texture) &&
                texture.TryGetWrap(out var wrap, out _))
                ImGui.Image(wrap.Handle, size);
            else
                ImGui.Dummy(size);

            if (cd.IsReady)
            {
                drawList.AddRect(pos, pos + size, ColourReady, 3f);
                return;
            }

            if (cd.IsWarming)
            {
                drawList.AddRectFilled(pos, pos + size, ColourDim);
                var label = Math.Ceiling(cd.SecondsLeft).ToString("0");
                var textSize = ImGui.CalcTextSize(label);
                drawList.AddText(pos + (size - textSize) / 2f, ColourText, label);
                return;
            }

            drawList.AddRectFilled(pos, pos + size, ColourPreview);
        }
    }
}
