using Microsoft.Data.Sqlite;

namespace EcoSim.Core.Sim;

public sealed class TimelineDb : IDisposable
{
    private SqliteConnection? _conn;
    private string _runId = "";

    public void Open(string dbPath)
    {
        _conn?.Dispose();
        _conn = new SqliteConnection($"Data Source={dbPath}");
        _conn.Open();
        EnsureSchema();
    }

    public void BeginRun(string runId)
    {
        _runId = runId;
        Execute("INSERT OR REPLACE INTO meta(key, value) VALUES('currentRunId', $v)", ("$v", runId));
    }

    public string RunId => _runId;

    public void SaveSnapshot(WorldSnapshot snap, double intervalSec)
    {
        if (_conn == null || string.IsNullOrEmpty(_runId)) return;
        snap.RunId = _runId;
        long bucket = (long)Math.Floor(snap.T / intervalSec);
        string json = SnapshotService.Serialize(snap);
        Execute(
            "INSERT OR REPLACE INTO snapshots(runId, tickBucket, t, day, json) VALUES($r, $b, $t, $d, $j)",
            ("$r", _runId), ("$b", bucket), ("$t", snap.T), ("$d", snap.Day), ("$j", json));
    }

    public WorldSnapshot? LoadNearestSnapshot(double targetT)
    {
        if (_conn == null || string.IsNullOrEmpty(_runId)) return null;
        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            "SELECT json FROM snapshots WHERE runId = $r AND t <= $t ORDER BY t DESC LIMIT 1";
        cmd.Parameters.AddWithValue("$r", _runId);
        cmd.Parameters.AddWithValue("$t", targetT);
        var result = cmd.ExecuteScalar();
        if (result is not string json) return null;
        return SnapshotService.Deserialize(json);
    }

    public List<(double T, int Day)> ListSnapshotTimes()
    {
        var list = new List<(double, int)>();
        if (_conn == null || string.IsNullOrEmpty(_runId)) return list;
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT t, day FROM snapshots WHERE runId = $r ORDER BY t ASC";
        cmd.Parameters.AddWithValue("$r", _runId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add((reader.GetDouble(0), reader.GetInt32(1)));
        }
        return list;
    }

    public void TruncateFuture(double fromT)
    {
        if (_conn == null || string.IsNullOrEmpty(_runId)) return;
        Execute("DELETE FROM snapshots WHERE runId = $r AND t > $t", ("$r", _runId), ("$t", fromT));
    }

    public void AppendWorldEvent(double t, int day, string html)
    {
        if (_conn == null || string.IsNullOrEmpty(_runId)) return;
        Execute(
            "INSERT INTO worldEvents(runId, t, day, html) VALUES($r, $t, $d, $h)",
            ("$r", _runId), ("$t", t), ("$d", day), ("$h", html));
    }

    private void EnsureSchema()
    {
        Execute("CREATE TABLE IF NOT EXISTS meta(key TEXT PRIMARY KEY, value TEXT)");
        Execute(
            "CREATE TABLE IF NOT EXISTS snapshots(" +
            "runId TEXT, tickBucket INTEGER, t REAL, day INTEGER, json TEXT, " +
            "PRIMARY KEY(runId, tickBucket))");
        Execute("CREATE INDEX IF NOT EXISTS idx_snap_run_t ON snapshots(runId, t)");
        Execute(
            "CREATE TABLE IF NOT EXISTS worldEvents(" +
            "id INTEGER PRIMARY KEY AUTOINCREMENT, " +
            "runId TEXT, t REAL, day INTEGER, html TEXT)");
        Execute("CREATE INDEX IF NOT EXISTS idx_world_run_t ON worldEvents(runId, t)");
    }

    private void Execute(string sql, params (string Name, object Value)[] args)
    {
        if (_conn == null) return;
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args)
        {
            cmd.Parameters.AddWithValue(name, value);
        }
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        _conn?.Dispose();
        _conn = null;
    }
}
