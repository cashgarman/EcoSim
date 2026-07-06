using System.Text.Json;
using EcoSim.Core.Batch;
using EcoSim.Core.Data;
using EcoSim.Core.Sim;

string? dataRoot = null;
uint? seed = 1;
string size = "s";
int days = 100;
int sampleEvery = 10;
string? reportDir = null;
string? balanceFile = null;
bool autoMigration = false;
double maxWallMs = 120_000;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--data-root" when i + 1 < args.Length:
            dataRoot = args[++i];
            break;
        case "--seed" when i + 1 < args.Length:
            seed = uint.Parse(args[++i]);
            break;
        case "--size" when i + 1 < args.Length:
            size = args[++i];
            break;
        case "--days" when i + 1 < args.Length:
            days = int.Parse(args[++i]);
            break;
        case "--sample-every" when i + 1 < args.Length:
            sampleEvery = int.Parse(args[++i]);
            break;
        case "--report-dir" when i + 1 < args.Length:
            reportDir = args[++i];
            break;
        case "--balance-file" when i + 1 < args.Length:
            balanceFile = args[++i];
            break;
        case "--auto-migration":
            autoMigration = true;
            break;
        case "--max-wall-ms" when i + 1 < args.Length:
            maxWallMs = double.Parse(args[++i]);
            break;
    }
}

if (dataRoot == null)
{
    dataRoot = DataPaths.RepoRoot;
}

var session = SimSession.Create(dataRoot, seed);
if (!string.IsNullOrEmpty(balanceFile))
{
    var overrides = BalanceConfigLoader.LoadFromFile(balanceFile);
    BalanceConfigLoader.Apply(session, overrides);
}

var harness = new BatchHarness(session);
harness.Init(new BatchRunConfig
{
    Seed = seed,
    Size = size,
    TargetDays = days,
    SampleEveryDays = sampleEvery,
    AutoMigration = autoMigration,
});

int initialPop = harness.GenerateWorld();
Console.WriteLine($"Seeded {initialPop} creatures (world {session.State.W}x{session.State.H})");

var result = harness.Run(new BatchRunOptions
{
    TargetDays = days,
    SampleEveryDays = sampleEvery,
    MaxWallMs = maxWallMs,
});
string runId = $"batch-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{Random.Shared.Next(100000, 999999)}";
var report = harness.GetReport(runId, result);
harness.Teardown();

Console.WriteLine($"Outcome: {report.Outcome}  score={report.Score:F2}  finalPop={report.Summary.FinalPop}  day={report.Summary.FinalDay}");

reportDir ??= Path.Combine(dataRoot, "reports");
Directory.CreateDirectory(reportDir);
string outPath = Path.Combine(reportDir, $"{runId}.json");
var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(outPath, json);
Console.WriteLine($"Wrote {outPath}");

return report.Outcome is "stable" or "partial_collapse" ? 0 : 1;
