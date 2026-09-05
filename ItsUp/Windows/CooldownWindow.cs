using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

namespace ItsUp.Windows
{
    public class CooldownWindow : Window, IDisposable
    {
        private const float MinIconSize = 24f;
        private const float MaxIconSize = 128f;
        private const float ResizeHandleSize = 12f;

        private const uint ColourReady = 0xFF00D7FF;
        private const uint ColourDim = 0xA0000000;
        private const uint ColourPreview = 0xC0000000;
        private const uint ColourText = 0xFFFFFFFF;
        private const uint ColourPressedFlash = 0xFF33DD33;


        private const float AntSpacingPx = 8f;
        private const float AntRadius = 1.6f;
        private const float AntSpeedPxPerSec = 24f;
        private const float BreathPeriodSeconds = 1.6f;

        private const float PopDurationSeconds = 0.22f;
        private const float PopScale = 1.17f;

        private const float PressedShrinkTo = 0.7f;
        private const int PressedFadeDurationMs = 280;
        private const float PressedFadeSeconds = PressedFadeDurationMs / 1000f;

        private const float IconFontSize = 42f;
        private const uint OutlineColour = 0xFF000000;
        private const float OutlineThickness = 1.5f;

        private const ImGuiWindowFlags LockedFlags =
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoMouseInputs |
            ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.AlwaysAutoResize;

        private const ImGuiWindowFlags UnlockedFlags =
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.AlwaysAutoResize |
            ImGuiWindowFlags.NoMove;

        private const uint ColourAnchor = 0xFF00D7FF;

        private enum DisplayState
        {
            Warming,
            Ready,
            PressedFading,
            LingerFading
        }

        private enum DrawMode
        {
            Nothing,
            Anchor,
            Icons
        }

        private class DisplayEntry
        {
            public required TrackedSkill Skill { get; init; }
            public DisplayState State { get; set; }
            public DateTime StateEnteredAt { get; set; }
            public float SecondsLeft { get; set; }

            public void TransitionTo(DisplayState newState)
            {
                State = newState;
                StateEnteredAt = DateTime.UtcNow;
            }
        }

        private readonly CooldownTracker _cooldowns;
        private readonly Configuration _config;
        private readonly List<DisplayEntry> _displayList = [];
        private bool _unlocked;

        private Vector2 _contentOffset;

        private BarAnchor _lastAnchor;
        private bool _anchorDirty;
        private bool _dragging;
        private bool _resizing;
        private bool _sizeDirty;

        private DrawMode _drawMode;

        public CooldownWindow(CooldownTracker cooldowns, Configuration config)
            : base("It's Up##itsup")
        {
            _cooldowns = cooldowns;
            _cooldowns.CloseToUp += OnCloseToUp;
            _cooldowns.Up += OnUp;
            _cooldowns.Down += OnDown;
            _cooldowns.Reset += OnReset;

            _config = config;
            _lastAnchor = config.Anchor;
            Flags = LockedFlags;
            IsOpen = true;
            RespectCloseHotkey = false;
        }

        private void OnCloseToUp(TrackedSkill skill, float secondsLeft)
        {
            var entry = _displayList.Find(e => e.Skill.ActionId == skill.ActionId);
            if (entry == null)
            {
                entry = new()
                {
                    Skill = skill,
                    State = DisplayState.Warming,
                    StateEnteredAt = DateTime.UtcNow,
                    SecondsLeft = secondsLeft
                };
                _displayList.Add(entry);
            }
            else
            {
                entry.SecondsLeft = secondsLeft;
                if (entry.State != DisplayState.Ready && entry.State != DisplayState.Warming)
                {
                    entry.TransitionTo(DisplayState.Warming);
                }
            }
        }

        private void OnUp(TrackedSkill skill)
        {
            var entry = _displayList.Find(e => e.Skill.ActionId == skill.ActionId);
            if (entry == null)
            {
                entry = new()
                {
                    Skill = skill,
                    State = DisplayState.Ready,
                    StateEnteredAt = DateTime.UtcNow,
                    SecondsLeft = 0f
                };
                _displayList.Add(entry);
            }
            else
            {
                entry.SecondsLeft = 0f;
                entry.TransitionTo(DisplayState.Ready);
            }
        }

