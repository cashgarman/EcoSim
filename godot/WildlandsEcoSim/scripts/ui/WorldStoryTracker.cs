using System.Text;
using EcoSim.Core.Sim;
using Godot;

namespace WildlandsEcoSim.UI;

public partial class WorldStoryTracker : Node
{
    private readonly HashSet<int> _knownAlive = new();
    private readonly HashSet<int> _reportedDeaths = new();
    private readonly List<string> _lines = [];
    private RichTextLabel? _label;
    private const int MaxLines = 80;

    public override void _Ready()
    {
        _label = GetNodeOrNull<RichTextLabel>("%StoryText");
    }

    public void Reset()
    {
        _knownAlive.Clear();
        _reportedDeaths.Clear();
        _lines.Clear();
        Render();
    }

    public void OnSimTicked(SimSession session)
    {
        var aliveNow = new HashSet<int>();
        foreach (var c in session.State.Creatures)
        {
            if (c.Dead)
            {
                if (_knownAlive.Contains(c.Id) && _reportedDeaths.Add(c.Id))
                {
                    string cause = string.IsNullOrEmpty(c.Cause) ? "unknown" : c.Cause;
                    AddLine($"Day {session.State.Day}: {c.Sp} #{c.Id} died ({cause})");
                }
                continue;
            }

            aliveNow.Add(c.Id);
            if (_knownAlive.Add(c.Id))
            {
                AddLine($"Day {session.State.Day}: {c.Sp} #{c.Id} appeared");
            }
        }

        _knownAlive.RemoveWhere(id => !aliveNow.Contains(id));
    }

    public void LogGodAction(string text)
    {
        AddLine(text);
    }

    private void AddLine(string line)
    {
        _lines.Insert(0, line);
        if (_lines.Count > MaxLines) _lines.RemoveAt(_lines.Count - 1);
        Render();
    }

    private void Render()
    {
        if (_label == null) return;
        var sb = new StringBuilder();
        foreach (string line in _lines)
        {
            sb.AppendLine(line);
        }
        _label.Text = sb.ToString();
    }
}
