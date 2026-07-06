using System.Text.Json.Nodes;
using EcoSim.Core.Sim;

namespace EcoSim.Core.Behavior;

public static class ConditionEvaluator
{
    public static bool Evaluate(SimState state, string conditionId, JsonObject conditionDef, BehaviorContext ctx)
    {
        var c = ctx.Creature;
        var S = ctx.Species;
        string op = conditionDef["op"]?.GetValue<string>() ?? "";

        switch (op)
        {
            case "hasThreat":
                return ctx.Threat != null;

            case "atWaterEdge":
                return Navigation.AtWaterEdge(state, c.X, c.Y);

            case "thirstBelow":
            {
                string key = conditionDef["key"]?.GetValue<string>() ?? "thirstExit";
                return c.Thirst < BehaviorContextBuilder.Threshold(ctx, key, 55);
            }

            case "thirstBelowWhileState":
            {
                string key = conditionDef["key"]?.GetValue<string>() ?? "thirstExit";
                string stateName = conditionDef["state"]?.GetValue<string>() ?? "";
                return c.State == stateName && c.Thirst < BehaviorContextBuilder.Threshold(ctx, key, 55);
            }

            case "thirstBelowOrState":
            {
                double urgent = BehaviorContextBuilder.Threshold(ctx, conditionDef["key"]?.GetValue<string>() ?? "thirstUrgent", 30);
                double exit = BehaviorContextBuilder.Threshold(ctx, conditionDef["exitKey"]?.GetValue<string>() ?? "thirstExit", 55);
                string stateName = conditionDef["state"]?.GetValue<string>() ?? "thirst";
                return c.Thirst < urgent || (c.State == stateName && c.Thirst < exit);
            }

            case "hungerBelow":
            {
                if (conditionDef.TryGetPropertyValue("dietMax", out var dm) && S.Diet > dm!.GetValue<int>()) return false;
                return c.Hunger < BehaviorContextBuilder.Threshold(ctx, conditionDef["key"]?.GetValue<string>() ?? "hungerGraze", 55);
            }

            case "hungerBelowNoPrey":
            {
                if (conditionDef.TryGetPropertyValue("diet", out var d) && S.Diet != d!.GetValue<int>()) return false;
                if (conditionDef.TryGetPropertyValue("dietMin", out var dmin) && S.Diet < dmin!.GetValue<int>()) return false;
                if (ctx.Prey != null) return false;
                return c.Hunger < BehaviorContextBuilder.Threshold(ctx, conditionDef["key"]?.GetValue<string>() ?? "hungerGraze", 55);
            }

            case "hungerBelowWithPrey":
            {
                if (conditionDef.TryGetPropertyValue("dietMin", out var dmin) && S.Diet < dmin!.GetValue<int>()) return false;
                if (ctx.Prey == null) return false;
                return c.Hunger < BehaviorContextBuilder.Threshold(ctx, conditionDef["key"]?.GetValue<string>() ?? "hungerGraze", 55);
            }

            case "energyBelow":
                return c.Energy < BehaviorContextBuilder.Threshold(ctx, conditionDef["key"]?.GetValue<string>() ?? "restEnergy", 18);

            case "canMate":
            {
                if (ctx.Mate == null || c.Pregnant > 0) return false;
                double hungerMin = BehaviorContextBuilder.Threshold(ctx, "mateHungerMin", 45);
                double thirstMin = BehaviorContextBuilder.Threshold(ctx, "mateThirstMin", 40);
                return c.Hunger > hungerMin && c.Thirst > thirstMin;
            }

            case "nightWanderTired":
                return ctx.IsNight && c.Energy < BehaviorContextBuilder.Threshold(ctx, "nightWanderRestEnergy", 75);

            default:
                return false;
        }
    }
}
