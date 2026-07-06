using EcoSim.Core.Data;
using Godot;

namespace WildlandsEcoSim.UI;

public partial class PopGraph : Control
{
    private PopHistoryTracker? _history;
    private SpeciesCatalog? _catalog;
    private string? _lockedSpecies;
    private string? _hoveredSpecies;
    private int _hoverIndex = -1;
    private bool _showCrosshair;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(0, 70);
        MouseFilter = MouseFilterEnum.Stop;
        Resized += () =>
        {
            if (_history != null)
            {
                _history.SyncCapacity(Math.Max(226, (int)Size.X));
            }
        };
    }

    public void Bind(SpeciesCatalog catalog, PopHistoryTracker? history)
    {
        _catalog = catalog;
        _history = history;
        if (_history != null)
        {
            _history.SyncCapacity(Math.Max(226, (int)Size.X));
        }
        QueueRedraw();
    }

    public void SetHistory(PopHistoryTracker history)
    {
        _history = history;
        _history.SyncCapacity(Math.Max(226, (int)Size.X));
        QueueRedraw();
    }

    public void SetFocus(string? lockedSpecies, string? hoveredSpecies)
    {
        _lockedSpecies = lockedSpecies;
        _hoveredSpecies = hoveredSpecies;
        QueueRedraw();
    }

    public void SetHoverIndex(int index, bool showCrosshair)
    {
        _hoverIndex = index;
        _showCrosshair = showCrosshair;
        QueueRedraw();
    }

    public int HoverIndexFromMouse(Vector2 localPos)
    {
        if (_catalog == null || _history == null) return -1;
        int capacity = Math.Max(1, _history.GraphCapacity);
        var first = _history.GetSeries(_catalog.SpeciesKeys[0]);
        int len = Math.Max(first.Count, 1);
        float x = Mathf.Clamp(localPos.X, 0, Size.X);
        return (int)Math.Round(x / Math.Max(1f, Size.X - 1) * Math.Max(1, len - 1));
    }

    public override void _Draw()
    {
        if (_catalog == null || _history == null) return;

        var rect = GetRect();
        DrawStyleBox(UiSliceCatalog.MakeInsetPanel(), rect);

        float maxVal = 1;
        foreach (string sp in _catalog.SpeciesKeys)
        {
            foreach (int v in _history.GetSeries(sp)) maxVal = Math.Max(maxVal, v);
        }

        string? focusSpecies = _hoveredSpecies ?? _lockedSpecies;

        foreach (string sp in _catalog.SpeciesKeys)
        {
            var list = _history.GetSeries(sp);
            if (list.Count < 2) continue;

            var def = _catalog.Get(sp);
            Color baseCol = EcoSimThemeBuilder.SpeciesColor(def);
            bool focused = focusSpecies == sp;
            bool dimmed = !string.IsNullOrEmpty(focusSpecies) && !focused;
            Color col;
            if (focused && _hoveredSpecies == sp)
            {
                col = EcoSimThemeBuilder.Blue;
            }
            else if (dimmed)
            {
                col = new Color(baseCol.R, baseCol.G, baseCol.B, 0.25f);
            }
            else
            {
                col = new Color(baseCol.R, baseCol.G, baseCol.B, focused ? 1f : 0.95f);
            }

            float width = focused ? 2.4f : 1f;
            float xStep = rect.Size.X / Math.Max(1, list.Count - 1);

            for (int i = 1; i < list.Count; i++)
            {
                float x0 = rect.Position.X + (i - 1) * xStep;
                float y0 = rect.Position.Y + rect.Size.Y - (list[i - 1] / maxVal) * (rect.Size.Y - 4) - 2;
                float x1 = rect.Position.X + i * xStep;
                float y1 = rect.Position.Y + rect.Size.Y - (list[i] / maxVal) * (rect.Size.Y - 4) - 2;
                DrawLine(new Vector2(x0, y0), new Vector2(x1, y1), col, width);
            }
        }

        if (_showCrosshair && _hoverIndex >= 0)
        {
            var refSeries = _history.GetSeries(_catalog.SpeciesKeys[0]);
            if (refSeries.Count > 0)
            {
                int ix = Mathf.Clamp(_hoverIndex, 0, refSeries.Count - 1);
                float xStep = rect.Size.X / Math.Max(1, refSeries.Count - 1);
                float x = rect.Position.X + ix * xStep;
                DrawLine(
                    new Vector2(x, rect.Position.Y + 1),
                    new Vector2(x, rect.Position.Y + rect.Size.Y - 1),
                    new Color(EcoSimThemeBuilder.Gold.R, EcoSimThemeBuilder.Gold.G, EcoSimThemeBuilder.Gold.B, 0.75f),
                    1f);
            }
        }
    }
}
