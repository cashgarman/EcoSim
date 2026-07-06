using EcoSim.Core.Data;
using EcoSim.Core.Sim;
using Godot;

namespace WildlandsEcoSim.UI;

/// <summary>Shared population history for ecosystem + species-stats graphs (mirrors JS state.popHistory).</summary>
public sealed class PopHistoryTracker
{
    private readonly Dictionary<string, List<int>> _history = new(StringComparer.Ordinal);
    private SpeciesCatalog? _catalog;
    private double _lastSampleMs;
    private const double SampleIntervalMs = 1000.0;

    public int GraphCapacity { get; private set; } = 226;

    public IReadOnlyDictionary<string, List<int>> History => _history;

    public void Bind(SpeciesCatalog catalog)
    {
        _catalog = catalog;
        Clear();
    }

    public void Clear()
    {
        _history.Clear();
        _lastSampleMs = 0;
        if (_catalog == null) return;
        foreach (string sp in _catalog.SpeciesKeys)
        {
            _history[sp] = [];
        }
    }

    public void SyncCapacity(int width)
    {
        GraphCapacity = Math.Max(226, width);
        foreach (var list in _history.Values)
        {
            while (list.Count > GraphCapacity)
            {
                list.RemoveAt(0);
            }
        }
    }

    public bool SampleIfDue(SimSession session, bool paused)
    {
        if (_catalog == null) return false;

        if (paused)
        {
            _lastSampleMs = 0;
            return false;
        }

        double now = Time.GetTicksMsec();
        if (_lastSampleMs > 0 && now - _lastSampleMs < SampleIntervalMs)
        {
            return false;
        }

        _lastSampleMs = now;
        foreach (string sp in _catalog.SpeciesKeys)
        {
            int count = session.State.Creatures.Count(c => c.Sp == sp && !c.Dead);
            var list = _history[sp];
            list.Add(count);
            while (list.Count > GraphCapacity)
            {
                list.RemoveAt(0);
            }
        }

        return true;
    }

    public IReadOnlyList<int> GetSeries(string speciesKey)
    {
        return _history.GetValueOrDefault(speciesKey) ?? [];
    }
}
