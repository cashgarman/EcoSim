namespace EcoSim.Core.Sim;

public sealed class LifeStoryEvent
{
    public int Seq { get; set; }
    public double T { get; set; }
    public int Day { get; set; }
    public double Age { get; set; }
    public string Kind { get; set; } = "";
    public string? Decision { get; set; }
    public string? Detail { get; set; }
}

public sealed class LifeStoryData
{
    public List<LifeStoryEvent> Events { get; } = [];
    public string? CommittedState { get; set; }
    public double CommittedSince { get; set; }
    public int NextSeq { get; set; } = 1;
}

public sealed class LifeStory
{
    public const int MaxEvents = 300;
    public const double DecisionDebounceSec = 2.5;

    public LifeStoryData Ensure(Creature c)
    {
        c.LifeStory ??= new LifeStoryData();
        return c.LifeStory;
    }

    public LifeStoryEvent? Record(Creature c, SimState state, string kind, string? decision = null, string? detail = null)
    {
        if (state.BatchMode) return null;
        var story = Ensure(c);
        var ev = new LifeStoryEvent
        {
            Seq = story.NextSeq++,
            T = state.TGlobal,
            Day = state.Day,
            Age = c.Age,
            Kind = kind,
            Decision = decision,
            Detail = detail,
        };
        story.Events.Add(ev);
        while (story.Events.Count > MaxEvents)
        {
            story.Events.RemoveAt(0);
        }
        return ev;
    }

    public void ObserveDecision(Creature c, SimState state, string newState)
    {
        var story = Ensure(c);
        if (story.CommittedState == newState) return;
        double now = state.TGlobal;
        if (story.CommittedState != null && now - story.CommittedSince < DecisionDebounceSec) return;
        if (story.CommittedState != null)
        {
            Record(c, state, "stateExit", story.CommittedState);
        }
        story.CommittedState = newState;
        story.CommittedSince = now;
        Record(c, state, "decision", newState);
        Record(c, state, "stateEnter", newState);
    }
}
