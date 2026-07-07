namespace WildlandsEcoSim.UI;

/// <summary>RAII wrapper for <see cref="PerfProfiler.EnterScope"/> / <see cref="PerfProfiler.ExitScope"/>.</summary>
public readonly struct PerfScope : IDisposable
{
    private readonly bool _active;

    public PerfScope(string name)
    {
        _active = PerfProfiler.Instance.IsInstrumentationActive;
        if (_active)
        {
            PerfProfiler.Instance.EnterScope(name);
        }
    }

    public void Dispose()
    {
        if (_active)
        {
            PerfProfiler.Instance.ExitScope();
        }
    }
}
