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
        Execute(
            "CREATE TABLE IF NOT EXISTS heartbeats(" +
            "id INTEGER PRIMARY KEY AUTOINCREMENT, " +
            "runId TEXT, t REAL, day INTEGER, json TEXT)");
        Execute(
            "CREATE TABLE IF NOT EXISTS creatureEvents(" +
            "id INTEGER PRIMARY KEY AUTOINCREMENT, " +
            "runId TEXT, creatureId INTEGER, t REAL, day INTEGER, kind TEXT, json TEXT)");
        Execute("CREATE INDEX IF NOT EXISTS idx_creature_run ON creatureEvents(runId, creatureId)");
    }

    public void SaveMeta(string key, string value)
    {
        Execute("INSERT OR REPLACE INTO meta(key, value) VALUES($k, $v)", ("$k", key), ("$v", value));
    }

    public string? LoadMeta(string key)
    {
        if (_conn == null) return null;
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM meta WHERE key = $k";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string;
    }

    public void AppendHeartbeat(double t, int day, string json)
    {
        if (_conn == null || string.IsNullOrEmpty(_runId)) return;
        Execute(
            "INSERT INTO heartbeats(runId, t, day, json) VALUES($r, $t, $d, $j)",
            ("$r", _runId), ("$t", t), ("$d", day), ("$j", json));
    }

    public void AppendCreatureEvent(int creatureId, double t, int day, string kind, string json)
    {
        if (_conn == null || string.IsNullOrEmpty(_runId)) return;
        Execute(
            "INSERT INTO creatureEvents(runId, creatureId, t, day, kind, json) VALUES($r, $c, $t, $d, $k, $j)",
            ("$r", _runId), ("$c", creatureId), ("$t", t), ("$d", day), ("$k", kind), ("$j", json));
    }

    public List<(string Store, double T, int Day, string Text)> ListRows(string store, int offset, int limit)
    {
        var rows = new List<(string, double, int, string)>();
        if (_conn == null || string.IsNullOrEmpty(_runId)) return rows;

        string sql = store switch
        {
            "world" => "SELECT t, day, html FROM worldEvents WHERE runId = $r ORDER BY t DESC LIMIT $l OFFSET $o",
            "creature" => "SELECT t, day, kind || ' ' || json FROM creatureEvents WHERE runId = $r ORDER BY t DESC LIMIT $l OFFSET $o",
            "heartbeat" => "SELECT t, day, json FROM heartbeats WHERE runId = $r ORDER BY t DESC LIMIT $l OFFSET $o",
            _ => "",
        };
        if (sql.Length == 0) return rows;

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$r", _runId);
        cmd.Parameters.AddWithValue("$l", limit);
        cmd.Parameters.AddWithValue("$o", offset);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rows.Add((store, reader.GetDouble(0), reader.GetInt32(1), reader.GetString(2)));
        }

        return rows;
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
