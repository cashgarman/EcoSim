using EcoSim.Core.Sim;
using Godot;
using WildlandsEcoSim.UI;

namespace WildlandsEcoSim;

/// <summary>
/// Possession gameplay glue: feeds WASD/click/action intents into the core
/// PlayerControlSystem, pumps its pending events into UI (birth choice, transfer
/// notices, game over), and manages the player HUD and evolution panel.
/// Keys: P possess/release, E attack, R mate, T evolution tree.
/// </summary>
public partial class PlayerModeController : Node
{
    [Signal]
    public delegate void RestartRequestedEventHandler();

    [Signal]
    public delegate void PossessionChangedEventHandler();

    private EcoSimHost _host = null!;
    private GameApp _gameApp = null!;
    private WorldCamera _camera = null!;

    private PlayerHudPanel _playerHud = null!;
    private BirthChoicePanel _birthChoice = null!;
    private TransferNoticePanel _transferNotice = null!;
    private EvolutionPanel _evolution = null!;
    private GameOverPanel _gameOver = null!;
    private bool _pausedByModal;

    public bool IsPossessing => _host.Session?.Player.IsControlling == true;

    public void Setup(EcoSimHost host, GameApp gameApp, WorldCamera camera, Theme theme)
    {
        _host = host;
        _gameApp = gameApp;
        _camera = camera;

        _playerHud = new PlayerHudPanel { Theme = theme };
        AddChild(_playerHud);
        _transferNotice = new TransferNoticePanel { Theme = theme };
        AddChild(_transferNotice);
        _birthChoice = new BirthChoicePanel { Theme = theme };
        _birthChoice.ContinueChosen += OnBirthContinue;
        _birthChoice.NewbornChosen += OnBirthNewborn;
        AddChild(_birthChoice);
        _evolution = new EvolutionPanel { Theme = theme };
        _evolution.Closed += OnEvolutionClosed;
        AddChild(_evolution);
        _gameOver = new GameOverPanel { Theme = theme };
        _gameOver.RestartRequested += OnGameOverRestart;
        _gameOver.PossessSpeciesRequested += OnGameOverPossessSpecies;
        AddChild(_gameOver);

        _gameApp.SimTicked += OnSimTicked;
    }

    private bool ModalOpen =>
        _birthChoice.IsOpen || _evolution.IsOpen || _gameOver.IsOpen;

    public override void _Process(double delta)
    {
        var session = _host.Session;
        if (session == null) return;

        PumpPlayerEvents(session);
        PollMovement(session);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo) return;
        var session = _host.Session;
        if (session == null || ModalOpen) return;

