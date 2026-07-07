using System.Text;
using EcoSim.Core;
using EcoSim.Core.Data;
using EcoSim.Core.Batch;
using Godot;

namespace WildlandsEcoSim.UI;

public partial class BatchTestController : Control
{
    private SpinBox _seed = null!;
    private OptionButton _size = null!;
    private SpinBox _days = null!;
    private SpinBox _sampleEvery = null!;
    private SpinBox _animals = null!;
    private OptionButton _simBackend = null!;
    private SpinBox _runs = null!;
    private CheckBox _autoMigration = null!;
    private CheckBox _fuzzEnabled = null!;
    private SpinBox _fuzzTrials = null!;
    private SpinBox _fuzzSeed = null!;
    private SpinBox _fuzzIntensity = null!;
    private LineEdit _fuzzScope = null!;
    private OptionButton _fuzzProfile = null!;
    private Button _runBtn = null!;
    private Button _abortBtn = null!;
    private Label _statusLabel = null!;
    private ProgressBar _campaignProgress = null!;
    private Label _campaignProgressLabel = null!;
    private VBoxContainer _campaignResults = null!;
    private VBoxContainer _balanceDesigner = null!;
    private Button _designerTab = null!;
    private Button _savedRunsTab = null!;
    private VBoxContainer _savedRunsPanel = null!;

    private BatchGodotRunner? _runner;
    private CancellationTokenSource? _runCts;
    private readonly List<BatchReport> _campaignReports = [];

    public override void _Ready()
    {
        Theme = EcoSimThemeBuilder.Build();
        GetNode<ColorRect>("PageBg").Color = EcoSimThemeBuilder.UiShellBg;

        string dataRoot = ProjectSettings.GlobalizePath("res://data");
        if (Directory.Exists(dataRoot))
        {
            string? parent = Directory.GetParent(dataRoot)?.FullName;
            if (parent != null)
            {
                DataPaths.SetDataRoot(parent);
            }
        }

        _runner = new BatchGodotRunner(DataPaths.RepoRoot);
        BuildUi();
    }

    private void BuildUi()
    {
        var root = new VBoxContainer();
        root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        root.AddThemeConstantOverride("separation", 8);
        root.AddThemeConstantOverride("margin_left", 8);
        root.AddThemeConstantOverride("margin_right", 8);
        root.AddThemeConstantOverride("margin_top", 8);
        root.AddThemeConstantOverride("margin_bottom", 8);

        root.AddChild(BuildTopBar());

        var mainSplit = new HSplitContainer();
        mainSplit.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        mainSplit.SizeFlagsVertical = SizeFlags.ExpandFill;
        mainSplit.SplitOffset = 300;
        mainSplit.AddChild(BuildSidebar());
        mainSplit.AddChild(BuildMainColumn());
        root.AddChild(mainSplit);

        AddChild(root);
    }

    private Control BuildTopBar()
    {
        var panel = new PanelContainer();
        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 6);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        var title = new Label { Text = "⚗ BATCH RUNNER" };
        EcoSimFonts.ApplyFont(title, EcoSimFonts.Body, EcoSimThemeBuilder.Gold);
        row.AddChild(title);
        row.AddChild(MakeDivider());

        var simNote = new Label { Text = "CPU" };
        EcoSimFonts.ApplyFont(simNote, EcoSimFonts.Scaled6, EcoSimThemeBuilder.Dim);
        row.AddChild(simNote);
        row.AddChild(MakeDivider());

        var openBtn = new Button { Text = "Open Game" };
        openBtn.Pressed += () => GetTree().ChangeSceneToFile("res://scenes/Main.tscn");
        row.AddChild(openBtn);
        vbox.AddChild(row);

