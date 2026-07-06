using EcoSim.Core.Data;
using EcoSim.Core.Sim;
using Godot;

namespace WildlandsEcoSim.UI;

public partial class EcosystemPanel : DraggablePanel
{
  [Signal]
  public delegate void SpeciesLockedEventHandler(string speciesKey);

  private VBoxContainer _rows = null!;
  private SpeciesCatalog? _catalog;
  private string? _lockedSpecies;

  public string? LockedSpecies => _lockedSpecies;

  public override void _Ready()
  {
    LayoutKey = "ecosystem";
    base._Ready();
    _rows = GetNode<VBoxContainer>("%SpeciesRows");
  }

  public void Bind(SpeciesCatalog catalog)
  {
    _catalog = catalog;
    _rows.GetChildren().ToList().ForEach(c => c.QueueFree());

    foreach (string sp in catalog.SpeciesKeys)
    {
      var def = catalog.Get(sp);
      var btn = new Button
      {
        Text = $"{def.Emoji} {def.Label}",
      };
      btn.Pressed += () => OnSpeciesPressed(sp, btn);
      btn.SetMeta("species", sp);
      _rows.AddChild(btn);
    }
  }

  public void Refresh(SimSession session)
  {
    if (_catalog == null) return;

    foreach (Node child in _rows.GetChildren())
    {
      if (child is not Button btn) continue;
      string sp = btn.GetMeta("species").AsString();
      int count = session.State.Creatures.Count(c => c.Sp == sp && !c.Dead);
      var def = _catalog.Get(sp);
      btn.Text = $"{def.Emoji} {def.Label}  {count}";
      btn.Modulate = sp == _lockedSpecies ? new Color(1f, 0.85f, 0.4f) : Colors.White;
    }
  }

  private void OnSpeciesPressed(string sp, Button btn)
  {
    _lockedSpecies = _lockedSpecies == sp ? null : sp;
    EmitSignal(SignalName.SpeciesLocked, _lockedSpecies ?? "");
  }
}
