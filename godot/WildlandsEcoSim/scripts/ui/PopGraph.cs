using EcoSim.Core.Data;
using EcoSim.Core.Sim;
using Godot;

namespace WildlandsEcoSim.UI;

public partial class PopGraph : Control
{
    private readonly Dictionary<string, List<int>> _history = new(StringComparer.Ordinal);
    private SpeciesCatalog? _catalog;
    private string? _highlightSpecies;
    private const int MaxSamples = 120;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(0, 70);
    }

    public void Bind(SpeciesCatalog catalog)
    {
        _catalog = catalog;
        _history.Clear();
        foreach (string sp in catalog.SpeciesKeys)
        {
            _history[sp] = [];
        }
    }

    public void SetHighlight(string? speciesKey)
    {
        _highlightSpecies = speciesKey;
        QueueRedraw();
    }

    public void Sample(SimSession session)
    {
        if (_catalog == null) return;
        foreach (string sp in _catalog.SpeciesKeys)
        {
            int count = session.State.Creatures.Count(c => c.Sp == sp && !c.Dead);
            var list = _history[sp];
            list.Add(count);
            if (list.Count > MaxSamples) list.RemoveAt(0);
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_catalog == null) return;

        var rect = GetRect();
        DrawStyleBox(EcoSimThemeBuilder.MakeFlat(EcoSimThemeBuilder.PanelDarker, EcoSimThemeBuilder.Edge), rect);

        float maxVal = 1;
        foreach (var list in _history.Values)
        {
            foreach (int v in list) maxVal = Math.Max(maxVal, v);
        }

        int speciesCount = _catalog.SpeciesKeys.Count;
        float xStep = rect.Size.X / Math.Max(1, MaxSamples - 1);
        int colorIdx = 0;
        Color[] palette =
        [
            new Color(0.35f, 0.72f, 0.9f),
            new Color(0.9f, 0.55f, 0.3f),
            new Color(0.5f, 0.85f, 0.45f),
            new Color(0.85f, 0.4f, 0.4f),
            new Color(0.75f, 0.6f, 0.9f),
        ];

        foreach (string sp in _catalog.SpeciesKeys)
        {
            var list = _history[sp];
            if (list.Count < 2) continue;
            Color col = palette[colorIdx % palette.Length];
            if (sp == _highlightSpecies) col = EcoSimThemeBuilder.Gold;
            colorIdx++;

            for (int i = 1; i < list.Count; i++)
            {
                float x0 = rect.Position.X + (i - 1) * xStep;
                float y0 = rect.Position.Y + rect.Size.Y - (list[i - 1] / maxVal) * (rect.Size.Y - 4) - 2;
                float x1 = rect.Position.X + i * xStep;
                float y1 = rect.Position.Y + rect.Size.Y - (list[i] / maxVal) * (rect.Size.Y - 4) - 2;
                DrawLine(new Vector2(x0, y0), new Vector2(x1, y1), col, sp == _highlightSpecies ? 2f : 1f);
            }
        }
    }
}
