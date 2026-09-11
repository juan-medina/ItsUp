using System;
using System.Collections.Generic;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;

namespace ItsUp
{
    public class HotbarKeybindResolver : IDisposable
    {
        private static readonly string[] _actionBarNames =
        [
            "_ActionBar",
            "_ActionBar01",
            "_ActionBar02",
            "_ActionBar03",
            "_ActionBar04",
            "_ActionBar05",
            "_ActionBar06",
            "_ActionBar07",
            "_ActionBar08",
            "_ActionBar09"
        ];

        private readonly object _lock = new();
        private Dictionary<uint, string> _keybinds = [];

        public HotbarKeybindResolver()
        {
            Services.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, _actionBarNames, OnActionBarEvent);
            Services.AddonLifecycle.RegisterListener(AddonEvent.PostRefresh, _actionBarNames, OnActionBarEvent);
            Services.ClientState.ClassJobChanged += OnClassJobChanged;
            Services.ClientState.Login += OnLogin;

            Refresh();
        }

        private void OnActionBarEvent(AddonEvent type, AddonArgs args) => Refresh();

        private void OnClassJobChanged(uint classJobId) => Refresh();

        private void OnLogin() => Refresh();

        public unsafe void Refresh()
        {
            var hotbarModule = RaptureHotbarModule.Instance();
            if (hotbarModule == null) return;

            var am = ActionManager.Instance();
            var newKeybinds = new Dictionary<uint, string>();

            // 10 standard hotbars (0..9) with 16 slots each (0..15)
            for (uint bar = 0; bar < 10; bar++)
            {
                for (uint slot = 0; slot < 16; slot++)
                {
                    var slotPtr = hotbarModule->GetSlotById(bar, slot);
                    if (slotPtr == null || slotPtr->IsEmpty) continue;
                    if (slotPtr->CommandType != RaptureHotbarModule.HotbarSlotType.Action) continue;

                    var hint = slotPtr->KeybindHintString?.Trim();
                    if (string.IsNullOrEmpty(hint)) continue;

                    var actionId = slotPtr->CommandId;
                    if (actionId != 0)
                    {
                        newKeybinds.TryAdd(actionId, hint);

                        if (am != null)
                        {
                            var adjusted = am->GetAdjustedActionId(actionId);
                            if (adjusted != 0 && adjusted != actionId)
                                newKeybinds.TryAdd(adjusted, hint);
                        }
                    }
                }
            }

            lock (_lock)
            {
                _keybinds = newKeybinds;
            }

            Services.Logger.Debug($"Refreshed hotbar keybind hints ({newKeybinds.Count} mapped)");
        }

        public string? GetKeybind(uint actionId, uint parentActionId = 0)
        {
            lock (_lock)
            {
                if (_keybinds.TryGetValue(actionId, out var hint))
                    return hint;

                if (parentActionId != 0 && _keybinds.TryGetValue(parentActionId, out var parentHint))
                    return parentHint;

                return null;
            }
        }

        public void Dispose()
        {
            Services.ClientState.Login -= OnLogin;
            Services.ClientState.ClassJobChanged -= OnClassJobChanged;
            Services.AddonLifecycle.UnregisterListener(OnActionBarEvent);
            GC.SuppressFinalize(this);
        }
    }
}
