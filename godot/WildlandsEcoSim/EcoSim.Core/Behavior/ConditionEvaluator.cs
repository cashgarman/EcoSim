using System.Text.Json.Nodes;
using EcoSim.Core.Data;
using EcoSim.Core.Sim;

namespace EcoSim.Core.Behavior;

public static class ConditionEvaluator
{
  private static readonly HashSet<string> HungerActiveStates = new(StringComparer.Ordinal)
  {
    "graze", "hunt", "huntSearch",
  };

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

      case "hungerBelowOrState":
        return EvaluateHungerBelowOrState(conditionDef, ctx, c, S);

      case "hungerBelowNoPrey":
      {
        if (conditionDef.TryGetPropertyValue("diet", out var d) && S.Diet != d!.GetValue<int>()) return false;
        if (conditionDef.TryGetPropertyValue("dietMin", out var dmin) && S.Diet < dmin!.GetValue<int>()) return false;
        if (ctx.Prey != null) return false;
        return c.Hunger < BehaviorContextBuilder.Threshold(ctx, conditionDef["key"]?.GetValue<string>() ?? "hungerGraze", 55);
      }

      case "hungerBelowNoPreyOrState":
      {
        if (conditionDef.TryGetPropertyValue("dietMin", out var dmin) && S.Diet < dmin!.GetValue<int>()) return false;
        return EvaluateHungerHysteresis(conditionDef, ctx, c, requirePrey: false);
      }

      case "hungerBelowWithPrey":
      {
        if (conditionDef.TryGetPropertyValue("dietMin", out var dmin) && S.Diet < dmin!.GetValue<int>()) return false;
        if (ctx.Prey == null) return false;
        return c.Hunger < BehaviorContextBuilder.Threshold(ctx, conditionDef["key"]?.GetValue<string>() ?? "hungerGraze", 55);
      }

      case "hungerBelowWithPreyOrState":
      {
        if (conditionDef.TryGetPropertyValue("dietMin", out var dmin) && S.Diet < dmin!.GetValue<int>()) return false;
        return EvaluateHungerHysteresis(conditionDef, ctx, c, requirePrey: true);
      }

      case "energyBelow":
        return c.Energy < BehaviorContextBuilder.Threshold(ctx, conditionDef["key"]?.GetValue<string>() ?? "restEnergy", 18);

      case "energyBelowOrState":
      {
        double entry = BehaviorContextBuilder.Threshold(ctx, conditionDef["key"]?.GetValue<string>() ?? "restEnergy", 18);
        double exit = BehaviorContextBuilder.Threshold(ctx, conditionDef["exitKey"]?.GetValue<string>() ?? "energyExit", 30);
        string stateName = conditionDef["state"]?.GetValue<string>() ?? "rest";
        return c.Energy < entry || (c.State == stateName && c.Energy < exit);
      }

      case "canMate":
      {
        if (ctx.Mate == null || c.Pregnant > 0) return false;
        double hungerMin = BehaviorContextBuilder.Threshold(ctx, "mateHungerMin", 45);
        double thirstMin = BehaviorContextBuilder.Threshold(ctx, "mateThirstMin", 40);
        double energyMin = BehaviorContextBuilder.Threshold(ctx, "mateEnergyMin", 45);
        return c.Hunger > hungerMin && c.Thirst > thirstMin && c.Energy > energyMin;
      }

      case "nightWanderTired":
        return ctx.IsNight && c.Energy < BehaviorContextBuilder.Threshold(ctx, "nightWanderRestEnergy", 75);

