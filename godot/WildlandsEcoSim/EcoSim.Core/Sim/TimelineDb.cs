using Microsoft.Data.Sqlite;

namespace EcoSim.Core.Sim;

public sealed class TimelineDb : IDisposable
{
    private SqliteConnection? _conn;
    private string _runId = "";

    public void Open(string dbPath)
    {
        _conn?.Dispose();
        string connStr = dbPath.Contains("Data Source=", StringComparison.OrdinalIgnoreCase)
            ? dbPath
            : $"Data Source={dbPath}";
        _conn = new SqliteConnection(connStr);
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
        long bucket = SnapshotCache.BucketFor(snap.T, intervalSec);
        string json = SnapshotService.Serialize(snap);
        var meta = SnapshotService.ToMeta(snap);
        string metaJson = SnapshotService.SerializeMeta(meta);
        string creaturesJson = SnapshotService.SerializeCreatures(snap.Creatures);
        byte[] vegBlob = SnapshotService.PackVegBlob(snap.Veg, snap.VegCap);
        Execute(
            "INSERT OR REPLACE INTO snapshots(runId, tickBucket, t, day, json, metaJson, creaturesJson, vegBlob) " +
            "VALUES($r, $b, $t, $d, $j, $m, $c, $v)",
            ("$r", _runId), ("$b", bucket), ("$t", snap.T), ("$d", snap.Day),
            ("$j", json), ("$m", metaJson), ("$c", creaturesJson), ("$v", vegBlob));
    }

    public WorldSnapshot? LoadNearestSnapshot(double targetT)
    {
        if (_conn == null || string.IsNullOrEmpty(_runId)) return null;
        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            "SELECT json, metaJson, creaturesJson, vegBlob FROM snapshots " +
            "WHERE runId = $r AND t <= $t ORDER BY t DESC LIMIT 1";
        cmd.Parameters.AddWithValue("$r", _runId);
        cmd.Parameters.AddWithValue("$t", targetT);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        string? metaJson = reader.IsDBNull(1) ? null : reader.GetString(1);
        string? creaturesJson = reader.IsDBNull(2) ? null : reader.GetString(2);
        byte[]? vegBlob = reader.IsDBNull(3) ? null : (byte[])reader.GetValue(3);
        if (metaJson != null && creaturesJson != null && vegBlob != null)
        {
            var meta = SnapshotService.DeserializeMeta(metaJson);
            if (meta != null)
            {
                var split = SnapshotService.AssembleFromSplit(meta, creaturesJson, vegBlob);
                if (split != null)
                {
                    split.RunId = _runId;
                    return split;
                }
            }
        }

        if (reader.IsDBNull(0)) return null;
        var legacy = SnapshotService.Deserialize(reader.GetString(0));
        if (legacy != null) legacy.RunId = _runId;
        return legacy;
    }

    public List<WorldSnapshot> LoadRecentSnapshots(double beforeT, int limit)
    {
        var list = new List<WorldSnapshot>();
        if (_conn == null || string.IsNullOrEmpty(_runId) || limit <= 0) return list;

        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            "SELECT json, metaJson, creaturesJson, vegBlob FROM snapshots " +
            "WHERE runId = $r AND t <= $t ORDER BY t DESC LIMIT $l";
        cmd.Parameters.AddWithValue("$r", _runId);
        cmd.Parameters.AddWithValue("$t", beforeT);
        cmd.Parameters.AddWithValue("$l", limit);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var snap = ReadSnapshotRow(reader);
            if (snap != null) list.Add(snap);
        }

        list.Reverse();
        return list;
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
        MigrateSnapshotColumns();
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

    private void MigrateSnapshotColumns()
    {
        if (_conn == null) return;
        if (!ColumnExists("snapshots", "metaJson"))
        {
            Execute("ALTER TABLE snapshots ADD COLUMN metaJson TEXT");
        }

        if (!ColumnExists("snapshots", "creaturesJson"))
        {
            Execute("ALTER TABLE snapshots ADD COLUMN creaturesJson TEXT");
        }

        if (!ColumnExists("snapshots", "vegBlob"))
        {
            Execute("ALTER TABLE snapshots ADD COLUMN vegBlob BLOB");
        }
    }

    private bool ColumnExists(string table, string column)
    {
        if (_conn == null) return false;
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table})";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static WorldSnapshot? ReadSnapshotRow(SqliteDataReader reader)
    {
        string? metaJson = reader.IsDBNull(1) ? null : reader.GetString(1);
        string? creaturesJson = reader.IsDBNull(2) ? null : reader.GetString(2);
        byte[]? vegBlob = reader.IsDBNull(3) ? null : (byte[])reader.GetValue(3);
        if (metaJson != null && creaturesJson != null && vegBlob != null)
        {
            var meta = SnapshotService.DeserializeMeta(metaJson);
            if (meta != null)
            {
                return SnapshotService.AssembleFromSplit(meta, creaturesJson, vegBlob);
            }
        }

        if (reader.IsDBNull(0)) return null;
        return SnapshotService.Deserialize(reader.GetString(0));
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