        var sub = BatchTestUiBuilder.MakeHint("CPU sim · balance tuning · optional fuzz campaigns");
        vbox.AddChild(sub);
        panel.AddChild(vbox);
        return panel;
    }

    private Control BuildSidebar()
    {
        var split = new VSplitContainer();
        split.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        split.SizeFlagsVertical = SizeFlags.ExpandFill;

        var simSection = BatchTestUiBuilder.MakeStoneSection("Sim Config");
        var simBody = BatchTestUiBuilder.AttachScrollBody(simSection);
        BuildSimConfig(simBody);
        split.AddChild(simSection);

        var fuzzSection = BatchTestUiBuilder.MakeStoneSection("Fuzzing");
        var fuzzBody = BatchTestUiBuilder.AttachScrollBody(fuzzSection);
        BuildFuzzConfig(fuzzBody);
        split.AddChild(fuzzSection);

        var runSection = BatchTestUiBuilder.MakeStoneSection("Run");
        var runBody = BatchTestUiBuilder.AttachFixedBody(runSection);
        BuildRunPanel(runBody);
        split.AddChild(runSection);

        Callable.From(() => split.SplitOffset = (int)(split.Size.Y * 0.38f)).CallDeferred();
        return split;
    }

    private void BuildSimConfig(VBoxContainer body)
    {
        body.AddChild(BatchTestUiBuilder.MakeFieldLabel("Seed"));
        _seed = BatchTestUiBuilder.MakeSpinBox(42, 1, 999999999);
        body.AddChild(_seed);

        body.AddChild(BatchTestUiBuilder.MakeFieldLabel("World size"));
        _size = BatchTestUiBuilder.MakeSizeOption();
        body.AddChild(_size);

        var dayRow = new HBoxContainer();
        dayRow.AddThemeConstantOverride("separation", 6);
        var daysCol = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        daysCol.AddChild(BatchTestUiBuilder.MakeFieldLabel("Days"));
        _days = BatchTestUiBuilder.MakeSpinBox(200, 10, 5000);
        daysCol.AddChild(_days);
        var sampleCol = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        sampleCol.AddChild(BatchTestUiBuilder.MakeFieldLabel("Sample every"));
        _sampleEvery = BatchTestUiBuilder.MakeSpinBox(10, 1, 200);
        sampleCol.AddChild(_sampleEvery);
        dayRow.AddChild(daysCol);
        dayRow.AddChild(sampleCol);
        body.AddChild(dayRow);

        body.AddChild(BatchTestUiBuilder.MakeFieldLabel("Animals density"));
        _animals = BatchTestUiBuilder.MakeSpinBox(0.45, 0, 1, 0.05);
        body.AddChild(_animals);

        body.AddChild(BatchTestUiBuilder.MakeFieldLabel("Sim backend"));
        _simBackend = BatchTestUiBuilder.MakeSimBackendOption();
        body.AddChild(_simBackend);

        body.AddChild(BatchTestUiBuilder.MakeFieldLabel("Sequential runs"));
        _runs = BatchTestUiBuilder.MakeSpinBox(1, 1, 100);
        body.AddChild(_runs);

        _autoMigration = new CheckBox { Text = "Auto migration" };
        EcoSimFonts.ApplyFont(_autoMigration, EcoSimFonts.Scaled6, EcoSimThemeBuilder.Dim);
        body.AddChild(_autoMigration);
    }

    private void BuildFuzzConfig(VBoxContainer body)
    {
        _fuzzEnabled = new CheckBox { Text = "Enable fuzz campaign" };
        EcoSimFonts.ApplyFont(_fuzzEnabled, EcoSimFonts.Scaled6, EcoSimThemeBuilder.Dim);
        body.AddChild(_fuzzEnabled);

        var trialRow = new HBoxContainer();
        trialRow.AddThemeConstantOverride("separation", 6);
        var trialsCol = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        trialsCol.AddChild(BatchTestUiBuilder.MakeFieldLabel("Trials"));
        _fuzzTrials = BatchTestUiBuilder.MakeSpinBox(50, 1, 500);
        trialsCol.AddChild(_fuzzTrials);
        var fuzzSeedCol = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        fuzzSeedCol.AddChild(BatchTestUiBuilder.MakeFieldLabel("Seed"));
        _fuzzSeed = BatchTestUiBuilder.MakeSpinBox(12345, 1, 999999999);
        fuzzSeedCol.AddChild(_fuzzSeed);
        trialRow.AddChild(trialsCol);
        trialRow.AddChild(fuzzSeedCol);
        body.AddChild(trialRow);

        body.AddChild(BatchTestUiBuilder.MakeFieldLabel("Intensity (±)"));
        _fuzzIntensity = BatchTestUiBuilder.MakeSpinBox(0.15, 0.05, 0.5, 0.05);
        body.AddChild(_fuzzIntensity);

        body.AddChild(BatchTestUiBuilder.MakeFieldLabel("Scope"));
        _fuzzScope = BatchTestUiBuilder.MakeLineField("all");
        body.AddChild(_fuzzScope);

        body.AddChild(BatchTestUiBuilder.MakeFieldLabel("Profile"));
        _fuzzProfile = BatchTestUiBuilder.MakeFuzzProfileOption();
        body.AddChild(_fuzzProfile);

        body.AddChild(BatchTestUiBuilder.MakeHint(
            "Fuzz perturbs from current balance panel values. Fast profiles use size s, 80 days, sparse samples. GPU profiles require WebGPU in the web runner; Godot batch uses CPU."));
    }

    private void BuildRunPanel(VBoxContainer body)
    {
        var btnRow = new HBoxContainer();
        btnRow.AddThemeConstantOverride("separation", 6);
        _runBtn = new Button { Text = "Run", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        EcoSimThemeBuilder.StylePrimaryButton(_runBtn);
        _runBtn.Pressed += OnRunPressed;
        _abortBtn = new Button { Text = "Abort", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        EcoSimThemeBuilder.StyleDangerButton(_abortBtn);
        _abortBtn.Pressed += OnAbortPressed;
        btnRow.AddChild(_runBtn);
        btnRow.AddChild(_abortBtn);
        body.AddChild(btnRow);

        var statusBox = BatchTestUiBuilder.MakeStatusBox();
        _statusLabel = statusBox.GetNode<Label>("StatusLabel");
        body.AddChild(statusBox);
    }

    private Control BuildMainColumn()
    {
        var split = new VSplitContainer();
        split.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        split.SizeFlagsVertical = SizeFlags.ExpandFill;

        var balanceSection = BatchTestUiBuilder.MakeStoneSection("Balance Tuning", "Designer · Saved Runs");
        var balanceBody = BatchTestUiBuilder.AttachScrollBody(balanceSection);
        BuildBalancePanel(balanceBody);
        split.AddChild(balanceSection);

        var campaignSection = BatchTestUiBuilder.MakeStoneSection("Campaign Results");
        var campaignBody = BatchTestUiBuilder.AttachScrollBody(campaignSection);
        BuildCampaignPanel(campaignBody);
        split.AddChild(campaignSection);

        Callable.From(() => split.SplitOffset = (int)(split.Size.Y * 0.52f)).CallDeferred();
        return split;
    }

    private void BuildBalancePanel(VBoxContainer body)
    {
        var tabs = new HBoxContainer();
        tabs.AddThemeConstantOverride("separation", 4);
        _designerTab = new Button { Text = "Designer", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _savedRunsTab = new Button { Text = "Saved Runs", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        EcoSimThemeBuilder.StyleActiveButton(_designerTab, true);
        _designerTab.Pressed += () => SetBalanceTab(true);
        _savedRunsTab.Pressed += () => SetBalanceTab(false);
        tabs.AddChild(_designerTab);
        tabs.AddChild(_savedRunsTab);
        body.AddChild(tabs);

        _balanceDesigner = new VBoxContainer();
        _balanceDesigner.AddThemeConstantOverride("separation", 6);
        _balanceDesigner.AddChild(BatchTestUiBuilder.MakeHint(
            "Balance designer mirrors the web batch runner. Species and behavior overrides will appear here in a future update. Runs still use default species data from data/species.json."));
        body.AddChild(_balanceDesigner);

        _savedRunsPanel = new VBoxContainer();
        _savedRunsPanel.Visible = false;
        _savedRunsPanel.AddThemeConstantOverride("separation", 6);
        _savedRunsPanel.AddChild(BatchTestUiBuilder.MakeHint("Saved batch reports from this session appear below."));
        body.AddChild(_savedRunsPanel);
    }

    private void BuildCampaignPanel(VBoxContainer body)
    {
        _campaignProgressLabel = new Label { Visible = false };
        EcoSimFonts.ApplyFont(_campaignProgressLabel, EcoSimFonts.Scaled6, EcoSimThemeBuilder.Dim);
        body.AddChild(_campaignProgressLabel);

        _campaignProgress = new ProgressBar
        {
            Visible = false,
            MinValue = 0,
            MaxValue = 100,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(0, 12),
        };
        EcoSimThemeBuilder.StyleNeedBar(_campaignProgress, EcoSimThemeBuilder.Gold);
        body.AddChild(_campaignProgress);

        _campaignResults = new VBoxContainer();
        _campaignResults.AddThemeConstantOverride("separation", 4);
        _campaignResults.AddChild(BatchTestUiBuilder.MakeHint("Fuzz campaign rankings appear here."));
        body.AddChild(_campaignResults);
    }

    private void SetBalanceTab(bool designer)
    {
        _balanceDesigner.Visible = designer;
        _savedRunsPanel.Visible = !designer;
        EcoSimThemeBuilder.StyleActiveButton(_designerTab, designer);
        EcoSimThemeBuilder.StyleActiveButton(_savedRunsTab, !designer);
    }

    private async void OnRunPressed()
    {
        if (_runner == null || _runner.IsRunning) return;

        _runBtn.Disabled = true;
        _abortBtn.Disabled = false;
        _campaignReports.Clear();
        ClearCampaignResults();
        SetStatus("Starting…");

        var form = ReadForm();
        _runCts = new CancellationTokenSource();

        try
        {
            var reports = await _runner.RunAsync(form, ReportProgress, _runCts.Token);
            _campaignReports.AddRange(reports);
            RenderCampaignResults(reports, form.Fuzz);
            if (reports.Count == 0)
            {
                SetStatus("Run aborted.", done: true);
            }
            else if (form.Fuzz)
            {
                var best = reports[0];
                SetStatus($"Campaign complete · best score {best.Score:F2} · {best.Outcome}", done: true);
            }
            else
            {
                var last = reports[^1];
                SetStatus(
                    $"Done · outcome={last.Outcome} · score={last.Score:F2} · pop={last.Summary.FinalPop} · day={last.Summary.FinalDay}",
                    done: true);
            }
        }
        catch (OperationCanceledException)
        {
            SetStatus("Run aborted.", done: true);
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}", done: true);
        }
        finally
        {
            _runBtn.Disabled = false;
            _abortBtn.Disabled = true;
            _campaignProgress.Visible = false;
            _campaignProgressLabel.Visible = false;
            _runCts?.Dispose();
            _runCts = null;
        }
    }

    private void OnAbortPressed()
    {
        _runner?.Abort();
        _runCts?.Cancel();
        SetStatus("Aborting…");
    }

    private void ReportProgress(BatchUiProgress prog)
    {
        Callable.From(() => UpdateProgressUi(prog)).CallDeferred();
    }

    private void UpdateProgressUi(BatchUiProgress prog)
    {
        var parts = new List<string>();
        if (prog.Mode == "fuzz")
        {
            parts.Add($"Trial {prog.TrialIndex + 1}/{prog.TrialTotal}");
            _campaignProgress.Visible = true;
            _campaignProgressLabel.Visible = true;
            float trialFrac = (prog.TrialIndex + prog.Day / (float)Math.Max(1, prog.TargetDays)) / Math.Max(1, prog.TrialTotal);
            _campaignProgress.Value = Math.Clamp(trialFrac * 100, 0, 100);
            _campaignProgressLabel.Text =
                $"Trial {prog.TrialIndex + 1}/{prog.TrialTotal} · Day {prog.Day}/{prog.TargetDays} · Pop {prog.TotalAlive}";
        }
        else if (prog.RunTotal > 1)
        {
            parts.Add($"Run {prog.RunIndex + 1}/{prog.RunTotal}");
            _campaignProgress.Visible = false;
            _campaignProgressLabel.Visible = false;
        }

        parts.Add($"Day {prog.Day}/{prog.TargetDays}");
        parts.Add($"Pop {prog.TotalAlive}");
        parts.Add($"Gen {prog.GenerationMax}");
        parts.Add($"{prog.WallMs / 1000:F1}s");
        SetStatus(string.Join(" · ", parts));
    }

    private BatchFormParams ReadForm()
    {
        return new BatchFormParams
        {
            Seed = (uint)_seed.Value,
            Size = SizeKeyFromIndex(_size.Selected),
            Days = (int)_days.Value,
            SampleEvery = (int)_sampleEvery.Value,
            Animals = _animals.Value,
            AutoMigration = _autoMigration.ButtonPressed,
            SimBackend = _simBackend.Selected == 1 ? "gpu" : "cpu",
            Runs = (int)_runs.Value,
            Fuzz = _fuzzEnabled.ButtonPressed,
            FuzzTrials = (int)_fuzzTrials.Value,
            FuzzSeed = (uint)_fuzzSeed.Value,
            FuzzIntensity = _fuzzIntensity.Value,
            FuzzScope = _fuzzScope.Text,
            FuzzProfile = FuzzProfileFromIndex(_fuzzProfile.Selected),
        };
    }

    private static string SizeKeyFromIndex(int index) => index switch
    {
        0 => "s",
        1 => "m",
        2 => "l",
        3 => "xl",
        4 => "xxl",
        _ => "m",
    };

    private static string FuzzProfileFromIndex(int index) => index switch
    {
        0 => "fast",
        1 => "fast-gpu",
        2 => "deep",
        3 => "deep-gpu",
        _ => "fast",
    };

    private void SetStatus(string text, bool done = false)
    {
        _statusLabel.Text = text;
        _statusLabel.AddThemeColorOverride("font_color", done ? EcoSimThemeBuilder.Text : EcoSimThemeBuilder.Gold);
    }

    private void ClearCampaignResults()
    {
        foreach (Node child in _campaignResults.GetChildren())
        {
            child.QueueFree();
        }
    }

    private void RenderCampaignResults(IReadOnlyList<BatchReport> reports, bool fuzz)
    {
        ClearCampaignResults();
        if (reports.Count == 0)
        {
            _campaignResults.AddChild(BatchTestUiBuilder.MakeHint("No results."));
            return;
        }

        if (fuzz)
        {
            var header = EcoSimThemeBuilder.MakeGoldTitle("Campaign rankings");
            _campaignResults.AddChild(header);
        }

        int rank = 1;
        foreach (var report in reports)
        {
            _campaignResults.AddChild(BuildReportRow(rank++, report, fuzz));
        }
    }

    private Control BuildReportRow(int rank, BatchReport report, bool showRank)
    {
        var row = new PanelContainer();
        row.AddThemeStyleboxOverride("panel", UiSliceCatalog.MakeInsetPanel());
        var text = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Text = FormatReportLine(rank, report, showRank),
        };
        EcoSimFonts.ApplyFont(text, EcoSimFonts.Scaled6, EcoSimThemeBuilder.Text);
        row.AddChild(text);
        return row;
    }

    private static string FormatReportLine(int rank, BatchReport report, bool showRank)
    {
        var sb = new StringBuilder();
        if (showRank)
        {
            sb.Append($"#{rank} ");
        }

        sb.Append($"score {report.Score:F2} · {report.Outcome} · pop {report.Summary.FinalPop} · day {report.Summary.FinalDay}");
        if (report.Summary.ExtinctAtDay.Count > 0)
        {
            sb.Append($" · extinct {string.Join(", ", report.Summary.ExtinctAtDay.Keys)}");
        }

        return sb.ToString();
    }

    private static Control MakeDivider()
    {
        var div = new ColorRect
        {
            CustomMinimumSize = new Vector2(2, 14),
            Color = EcoSimThemeBuilder.Edge,
        };
        return div;
    }
}
