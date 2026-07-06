namespace EcoSim.Core.Sim;

public static class PerfPolicy
{
    private static readonly HashSet<string> MilestoneKinds = new(StringComparer.Ordinal)
    {
        "appeared", "born", "mated", "gaveBirth", "hunted", "preyedOn", "died", "stage",
    };

    public static double EffectiveSnapshotIntervalSec(double snapshotIntervalSec) =>
        Math.Max(0.5, snapshotIntervalSec);

    public static double EffectiveHeartbeatIntervalSec(double heartbeatIntervalSec, double speed)
    {
        double baseSec = Math.Max(1, heartbeatIntervalSec);
        speed = Math.Max(1, speed);
        if (speed < 4) return baseSec;
        return baseSec * Math.Max(1, speed / 3);
    }

    public static bool ShouldRunBehaviorThisSubstep(int substep, int substepCount, double speed)
    {
        speed = Math.Max(1, speed);
        if (speed < 5) return true;
        if (substepCount <= 0) return true;
        return substep >= substepCount - 1;
    }

    public static bool ShouldPersistCreatureEvent(string kind, int? creatureId, int? selectedId, bool selectedDead)
    {
        return true;
    }

    public static bool ShouldPersistCreatureEventAtSpeed(string kind, double speed, int? creatureId, int? selectedId, bool selectedDead)
    {
        speed = Math.Max(1, speed);
        if (speed < 5) return true;
        if (MilestoneKinds.Contains(kind)) return true;
        if (creatureId != null && selectedId == creatureId && !selectedDead) return true;
        return false;
    }

    public static string TimelineWritePressure(double speed)
    {
        speed = Math.Max(1, speed);
        if (speed >= 8) return "high";
        if (speed >= 5) return "medium";
        return "low";
    }

    public static double EffectiveScrubTickRefreshMs() => 800;
}
