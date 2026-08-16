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
        private const float MinIconSize = 24f;
        private const float MaxIconSize = 128f;
        private const float ResizeHandleSize = 12f;

        private const uint ColourReady = 0xFF00D7FF;   // gold, ABGR
        private const uint ColourDim = 0xA0000000;
        private const uint ColourPreview = 0xC0000000;
        private const uint ColourText = 0xFFFFFFFF;
        private const uint ColourPressedFlash = 0xFF33DD33;   // vivid green, ABGR — plays on a press only

        private const float PulseFinalWindowSeconds = 1f;
        private const float PulseFreqMinHz = 2f;
        private const float PulseFreqMaxHz = 9f;

        private const float AntSpacingPx = 8f;
        private const float AntRadius = 1.6f;
        private const float AntSpeedPxPerSec = 24f;
        private const float BreathPeriodSeconds = 1.6f;

        private const float PopDurationSeconds = 0.22f;
        private const float PopScale = 1.17f;

        private const float PressedShrinkTo = 0.7f;
        private const float PressedFadeSeconds = TrackedCooldown.PressedFadeDuration / 1000f;

        private const ImGuiWindowFlags LockedFlags =
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoMouseInputs |
            ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.AlwaysAutoResize;

        private const ImGuiWindowFlags UnlockedFlags =
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.AlwaysAutoResize |
            ImGuiWindowFlags.NoMove;

        private const uint ColourAnchor = 0xFF00D7FF;  // gold, ABGR

        private readonly CooldownTracker _cooldowns;
        private readonly Configuration _config;
        private bool _unlocked;

        private Vector2 _contentOffset;

        private BarAnchor _lastAnchor;
        private bool _anchorDirty;
        private bool _dragging;
        private bool _resizing;
        private bool _sizeDirty;

        public CooldownWindow(CooldownTracker cooldowns, Configuration config)
            : base("It's Up##itsup")
        {
            _cooldowns = cooldowns;
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
            _resizing = false;
        }

        private float IconSize => Math.Clamp(_config.IconSize, MinIconSize, MaxIconSize);

        private float IconGap => IconSize / 8f;

        private float AnchorFactor => _config.Anchor switch
        {
            BarAnchor.Centre => 0.5f,
            BarAnchor.Right => 1f,
            _ => 0f,
        };

        private int IconCount() => _cooldowns.Cooldowns.Count(cd => cd.Visible);

        private float ContentWidth(int icons) => icons <= 0 ? IconSize : icons * IconSize + (icons - 1) * IconGap;

        public override void PreDraw()
        {
            EnsureAnchorPlaced();

            var width = _unlocked ? IconSize : ContentWidth(IconCount());
            KeepBarStillWhenAnchorChanges(width);

            // anchor the window
            var origin = new Vector2(_config.AnchorX - AnchorFactor * width, _config.AnchorY);
            ImGui.SetNextWindowPos(origin - _contentOffset, ImGuiCond.Always);
        }

        private void EnsureAnchorPlaced()
        {
            if (_config.AnchorX != 0f || _config.AnchorY != 0f) return;

            var viewport = ImGuiHelpers.MainViewport;
            _config.AnchorX = viewport.Pos.X + viewport.Size.X * 0.5f;
            _config.AnchorY = viewport.Pos.Y + viewport.Size.Y * 0.72f;
            _config.Save();
        }

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
                ImGui.Dummy(new Vector2(IconSize, IconSize));
                DrawSlotPreview(drawList, origin);
                HandleResize(origin);
                HandleDrag();
                HandleDirectionCycle();
                return;
            }

            var drawn = false;

            foreach (var cd in _cooldowns.Cooldowns)
            {
                if (!cd.Visible) continue;

                if (drawn) ImGui.SameLine(0, IconGap);
                drawn = true;

                DrawEntry(drawList, cd, IconSize);
            }
        }


        private void DrawSlotPreview(ImDrawListPtr drawList, Vector2 pos)
        {
            var size = new Vector2(IconSize, IconSize);
            drawList.AddRect(pos, pos + size, ColourAnchor, 3f);

            const string label = "It's Up";
            var textSize = ImGui.CalcTextSize(label);
            drawList.AddText(new Vector2(pos.X + (IconSize - textSize.X) / 2f, pos.Y + 6f), ColourText, label);

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
                    new Vector2(left + 6f, midY + 5f),
                    new Vector2(left + 6f, midY - 5f),
                    new Vector2(left, midY),
                    ColourAnchor);

            // Resize grip: drag this corner to change icon size.
            drawList.AddTriangleFilled(
                new Vector2(pos.X + size.X, pos.Y + size.Y - ResizeHandleSize),
                new Vector2(pos.X + size.X, pos.Y + size.Y),
                new Vector2(pos.X + size.X - ResizeHandleSize, pos.Y + size.Y),
                _resizing ? ColourText : ColourAnchor);
        }


        private void HandleResize(Vector2 slotOrigin)
        {
            var handleMin = slotOrigin + new Vector2(IconSize - ResizeHandleSize, IconSize - ResizeHandleSize);
            var handleMax = slotOrigin + new Vector2(IconSize, IconSize);

            if (!_resizing && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && ImGui.IsMouseHoveringRect(handleMin, handleMax))
                _resizing = true;

            if (_resizing)
            {
                var delta = ImGui.GetIO().MouseDelta;
                if (delta != Vector2.Zero)
                {
                    _config.IconSize = Math.Clamp(_config.IconSize + (delta.X + delta.Y) / 2f, MinIconSize, MaxIconSize);
                    _sizeDirty = true;
                }
            }

            if (ImGui.IsMouseDown(ImGuiMouseButton.Left)) return;

            _resizing = false;

            // Save on release rather than every frame of the drag.
            if (!_sizeDirty) return;
            _config.Save();
            _sizeDirty = false;
        }


        private void HandleDrag()
        {
            // IsWindowHovered rather than a rectangle test, so clicking the settings window where it
            // overlaps the panel does not grab the panel underneath.
            if (!_dragging && !_resizing && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && ImGui.IsWindowHovered())
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

        private static void DrawEntry(ImDrawListPtr drawList, TrackedCooldown cd, float iconSize)
        {
            var pos = ImGui.GetCursorScreenPos();
            var size = new Vector2(iconSize, iconSize);

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

            if (cd.IsFading)
                DrawPressed(drawList, wrap, pos, size, cd);
        }


        private static uint WithAlpha(uint colour, float alpha01) =>
            (colour & 0x00FFFFFF) | ((uint)(Math.Clamp(alpha01, 0f, 1f) * 255f) << 24);

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

        private static void DrawWarmingWipe(ImDrawListPtr drawList, Vector2 pos, Vector2 size, TrackedCooldown cd)
        {
            var warnSeconds = cd.WarnMs / 1000f;
            var fraction = warnSeconds > 0f ? Math.Clamp(cd.SecondsLeft / warnSeconds, 0f, 1f) : 0f;

            var centre = pos + size / 2f;
            var radius = size.X * 0.75f;
            var start = -MathF.PI / 2f;
            var end = start + fraction * MathF.Tau;

            drawList.PushClipRect(pos, pos + size, true);
            DrawPieWedge(drawList, centre, radius, start, end, ColourDim);

            var brightness = WarmingPulseBrightness(cd.SecondsLeft);
            if (brightness > 0f)
                DrawPieWedge(drawList, centre, radius, start, end, WithAlpha(ColourText, brightness * 0.5f));

            drawList.PopClipRect();
        }

        private static float WarmingPulseBrightness(float secondsLeft)
        {
            if (secondsLeft > PulseFinalWindowSeconds || secondsLeft < 0f) return 0f;

            var urgency = 1f - secondsLeft / PulseFinalWindowSeconds;
            var freqHz = PulseFreqMinHz + (PulseFreqMaxHz - PulseFreqMinHz) * urgency;
            var wave = (MathF.Sin((float)ImGui.GetTime() * freqHz * MathF.Tau) + 1f) * 0.5f;
            return wave * urgency;
        }

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

        private static void DrawMarchingAnts(ImDrawListPtr drawList, Vector2 pos, Vector2 size, float alpha)
        {
            var perimeter = 2f * (size.X + size.Y);
            var offset = (float)(ImGui.GetTime() * AntSpeedPxPerSec) % AntSpacingPx;
            var colour = WithAlpha(ColourReady, alpha);

            for (var d = offset; d < perimeter; d += AntSpacingPx)
                drawList.AddCircleFilled(PerimeterPoint(pos, size, d), AntRadius, colour, 8);
        }

        private static void DrawReadyBorder(ImDrawListPtr drawList, Vector2 pos, Vector2 size)
        {
            var breath = (MathF.Sin((float)ImGui.GetTime() * MathF.Tau / BreathPeriodSeconds) + 1f) * 0.5f;

            drawList.AddRect(pos, pos + size, WithAlpha(ColourReady, 0.55f + 0.45f * breath), 3f,
                ImDrawFlags.None, 1.5f + breath);
            DrawMarchingAnts(drawList, pos, size, 0.7f + 0.3f * breath);
        }


        private static float PopScaleFor(DateTime? readySince)
        {
            if (readySince is not { } since) return 1f;

            var t = (float)(DateTime.UtcNow - since).TotalSeconds / PopDurationSeconds;
            if (t is < 0f or >= 1f) return 1f;

            return 1f + (PopScale - 1f) * MathF.Sin(t * MathF.PI);
        }

        private static float EaseOutCubic(float t) => 1f - MathF.Pow(1f - t, 3f);

        private static void DrawPressed(
            ImDrawListPtr drawList, IDalamudTextureWrap? wrap, Vector2 pos, Vector2 size, TrackedCooldown cd)
        {
            var t = Math.Clamp((float)(DateTime.UtcNow - cd.PressedSince!.Value).TotalSeconds / PressedFadeSeconds, 0f, 1f);
            var eased = EaseOutCubic(t);

            DrawScaledIcon(drawList, wrap, pos, size, 1f - (1f - PressedShrinkTo) * eased, 1f - eased);

            if (cd.IsPressed)
                drawList.AddRectFilled(pos, pos + size, WithAlpha(ColourPressedFlash, (1f - eased) * 0.6f), 3f);
        }
    }
}
