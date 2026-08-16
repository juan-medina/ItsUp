using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures.TextureWraps;
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
        private const uint ColourConsumeFlash = 0xFF33DD33;   // vivid green, ABGR — plays on a press only

        // Warming: the dim overlay is a shrinking pie wedge (a cooldown wipe) rather than a flat rect,
        // and the final second gets a brightness pulse that speeds up as it nears zero.
        private const float PulseFinalWindowSeconds = 1f;
        private const float PulseFreqMinHz = 2f;
        private const float PulseFreqMaxHz = 9f;

        // Ready: marching ants walked around the perimeter plus a slow breathing pulse on the border,
        // so the reminder reads as "alive" in peripheral vision instead of a static outline.
        private const float AntSpacingPx = 8f;
        private const float AntRadius = 1.6f;
        private const float AntSpeedPxPerSec = 24f;
        private const float BreathPeriodSeconds = 1.6f;

        // Ready: a one-shot scale "pop" plays at the instant the rising edge fires.
        private const float PopDurationSeconds = 0.22f;
        private const float PopScale = 1.17f;

        // Consumed: shrink + fade always play; the green flash is press-only (see TrackedCooldown.ConsumedByPress).
        private const float ConsumeShrinkTo = 0.7f;
        private static readonly float ConsumeFadeSeconds = TrackedCooldown.ConsumeFadeMs / 1000f;

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

            // Vertex order deliberately mirrors the other triangle's winding (base-bottom before
            // base-top here) rather than mirroring its coordinates — same winding direction keeps
            // Dear ImGui's AA fringe on the outside of both triangles equally, so they render as
            // true mirror images instead of one being a hair softer/larger than the other.
            if (_config.Anchor is BarAnchor.Right or BarAnchor.Centre)
                drawList.AddTriangleFilled(
                    new Vector2(left + 6f, midY + 5f),
                    new Vector2(left + 6f, midY - 5f),
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

        /// <summary>
        /// Reserves a fixed <see cref="IconSize"/> footprint via <c>Dummy</c> and draws everything else
        /// manually into the draw list, same pattern as <see cref="DrawSlotPreview"/> — so the pop and
        /// consume animations can scale the icon without ever disturbing <c>SameLine</c> spacing.
        /// </summary>
        private static void DrawEntry(ImDrawListPtr drawList, TrackedCooldown cd)
        {
            var pos = ImGui.GetCursorScreenPos();
            var size = new Vector2(IconSize, IconSize);

            IDalamudTextureWrap? wrap = null;
            if (Services.TextureProvider.TryGetFromGameIcon(cd.IconId, out var texture))
                texture.TryGetWrap(out wrap, out _);

            ImGui.Dummy(size);

            if (cd.IsReady)
            {
                DrawScaledIcon(drawList, wrap, pos, size, PopScaleFor(cd.ReadySince), 1f);
                DrawReadyBorder(drawList, pos, size);
                return;
            }

            if (cd.IsWarming)
            {
                DrawScaledIcon(drawList, wrap, pos, size, 1f, 1f);
                DrawWarmingWipe(drawList, pos, size, cd);
                var label = Math.Ceiling(cd.SecondsLeft).ToString("0");
                var textSize = ImGui.CalcTextSize(label);
                drawList.AddText(pos + (size - textSize) / 2f, ColourText, label);
                return;
            }

            if (cd.IsConsuming)
                DrawConsumed(drawList, wrap, pos, size, cd);
        }

        /// <summary>Replaces the colour's alpha byte, clamped to [0, 1].</summary>
        private static uint WithAlpha(uint colour, float alpha01) =>
            (colour & 0x00FFFFFF) | ((uint)(Math.Clamp(alpha01, 0f, 1f) * 255f) << 24);

        /// <summary>
        /// Draws the icon (or the same dark-rect fallback the bar always used when a texture wasn't
        /// ready) centred inside <paramref name="size"/> at <paramref name="scale"/>, so scaling never
        /// changes the reserved layout footprint.
        /// </summary>
        private static void DrawScaledIcon(
            ImDrawListPtr drawList, IDalamudTextureWrap? wrap, Vector2 pos, Vector2 size, float scale, float alpha)
        {
            var scaledSize = size * scale;
            var scaledPos = pos + (size - scaledSize) / 2f;

            if (wrap != null)
                drawList.AddImage(wrap.Handle, scaledPos, scaledPos + scaledSize, Vector2.Zero, Vector2.One,
                    WithAlpha(0xFFFFFFFF, alpha));
            else
                drawList.AddRectFilled(scaledPos, scaledPos + scaledSize, WithAlpha(ColourPreview, alpha));
        }

        private static void DrawPieWedge(
            ImDrawListPtr drawList, Vector2 centre, float radius, float startAngle, float endAngle, uint colour)
        {
            if (endAngle <= startAngle) return;

            drawList.PathLineTo(centre);
            drawList.PathArcTo(centre, radius, startAngle, endAngle, 32);
            drawList.PathFillConvex(colour);
        }

        /// <summary>
        /// An OmniCC-style cooldown wipe standing in for the old flat dim overlay. The icon is only ever
        /// on screen for the <see cref="TrackedCooldown.WarnMs"/> window, so the wedge sweeps full-circle
        /// down to nothing across exactly that span rather than needing the total recast time.
        /// </summary>
        private static void DrawWarmingWipe(ImDrawListPtr drawList, Vector2 pos, Vector2 size, TrackedCooldown cd)
        {
            var warnSeconds = cd.WarnMs / 1000f;
            var fraction = warnSeconds > 0f ? Math.Clamp(cd.SecondsLeft / warnSeconds, 0f, 1f) : 0f;

            var centre = pos + size / 2f;
            var radius = size.X * 0.75f;              // overshoots the inscribed circle so the wedge covers the square's corners once clipped
            var start = -MathF.PI / 2f;                // 12 o'clock
            var end = start + fraction * MathF.Tau;

            drawList.PushClipRect(pos, pos + size, true);
            DrawPieWedge(drawList, centre, radius, start, end, ColourDim);

            var brightness = WarmingPulseBrightness(cd.SecondsLeft);
            if (brightness > 0f)
                DrawPieWedge(drawList, centre, radius, start, end, WithAlpha(ColourText, brightness * 0.5f));

            drawList.PopClipRect();
        }

        /// <summary>
        /// 0 outside the final second. Inside it, the pulse both speeds up and brightens as it nears
        /// zero — the "about to pop" cue asked for, kept to the last second so it marks the approach
        /// rather than pulsing through the whole warm-up.
        /// </summary>
        private static float WarmingPulseBrightness(float secondsLeft)
        {
            if (secondsLeft > PulseFinalWindowSeconds || secondsLeft < 0f) return 0f;

            var urgency = 1f - secondsLeft / PulseFinalWindowSeconds;
            var freqHz = PulseFreqMinHz + (PulseFreqMaxHz - PulseFreqMinHz) * urgency;
            var wave = (MathF.Sin((float)ImGui.GetTime() * freqHz * MathF.Tau) + 1f) * 0.5f;
            return wave * urgency;
        }

        /// <summary>Walks the rectangle's perimeter clockwise from the top-left corner, <paramref name="d"/> px in.</summary>
        private static Vector2 PerimeterPoint(Vector2 pos, Vector2 size, float d)
        {
            var perimeter = 2f * (size.X + size.Y);
            d = ((d % perimeter) + perimeter) % perimeter;

            if (d < size.X) return new Vector2(pos.X + d, pos.Y);
            d -= size.X;
            if (d < size.Y) return new Vector2(pos.X + size.X, pos.Y + d);
            d -= size.Y;
            if (d < size.X) return new Vector2(pos.X + size.X - d, pos.Y + size.Y);
            d -= size.X;
            return new Vector2(pos.X, pos.Y + size.Y - d);
        }

        /// <summary>Small dots marching around the border on a continuous loop — the "still up, come on" cue.</summary>
        private static void DrawMarchingAnts(ImDrawListPtr drawList, Vector2 pos, Vector2 size, float alpha)
        {
            var perimeter = 2f * (size.X + size.Y);
            var offset = (float)(ImGui.GetTime() * AntSpeedPxPerSec) % AntSpacingPx;
            var colour = WithAlpha(ColourReady, alpha);

            for (var d = offset; d < perimeter; d += AntSpacingPx)
                drawList.AddCircleFilled(PerimeterPoint(pos, size, d), AntRadius, colour, 8);
        }

        /// <summary>Breathing gold border plus marching ants, both riding the same slow pulse.</summary>
        private static void DrawReadyBorder(ImDrawListPtr drawList, Vector2 pos, Vector2 size)
        {
            var breath = (MathF.Sin((float)ImGui.GetTime() * MathF.Tau / BreathPeriodSeconds) + 1f) * 0.5f;

            drawList.AddRect(pos, pos + size, WithAlpha(ColourReady, 0.55f + 0.45f * breath), 3f,
                ImDrawFlags.None, 1.5f + breath);
            DrawMarchingAnts(drawList, pos, size, 0.7f + 0.3f * breath);
        }

        /// <summary>
        /// One sine bump driven by wall-clock time since the rising edge fired — grows then eases back to
        /// 1, marking the moment it became ready rather than pulsing for as long as it stays ready.
        /// </summary>
        private static float PopScaleFor(DateTime? readySince)
        {
            if (readySince is not { } since) return 1f;

            var t = (float)(DateTime.UtcNow - since).TotalSeconds / PopDurationSeconds;
            if (t is < 0f or >= 1f) return 1f;

            return 1f + (PopScale - 1f) * MathF.Sin(t * MathF.PI);
        }

        private static float EaseOutCubic(float t) => 1f - MathF.Pow(1f - t, 3f);

        /// <summary>
        /// Plays for <see cref="TrackedCooldown.ConsumeFadeMs"/> after the reminder clears: a shrink+fade
        /// every time, plus a green "got it" flash only when it cleared because you pressed it — a
        /// linger timeout fades out plainly so ignoring it never looks the same as using it.
        /// </summary>
        private static void DrawConsumed(
            ImDrawListPtr drawList, IDalamudTextureWrap? wrap, Vector2 pos, Vector2 size, TrackedCooldown cd)
        {
            var t = Math.Clamp((float)(DateTime.UtcNow - cd.ConsumedSince!.Value).TotalSeconds / ConsumeFadeSeconds, 0f, 1f);
            var eased = EaseOutCubic(t);

            DrawScaledIcon(drawList, wrap, pos, size, 1f - (1f - ConsumeShrinkTo) * eased, 1f - eased);

            if (cd.ConsumedByPress)
                drawList.AddRectFilled(pos, pos + size, WithAlpha(ColourConsumeFlash, (1f - eased) * 0.6f), 3f);
        }
    }
}
