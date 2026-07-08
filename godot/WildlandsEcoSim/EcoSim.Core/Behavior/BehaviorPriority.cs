using System.Text.Json.Nodes;
using EcoSim.Core.Sim;

namespace EcoSim.Core.Behavior;

public static class BehaviorPriority
{
  public const double SameTierDwellSec = 0.3;

  private static readonly Dictionary<string, int> DefaultTierByState = new(StringComparer.Ordinal)
  {
    ["flee"] = 0,
    ["thirst"] = 1,
    ["graze"] = 1,
    ["hunt"] = 1,
    ["rest"] = 1,
    ["huntSearch"] = 2,
    ["mate"] = 2,
    ["wander"] = 3,
  };

  public static int GetTier(JsonObject? action)
  {
    if (action == null) return 3;
    if (action.TryGetPropertyValue("interruptTier", out var tierNode) && tierNode != null)
    {
      return tierNode.GetValue<int>();
    }

    string state = action["state"]?.GetValue<string>() ?? "";
    return DefaultTierByState.GetValueOrDefault(state, 3);
  }

  public static double GetMinCommitSec(JsonObject? action)
  {
    if (action != null &&
        action.TryGetPropertyValue("minCommitSec", out var secNode) &&
        secNode != null)
    {
      return secNode.GetValue<double>();
    }

    int tier = GetTier(action);
    return tier >= 2 ? 0.5 : 0;
  }

  public static bool IsUrgentNeed(Creature creature, BehaviorContext ctx)
  {
    double hungerUrgent = BehaviorContextBuilder.Threshold(ctx, "hungerUrgent", 25);
    double thirstUrgent = BehaviorContextBuilder.Threshold(ctx, "thirstUrgent", 30);
    double energyUrgent = BehaviorContextBuilder.Threshold(ctx, "energyUrgent", 12);
    return creature.Hunger < hungerUrgent ||
           creature.Thirst < thirstUrgent ||
           creature.Energy < energyUrgent;
  }
}