        private void OnDown(TrackedSkill skill)
        {
            var entry = _displayList.Find(e => e.Skill.ActionId == skill.ActionId);
            if (entry == null) return;

            if (entry.State == DisplayState.Ready)
            {
                // Player pressed it while it was UP
                entry.TransitionTo(DisplayState.PressedFading);
            }
            else if (entry.State == DisplayState.Warming)
            {
                // Cancelled or no longer close to up
                _displayList.Remove(entry);
            }
        }

        private void OnReset() => _displayList.Clear();

        public void Dispose()
        {
            _cooldowns.CloseToUp -= OnCloseToUp;
            _cooldowns.Up -= OnUp;
            _cooldowns.Down -= OnDown;
            _cooldowns.Reset -= OnReset;
            GC.SuppressFinalize(this);
        }

        public bool Unlocked => _unlocked;

        public void ToggleLock() => SetLock(!_unlocked);

        public void SetLock(bool unlocked)
        {
            if (unlocked && _cooldowns.IsPreview)
                _cooldowns.StopPreview();

            _unlocked = unlocked;
            Flags = _unlocked ? UnlockedFlags : LockedFlags;
            IsOpen = true;
            _dragging = false;
            _resizing = false;
        }

        private float IconSize => Math.Clamp(_config.IconSize, MinIconSize, MaxIconSize);

        private float IconGap => IconSize / 8f;

        private static float FactorFor(BarAnchor anchor) => anchor switch
        {
            BarAnchor.Centre => 0.5f,
            BarAnchor.Right => 1f,
            _ => 0f,
        };

        private int IconCount() => _displayList.Count;

        private float ContentWidth(int icons) => icons <= 0 ? IconSize : icons * IconSize + (icons - 1) * IconGap;

        public override void PreDraw()
        {
            var inCombat = Services.Condition[ConditionFlag.InCombat] || _cooldowns.IsPreview;

            _drawMode = (!inCombat && _unlocked) ? DrawMode.Anchor
                      : (inCombat && !_unlocked)  ? DrawMode.Icons
                      : DrawMode.Nothing;
            var needUnlock = _unlocked && inCombat;

            // if we are unlocked and in combat we need to lock
            if (needUnlock) SetLock(false);

            // nothing to draw
            if (_drawMode == DrawMode.Nothing) return;

            EnsureAnchorPlaced();

            var width = _unlocked ? IconSize : ContentWidth(IconCount());
            KeepBarStillWhenAnchorChanges(width);

            // anchor the window
            var origin = new Vector2(_config.AnchorX - FactorFor(_config.Anchor) * width, _config.AnchorY);
            ImGui.SetNextWindowPos(origin - _contentOffset, ImGuiCond.Always);
        }

        private void EnsureAnchorPlaced()
        {
            if (_config.AnchorX != 0f || _config.AnchorY != 0f) return;

            var (x, y) = ScreenCentre();
            _config.AnchorX = x;
            _config.AnchorY = y;
            _config.Save();
        }

        private static (float X, float Y) ScreenCentre()
        {
            var viewport = ImGuiHelpers.MainViewport;
            return (viewport.Pos.X + viewport.Size.X * 0.5f, viewport.Pos.Y + viewport.Size.Y * 0.5f);
        }

        public void ResetPanel()
        {
            _config.IconSize = Configuration.DefaultIconSize;

            var (x, y) = ScreenCentre();
            _config.AnchorX = x;
            _config.AnchorY = y;

            _config.Save();
        }

        private void KeepBarStillWhenAnchorChanges(float width)
        {
            if (_config.Anchor == _lastAnchor) return;

            _config.AnchorX += (FactorFor(_config.Anchor) - FactorFor(_lastAnchor)) * width;
            _lastAnchor = _config.Anchor;
            _config.Save();
        }

