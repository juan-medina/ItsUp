using System;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace ItsUp
{
    public readonly record struct AbilityAvailability(bool IsUp, float SecondsRemaining);

    public static class AbilityEvaluator
    {
        public static unsafe AbilityAvailability Evaluate(
            ActionManager* am,
            byte playerLevel,
            uint actionId,
            uint parentActionId)
        {
            // 1. Follow-up / Proc abilities
            if (parentActionId != 0)
            {
                var isUp = am->GetAdjustedActionId(parentActionId) == actionId;
                return new AbilityAvailability(isUp, 0f);
            }

            // 2. Charge abilities
            var maxCharges = ActionManager.GetMaxCharges(actionId, playerLevel);
            if (maxCharges > 0)
            {
                var hasCharge = am->GetCurrentCharges(actionId) >= 1;
                var total = am->GetRecastTime(ActionType.Action, actionId);
                var elapsed = am->GetRecastTimeElapsed(ActionType.Action, actionId);
                return new AbilityAvailability(hasCharge, Math.Max(0f, total - elapsed));
            }

            // 3. Standard cooldown abilities
            var isReady = !am->IsRecastTimerActive(ActionType.Action, actionId);
            var recastTotal = am->GetRecastTime(ActionType.Action, actionId);
            var recastElapsed = am->GetRecastTimeElapsed(ActionType.Action, actionId);
            return new AbilityAvailability(isReady, Math.Max(0f, recastTotal - recastElapsed));
        }
    }
}