        switch (key.Keycode)
        {
            case Key.P:
                TogglePossession(session);
                GetViewport().SetInputAsHandled();
                break;
            case Key.E when IsPossessing:
                session.Player.Intents.AttackPressed = true;
                GetViewport().SetInputAsHandled();
                break;
            case Key.R when IsPossessing:
                session.Player.Intents.MatePressed = true;
                GetViewport().SetInputAsHandled();
                break;
            case Key.T when IsPossessing:
                OpenEvolutionPanel(session);
                GetViewport().SetInputAsHandled();
                break;
        }
    }

    private void PollMovement(SimSession session)
    {
        if (!IsPossessing || ModalOpen) return;
        if (GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit) return;

        double x = 0, y = 0;
        if (Input.IsPhysicalKeyPressed(Key.D) || Input.IsPhysicalKeyPressed(Key.Right)) x += 1;
        if (Input.IsPhysicalKeyPressed(Key.A) || Input.IsPhysicalKeyPressed(Key.Left)) x -= 1;
        if (Input.IsPhysicalKeyPressed(Key.S) || Input.IsPhysicalKeyPressed(Key.Down)) y += 1;
        if (Input.IsPhysicalKeyPressed(Key.W) || Input.IsPhysicalKeyPressed(Key.Up)) y -= 1;
        session.Player.Intents.MoveX = x;
        session.Player.Intents.MoveY = y;
    }

    private void TogglePossession(SimSession session)
    {
        if (IsPossessing)
        {
            session.Player.Release();
            _playerHud.Visible = false;
        }
        else
        {
            var selected = session.State.Selected;
            if (selected is { Dead: false })
            {
                Possess(session, selected);
            }
            else
            {
                // No selection: jump into a random animal so P is always a way to start playing.
                var target = PickRandomOfSpecies(session, null);
                if (target == null) return;
                Possess(session, target);
            }
        }

        EmitSignal(SignalName.PossessionChanged);
    }

    private void Possess(SimSession session, Creature c)
    {
        session.Player.Possess(c);
        _camera.FollowEnabled = true;
        _camera.FocusCreature(c);
        _playerHud.Refresh(session);
    }

    private void PumpPlayerEvents(SimSession session)
    {
        var events = session.Player.PendingEvents;
        while (events.Count > 0 && !ModalOpen)
        {
            switch (events.Dequeue())
            {
                case BirthChoiceEvent birth:
                {
                    var def = session.Species.Get(birth.Species);
                    PauseForModal();
                    _birthChoice.ShowChoice(def, birth.NewbornIds.Count,
                        birth.NewbornIds[0], session.Progress.Points(birth.Species));
                    break;
                }
                case TransferEvent transfer:
                {
                    _transferNotice.ShowTransfer(transfer, session.Species, session.State);
                    _camera.FollowEnabled = true;
                    _camera.FocusCreature(transfer.To);
                    EmitSignal(SignalName.PossessionChanged);
                    break;
                }
                case GameOverEvent over:
                {
                    PauseForModal();
                    _playerHud.Visible = false;
                    _gameOver.ShowGameOver(session, over);
                    EmitSignal(SignalName.PossessionChanged);
                    break;
                }
            }
        }
    }

    private void PauseForModal()
    {
        if (!_gameApp.Paused)
        {
            _gameApp.TogglePause();
            _pausedByModal = true;
        }
    }

    private void ResumeFromModal()
    {
        if (_pausedByModal && _gameApp.Paused)
        {
            _gameApp.TogglePause();
        }
        _pausedByModal = false;
    }

    private void OnBirthContinue() => ResumeFromModal();

    private void OnBirthNewborn(int newbornId)
    {
        var session = _host.Session;
        if (session != null)
        {
            var newborn = session.Creatures.GetById(newbornId);
            if (newborn is { Dead: false })
            {
                Possess(session, newborn);
                EmitSignal(SignalName.PossessionChanged);
            }
        }
        ResumeFromModal();
    }

    private void OpenEvolutionPanel(SimSession session)
    {
        var c = session.Player.Controlled;
        if (c == null) return;
        PauseForModal();
        _evolution.Open(session, c.Sp);
    }

    private void OnEvolutionClosed() => ResumeFromModal();

    private void OnGameOverRestart()
    {
        _pausedByModal = false;
        EmitSignal(SignalName.RestartRequested);
    }

    private void OnGameOverPossessSpecies(string speciesKey)
    {
        var session = _host.Session;
        if (session == null) return;
        var target = PickRandomOfSpecies(session, speciesKey);
        if (target == null) return;
        Possess(session, target);
        EmitSignal(SignalName.PossessionChanged);
        ResumeFromModal();
    }

    /// <summary>Possess a random living creature (optionally of a specific species). Used on restart.</summary>
    public void PossessRandom(string? speciesKey = null)
    {
        var session = _host.Session;
        if (session == null) return;
        var target = PickRandomOfSpecies(session, speciesKey);
        if (target == null) return;
        Possess(session, target);
        EmitSignal(SignalName.PossessionChanged);
    }

    private static Creature? PickRandomOfSpecies(SimSession session, string? speciesKey)
    {
        var candidates = new List<Creature>();
        foreach (var c in session.State.Creatures)
        {
            if (c.Dead) continue;
            if (speciesKey != null && c.Sp != speciesKey) continue;
            candidates.Add(c);
        }
        if (candidates.Count == 0) return null;
        return candidates[(int)(GD.Randf() * candidates.Count) % candidates.Count];
    }

    private void OnSimTicked()
    {
        var session = _host.Session;
        if (session == null) return;
        if (IsPossessing)
        {
            _playerHud.Refresh(session);
        }
        else if (_playerHud.Visible)
        {
            _playerHud.Visible = false;
        }
    }

    /// <summary>Reset player state when a new world is generated.</summary>
    public void OnWorldRegenerated()
    {
        var session = _host.Session;
        if (session == null) return;
        session.Player.Release();
        session.Player.PendingEvents.Clear();
        _playerHud.Visible = false;
        _birthChoice.Visible = false;
        _evolution.Visible = false;
        _gameOver.HidePanel();
        _pausedByModal = false;
    }
}