        public override void Draw()
        {
            // nothing to draw
            if (_drawMode == DrawMode.Nothing) return;

            var drawList = ImGui.GetWindowDrawList();
            var origin = ImGui.GetCursorScreenPos();
            _contentOffset = origin - ImGui.GetWindowPos();

            if (_drawMode == DrawMode.Anchor)
            {
                ImGui.Dummy(new Vector2(IconSize, IconSize));
                DrawSlotPreview(drawList, origin);
                HandleResize(origin);
                HandleDrag();
                HandleDirectionCycle();
                return;
            }

            if (_drawMode == DrawMode.Icons)
            {
                var now = DateTime.UtcNow;
                for (var i = _displayList.Count - 1; i >= 0; i--)
                {
                    var entry = _displayList[i];

                    // 1. Check Linger timeout for Ready entries
                    if (entry.State == DisplayState.Ready)
                    {
                        if (!entry.Skill.Settings.LingerForever)
                        {
                            var lingerMs = entry.Skill.Settings.LingerMs;
                            if ((now - entry.StateEnteredAt).TotalMilliseconds > lingerMs)
                            {
                                entry.TransitionTo(DisplayState.LingerFading);
                            }
                        }
                    }

                    // 2. Check Fade completion
                    if (entry.State is DisplayState.PressedFading or DisplayState.LingerFading)
                    {
                        if ((now - entry.StateEnteredAt).TotalMilliseconds > PressedFadeDurationMs)
                        {
                            _displayList.RemoveAt(i);
                            continue;
                        }
                    }
                }

                var drawn = false;
                foreach (var entry in _displayList)
                {
                    if (drawn) ImGui.SameLine(0, IconGap);
                    drawn = true;

                    DrawEntry(drawList, entry, IconSize);
                }
            }
        }

        private void DrawSlotPreview(ImDrawListPtr drawList, Vector2 pos)
        {
            var size = new Vector2(IconSize, IconSize);
            drawList.AddRect(pos, pos + size, ColourAnchor, 3f);

            var label = Strings.SlotPreviewLabel;
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
            var gripPos = slotOrigin + new Vector2(IconSize - ResizeHandleSize, IconSize - ResizeHandleSize);
            var mousePos = ImGui.GetIO().MousePos;
            var mouseInGrip = mousePos.X >= gripPos.X && mousePos.X <= slotOrigin.X + IconSize
                           && mousePos.Y >= gripPos.Y && mousePos.Y <= slotOrigin.Y + IconSize;

            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left) && mouseInGrip)
                _resizing = true;

            if (_resizing)
            {
                var delta = ImGui.GetIO().MouseDelta;
                var rawNewSize = _config.IconSize + (delta.X + delta.Y) * 0.5f;
                var clamped = Math.Clamp(rawNewSize, MinIconSize, MaxIconSize);

                if (Math.Abs(clamped - _config.IconSize) > 0.01f)
                {
                    _config.IconSize = clamped;
                    _sizeDirty = true;
                }
            }

            if (ImGui.IsMouseDown(ImGuiMouseButton.Left)) return;

            _resizing = false;