      default:
        return false;
    }
  }

  private static bool EvaluateHungerBelowOrState(JsonObject conditionDef, BehaviorContext ctx, Creature c, SpeciesDefinition S)
  {
    if (conditionDef.TryGetPropertyValue("diet", out var d) && S.Diet != d!.GetValue<int>()) return false;
    if (conditionDef.TryGetPropertyValue("dietMax", out var dm) && S.Diet > dm!.GetValue<int>()) return false;
    if (conditionDef.TryGetPropertyValue("dietMin", out var dmin) && S.Diet < dmin!.GetValue<int>()) return false;
    if (conditionDef.TryGetPropertyValue("noPrey", out var np) && np!.GetValue<bool>() && ctx.Prey != null) return false;

    double graze = BehaviorContextBuilder.Threshold(ctx, conditionDef["grazeKey"]?.GetValue<string>() ?? "hungerGraze", 55);
    double exit = BehaviorContextBuilder.Threshold(ctx, conditionDef["exitKey"]?.GetValue<string>() ?? "hungerExit", 65);
    if (HungerActiveStates.Contains(c.State))
    {
      return c.Hunger < exit;
    }

    return c.Hunger < graze;
  }

  public static string Describe(SimState state, string conditionId, JsonObject conditionDef, BehaviorContext ctx, bool result)
  {
    var c = ctx.Creature;
    var S = ctx.Species;
    string op = conditionDef["op"]?.GetValue<string>() ?? "";

    switch (op)
    {
      case "hasThreat":
        return ctx.Threat != null
          ? $"threat id={ctx.Threat.Id} dist={ctx.Tdist:F1}"
          : "no threat";

      case "atWaterEdge":
      {
        bool atEdge = Navigation.AtWaterEdge(state, c.X, c.Y);
        return $"atWaterEdge={atEdge} pos=({c.X:F1},{c.Y:F1})";
      }

      case "thirstBelow":
      {
        string key = conditionDef["key"]?.GetValue<string>() ?? "thirstExit";
        double threshold = BehaviorContextBuilder.Threshold(ctx, key, 55);
        return $"thirst={c.Thirst:F0} {(result ? "<" : ">=")} {key}={threshold:F0}";
      }

      case "thirstBelowWhileState":
      {
        string key = conditionDef["key"]?.GetValue<string>() ?? "thirstExit";
        string stateName = conditionDef["state"]?.GetValue<string>() ?? "";
        double threshold = BehaviorContextBuilder.Threshold(ctx, key, 55);
        return $"state={c.State} thirst={c.Thirst:F0} need state={stateName} & thirst {(result ? "<" : ">=")} {threshold:F0}";
      }

      case "thirstBelowOrState":
      {
        double urgent = BehaviorContextBuilder.Threshold(ctx, conditionDef["key"]?.GetValue<string>() ?? "thirstUrgent", 30);
        double exit = BehaviorContextBuilder.Threshold(ctx, conditionDef["exitKey"]?.GetValue<string>() ?? "thirstExit", 55);
        string stateName = conditionDef["state"]?.GetValue<string>() ?? "thirst";
        return $"thirst={c.Thirst:F0} urgent={urgent:F0} exit={exit:F0} state={c.State} (need thirst<{urgent:F0} or state={stateName} & thirst<{exit:F0})";
      }

      case "hungerBelow":
      {
        double threshold = BehaviorContextBuilder.Threshold(ctx, conditionDef["key"]?.GetValue<string>() ?? "hungerGraze", 55);
        return $"hunger={c.Hunger:F0} diet={S.Diet} {(result ? "<" : ">=")} {threshold:F0}";
      }

      case "hungerBelowOrState":
        return $"hunger={c.Hunger:F0} state={c.State} diet={S.Diet}";

      case "hungerBelowNoPrey":
        return $"hunger={c.Hunger:F0} prey={(ctx.Prey != null ? ctx.Prey.Id.ToString() : "none")}";

      case "hungerBelowNoPreyOrState":
      case "hungerBelowWithPrey":
      case "hungerBelowWithPreyOrState":
        return $"hunger={c.Hunger:F0} prey={(ctx.Prey != null ? ctx.Prey.Id.ToString() : "none")} state={c.State}";

      case "energyBelow":
      {
        double threshold = BehaviorContextBuilder.Threshold(ctx, conditionDef["key"]?.GetValue<string>() ?? "restEnergy", 18);
        return $"energy={c.Energy:F0} {(result ? "<" : ">=")} {threshold:F0}";
      }

      case "energyBelowOrState":
      {
        double entry = BehaviorContextBuilder.Threshold(ctx, conditionDef["key"]?.GetValue<string>() ?? "restEnergy", 18);
        double exit = BehaviorContextBuilder.Threshold(ctx, conditionDef["exitKey"]?.GetValue<string>() ?? "energyExit", 30);
        string stateName = conditionDef["state"]?.GetValue<string>() ?? "rest";
        return $"energy={c.Energy:F0} entry={entry:F0} exit={exit:F0} state={c.State}";
      }

      case "canMate":
        return $"mate={(ctx.Mate != null ? ctx.Mate.Id.ToString() : "none")} hunger={c.Hunger:F0} thirst={c.Thirst:F0} energy={c.Energy:F0} pregnant={c.Pregnant:F1}";

      case "nightWanderTired":
      {
        double threshold = BehaviorContextBuilder.Threshold(ctx, "nightWanderRestEnergy", 75);
        return $"night={ctx.IsNight} energy={c.Energy:F0} {(result ? "<" : ">=")} {threshold:F0}";
      }

      default:
        return $"op={op} result={result}";
    }
  }

  private static bool EvaluateHungerHysteresis(JsonObject conditionDef, BehaviorContext ctx, Creature c, bool requirePrey)
  {
    double urgent = BehaviorContextBuilder.Threshold(ctx, conditionDef["key"]?.GetValue<string>() ?? "hungerUrgent", 25);
    double exit = BehaviorContextBuilder.Threshold(ctx, conditionDef["exitKey"]?.GetValue<string>() ?? "hungerExit", 65);
    double graze = BehaviorContextBuilder.Threshold(ctx, conditionDef["grazeKey"]?.GetValue<string>() ?? "hungerGraze", 55);

    if (HungerActiveStates.Contains(c.State))
    {
      return c.Hunger < exit;
    }

    if (requirePrey)
    {
      if (ctx.Prey == null) return false;
      return c.Hunger < graze;
    }

    if (ctx.Prey != null) return false;
    return c.Hunger < graze || c.Hunger < urgent;
  }
}