            if (!_sizeDirty) return;
            _config.Save();
            _sizeDirty = false;
        }

        private void HandleDrag()
        {
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

        private static void DrawEntry(ImDrawListPtr drawList, DisplayEntry entry, float iconSize)
        {
            var pos = ImGui.GetCursorScreenPos();
            var size = new Vector2(iconSize, iconSize);
            var scale = iconSize / Configuration.DefaultIconSize;

            IDalamudTextureWrap? wrap = null;
            if (Services.TextureProvider.TryGetFromGameIcon(entry.Skill.IconId, out var texture))
                texture.TryGetWrap(out wrap, out _);

            ImGui.Dummy(size);

            if (entry.State == DisplayState.Ready)
            {
                var popScale = PopScaleFor(entry.StateEnteredAt);
                DrawScaledIcon(drawList, wrap, pos, size, popScale, 1f);
                DrawReadyBorder(drawList, pos, size, scale, popScale);
                return;
            }

            if (entry.State == DisplayState.Warming)
            {
                DrawScaledIcon(drawList, wrap, pos, size, 1f, 1f);
                DrawWarmingWipe(drawList, pos, size, entry.Skill.WarnMs, entry.SecondsLeft);
                var label = Math.Ceiling(entry.SecondsLeft).ToString("0");
                var useFont = Plugin.NumberFont?.Available == true;
                if (useFont) Plugin.NumberFont!.Push();

                var font = ImGui.GetFont();
                var fontSize = IconFontSize * scale;
                var textSize = ImGui.CalcTextSizeA(font, fontSize, float.MaxValue, -1f, label, out _);
                var textPos = pos + (size - textSize) / 2f;

                var outlineSize = OutlineThickness * scale;
                drawList.AddText(font, fontSize, textPos + new Vector2(-outlineSize, 0), OutlineColour, label);
                drawList.AddText(font, fontSize, textPos + new Vector2(outlineSize, 0), OutlineColour, label);
                drawList.AddText(font, fontSize, textPos + new Vector2(0, -outlineSize), OutlineColour, label);
                drawList.AddText(font, fontSize, textPos + new Vector2(0, outlineSize), OutlineColour, label);

                drawList.AddText(font, fontSize, textPos, ColourText, label);

                if (useFont) Plugin.NumberFont!.Pop();
                return;
            }

            if (entry.State is DisplayState.PressedFading or DisplayState.LingerFading)
                DrawPressed(drawList, wrap, pos, size, entry.State, entry.StateEnteredAt);
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

        private static void DrawWarmingWipe(ImDrawListPtr drawList, Vector2 pos, Vector2 size, int warnMs, float secondsLeft)
        {
            var warnSeconds = warnMs / 1000f;
            var fraction = warnSeconds > 0f ? Math.Clamp(secondsLeft / warnSeconds, 0f, 1f) : 0f;

            var centre = pos + size / 2f;
            var radius = size.X * 0.75f;
            var start = -MathF.PI / 2f;
            var end = start + fraction * MathF.Tau;

            drawList.PushClipRect(pos, pos + size, true);
            DrawPieWedge(drawList, centre, radius, start, end, ColourDim);

            drawList.PopClipRect();
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

        private static void DrawMarchingAnts(ImDrawListPtr drawList, Vector2 pos, Vector2 size, float alpha, float scale, float popScale)
        {
            var perimeter = 2f * (size.X + size.Y);
            var offset = (float)(ImGui.GetTime() * AntSpeedPxPerSec) % AntSpacingPx;
            var colour = WithAlpha(ColourReady, alpha);

            var center = pos + size / 2f;
            var antRadius = AntRadius * scale;

            for (var d = offset; d < perimeter; d += AntSpacingPx)
            {
                var p = PerimeterPoint(pos, size, d);
                var scaledP = center + (p - center) * popScale;
                drawList.AddCircleFilled(scaledP, antRadius, colour, 8);
            }
        }

        private static void DrawReadyBorder(ImDrawListPtr drawList, Vector2 pos, Vector2 size, float scale, float popScale)
        {
            var breath = (MathF.Sin((float)ImGui.GetTime() / BreathPeriodSeconds * MathF.Tau) + 1f) * 0.5f;
            var alpha = 0.5f + 0.5f * breath;

            DrawMarchingAnts(drawList, pos, size, alpha, scale, popScale);
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
            ImDrawListPtr drawList, IDalamudTextureWrap? wrap, Vector2 pos, Vector2 size, DisplayState state, DateTime stateEnteredAt)
        {
            var t = Math.Clamp((float)(DateTime.UtcNow - stateEnteredAt).TotalSeconds / PressedFadeSeconds, 0f, 1f);
            var eased = EaseOutCubic(t);

            DrawScaledIcon(drawList, wrap, pos, size, 1f - (1f - PressedShrinkTo) * eased, 1f - eased);

            if (state == DisplayState.PressedFading)
                drawList.AddRectFilled(pos, pos + size, WithAlpha(ColourPressedFlash, (1f - eased) * 0.6f), 3f);
        }
    }
}
