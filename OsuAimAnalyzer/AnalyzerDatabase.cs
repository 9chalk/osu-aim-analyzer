using Microsoft.Data.Sqlite;

namespace OsuAimAnalyzer;

public sealed class AnalyzerDatabase : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly object gate = new();

    public AnalyzerDatabase(string path)
    {
        connection = new SqliteConnection($"Data Source={path};Cache=Shared");
        connection.Open();
    }

    public void Initialize()
    {
        lock (gate)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                PRAGMA journal_mode=WAL;
                PRAGMA synchronous=NORMAL;
                CREATE TABLE IF NOT EXISTS Plays (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ReplayHash TEXT NOT NULL UNIQUE,
                    BeatmapHash TEXT NOT NULL,
                    ReplayPath TEXT NOT NULL DEFAULT '',
                    BeatmapPath TEXT NOT NULL DEFAULT '',
                    TimestampUtc TEXT NOT NULL,
                    Player TEXT NOT NULL,
                    MapName TEXT NOT NULL,
                    Mods INTEGER NOT NULL,
                    ModsText TEXT NOT NULL,
                    RankedStatus INTEGER NOT NULL,
                    StarRating REAL NOT NULL,
                    StarSource TEXT NOT NULL,
                    EffectiveAr REAL NOT NULL DEFAULT 0,
                    MeanBpm REAL NOT NULL,
                    SpacingP75 REAL NOT NULL,
                    SpacingP90 REAL NOT NULL,
                    MeanDensity REAL NOT NULL,
                    DensityP90 REAL NOT NULL,
                    Straightness REAL NOT NULL,
                    Landing REAL NOT NULL,
                    Arrival REAL NOT NULL,
                    Stability REAL NOT NULL,
                    Deceleration REAL NOT NULL,
                    IdealPathMatch REAL NOT NULL DEFAULT 0,
                    Proficiency REAL NOT NULL,
                    AimChallenge REAL NOT NULL,
                    RawAimRating REAL NOT NULL,
                    TransitionCount INTEGER NOT NULL,
                    Zone TEXT NOT NULL,
                    Count300 INTEGER NOT NULL,
                    Count100 INTEGER NOT NULL,
                    Count50 INTEGER NOT NULL,
                    CountMiss INTEGER NOT NULL
                );
                CREATE TABLE IF NOT EXISTS Transitions (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    PlayId INTEGER NOT NULL,
                    ObjectIndex INTEGER NOT NULL,
                    TimeMs INTEGER NOT NULL,
                    Bpm REAL NOT NULL,
                    Spacing REAL NOT NULL,
                    NormalizedSpacing REAL NOT NULL,
                    IntervalMs REAL NOT NULL,
                    Velocity REAL NOT NULL,
                    Density REAL NOT NULL,
                    Angle REAL NOT NULL,
                    Straightness REAL NOT NULL,
                    Landing REAL NOT NULL,
                    Arrival REAL NOT NULL,
                    Stability REAL NOT NULL,
                    Deceleration REAL NOT NULL,
                    IdealPathMatch REAL NOT NULL DEFAULT 0,
                    Proficiency REAL NOT NULL,
                    AxialError REAL NOT NULL,
                    LateralError REAL NOT NULL,
                    ErrorClass TEXT NOT NULL,
                    Challenge REAL NOT NULL,
                    FOREIGN KEY(PlayId) REFERENCES Plays(Id) ON DELETE CASCADE
                );
                CREATE TABLE IF NOT EXISTS Meta (
                    Key TEXT PRIMARY KEY,
                    Value TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_Plays_Time ON Plays(TimestampUtc);
                CREATE INDEX IF NOT EXISTS IX_Plays_Prof ON Plays(Proficiency);
                CREATE INDEX IF NOT EXISTS IX_Transitions_Play ON Transitions(PlayId);
                CREATE INDEX IF NOT EXISTS IX_Transitions_Bpm ON Transitions(Bpm);
                CREATE INDEX IF NOT EXISTS IX_Transitions_Spacing ON Transitions(NormalizedSpacing);
                CREATE INDEX IF NOT EXISTS IX_Transitions_Density ON Transitions(Density);
                """;
            cmd.ExecuteNonQuery();

            EnsureColumn("Plays", "ReplayPath", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn("Plays", "BeatmapPath", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn("Plays", "EffectiveAr", "REAL NOT NULL DEFAULT 0");
            EnsureColumn("Plays", "IdealPathMatch", "REAL NOT NULL DEFAULT 0");
            EnsureColumn("Transitions", "IdealPathMatch", "REAL NOT NULL DEFAULT 0");
            EnsureCurrentScoringModel();
        }
    }

    private void EnsureCurrentScoringModel()
    {
        int storedVersion = 0;
        using (var get = connection.CreateCommand())
        {
            get.CommandText = "SELECT Value FROM Meta WHERE Key='ScoringVersion' LIMIT 1";
            object? value = get.ExecuteScalar();
            if (value != null) int.TryParse(Convert.ToString(value), out storedVersion);
        }
        if (storedVersion >= ScoringConfig.CurrentScoringVersion) return;

        // v21 promotes the finalized Aim Tuner v9 model into the production analyzer.
        // The database already stores every transition feature used by that model, so old
        // history can be rescored locally without reopening replay files.
        var rows = new List<(long Id, long PlayId, TransitionMetric Metric)>();
        using (var read = connection.CreateCommand())
        {
            read.CommandText = """
                SELECT Id,PlayId,ObjectIndex,TimeMs,Bpm,Spacing,NormalizedSpacing,IntervalMs,Velocity,Density,Angle,
                       Straightness,Landing,Arrival,Stability,Deceleration,IdealPathMatch,AxialError,LateralError,ErrorClass,Challenge
                FROM Transitions
                """;
            using var r = read.ExecuteReader();
            while (r.Read())
            {
                var metric = new TransitionMetric
                {
                    PlayId = r.GetInt64(1), ObjectIndex = r.GetInt32(2), TimeMs = r.GetInt64(3),
                    Bpm = r.GetDouble(4), Spacing = r.GetDouble(5), NormalizedSpacing = r.GetDouble(6),
                    IntervalMs = r.GetDouble(7), Velocity = r.GetDouble(8), Density = r.GetDouble(9), Angle = r.GetDouble(10),
                    Straightness = r.GetDouble(11), Landing = r.GetDouble(12), Arrival = r.GetDouble(13), Stability = r.GetDouble(14),
                    Deceleration = r.GetDouble(15), IdealPathMatch = r.GetDouble(16), AxialError = r.GetDouble(17), LateralError = r.GetDouble(18),
                    ErrorClass = r.GetString(19), Challenge = r.GetDouble(20)
                };
                metric.IdealPathMatch = ScoringConfig.EffectiveIdealPathMatch(metric);
                rows.Add((r.GetInt64(0), metric.PlayId, metric));
            }
        }

        var transitionsByPlay = rows.GroupBy(x => x.PlayId).ToDictionary(g => g.Key, g => g.Select(x => x.Metric).OrderBy(x => x.TimeMs).ToList());
        var analyses = new Dictionary<long, PlayAnalysis>();
        using (var read = connection.CreateCommand())
        {
            read.CommandText = """
                SELECT Id,ReplayHash,BeatmapHash,Mods,StarRating,StarSource,EffectiveAr,MeanBpm,SpacingP75,SpacingP90,
                       MeanDensity,DensityP90,AimChallenge,Count300,Count100,Count50,CountMiss
                FROM Plays
                """;
            using var r = read.ExecuteReader();
            while (r.Read())
            {
                long playId = r.GetInt64(0);
                var replay = new ReplayData
                {
                    ReplayHash = r.GetString(1), BeatmapHash = r.GetString(2), Mods = r.GetInt32(3),
                    Count300 = checked((ushort)Math.Clamp(r.GetInt32(13), 0, ushort.MaxValue)),
                    Count100 = checked((ushort)Math.Clamp(r.GetInt32(14), 0, ushort.MaxValue)),
                    Count50 = checked((ushort)Math.Clamp(r.GetInt32(15), 0, ushort.MaxValue)),
                    CountMiss = checked((ushort)Math.Clamp(r.GetInt32(16), 0, ushort.MaxValue))
                };
                var a = new PlayAnalysis
                {
                    Replay = replay,
                    StarRating = r.GetDouble(4), StarSource = r.GetString(5), EffectiveAr = r.GetDouble(6),
                    MeanBpm = r.GetDouble(7), SpacingP75 = r.GetDouble(8), SpacingP90 = r.GetDouble(9),
                    MeanDensity = r.GetDouble(10), DensityP90 = r.GetDouble(11), AimChallenge = r.GetDouble(12),
                    Transitions = transitionsByPlay.GetValueOrDefault(playId) ?? new List<TransitionMetric>()
                };
                a.TransitionCount = a.Transitions.Count;
                if (a.Transitions.Count > 0) ProductionScoring.Apply(a);
                analyses[playId] = a;
            }
        }

        using var tx = connection.BeginTransaction();
        using (var updateTransition = connection.CreateCommand())
        {
            updateTransition.Transaction = tx;
            updateTransition.CommandText = "UPDATE Transitions SET Proficiency=$prof,IdealPathMatch=$ideal WHERE Id=$id";
            var pProf = updateTransition.Parameters.Add("$prof", SqliteType.Real);
            var pIdeal = updateTransition.Parameters.Add("$ideal", SqliteType.Real);
            var pId = updateTransition.Parameters.Add("$id", SqliteType.Integer);
            foreach (var row in rows)
            {
                pProf.Value = ProductionScoring.TransitionScore(row.Metric);
                pIdeal.Value = row.Metric.IdealPathMatch;
                pId.Value = row.Id;
                updateTransition.ExecuteNonQuery();
            }
        }

        using (var updatePlay = connection.CreateCommand())
        {
            updatePlay.Transaction = tx;
            updatePlay.CommandText = "UPDATE Plays SET Proficiency=$prof,RawAimRating=$rating,IdealPathMatch=$ideal,Zone=$zone WHERE Id=$id";
            var pProf = updatePlay.Parameters.Add("$prof", SqliteType.Real);
            var pRating = updatePlay.Parameters.Add("$rating", SqliteType.Real);
            var pIdeal = updatePlay.Parameters.Add("$ideal", SqliteType.Real);
            var pZone = updatePlay.Parameters.Add("$zone", SqliteType.Text);
            var pId = updatePlay.Parameters.Add("$id", SqliteType.Integer);
            foreach (var kv in analyses)
            {
                var a = kv.Value;
                if (a.Transitions.Count == 0) continue;
                pProf.Value = a.Proficiency;
                pRating.Value = a.RawAimRating;
                pIdeal.Value = a.Transitions.Average(x => x.IdealPathMatch);
                pZone.Value = a.TrainingZone;
                pId.Value = kv.Key;
                updatePlay.ExecuteNonQuery();
            }
        }

        using (var set = connection.CreateCommand())
        {
            set.Transaction = tx;
            set.CommandText = "INSERT INTO Meta(Key,Value) VALUES('ScoringVersion',$v) ON CONFLICT(Key) DO UPDATE SET Value=excluded.Value";
            set.Parameters.AddWithValue("$v", ScoringConfig.CurrentScoringVersion.ToString());
            set.ExecuteNonQuery();
        }
        tx.Commit();
    }

    private void EnsureColumn(string table, string column, string declaration)
    {
        using var info = connection.CreateCommand();
        info.CommandText = $"PRAGMA table_info({table})";
        using var r = info.ExecuteReader();
        bool found = false;
        while (r.Read()) if (string.Equals(r.GetString(1), column, StringComparison.OrdinalIgnoreCase)) { found = true; break; }
        if (found) return;
        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {declaration}";
        alter.ExecuteNonQuery();
    }

    public bool ContainsReplay(string replayHash)
    {
        lock (gate)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM Plays WHERE ReplayHash=$h LIMIT 1";
            cmd.Parameters.AddWithValue("$h", replayHash);
            return cmd.ExecuteScalar() != null;
        }
    }

    public void UpdateReplayPath(string replayHash, string path)
    {
        if (string.IsNullOrWhiteSpace(replayHash) || string.IsNullOrWhiteSpace(path)) return;
        lock (gate)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "UPDATE Plays SET ReplayPath=$p WHERE ReplayHash=$h AND (ReplayPath='' OR ReplayPath IS NULL)";
            cmd.Parameters.AddWithValue("$p", path);
            cmd.Parameters.AddWithValue("$h", replayHash);
            cmd.ExecuteNonQuery();
        }
    }

    public long Save(PlayAnalysis a)
    {
        lock (gate)
        {
            using var tx = connection.BeginTransaction();
            using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT OR IGNORE INTO Plays
                (ReplayHash,BeatmapHash,ReplayPath,BeatmapPath,TimestampUtc,Player,MapName,Mods,ModsText,RankedStatus,StarRating,StarSource,EffectiveAr,
                 MeanBpm,SpacingP75,SpacingP90,MeanDensity,DensityP90,Straightness,Landing,Arrival,Stability,Deceleration,IdealPathMatch,
                 Proficiency,AimChallenge,RawAimRating,TransitionCount,Zone,Count300,Count100,Count50,CountMiss)
                VALUES
                ($rh,$bh,$rp,$bp,$ts,$player,$map,$mods,$modst,$status,$sr,$srs,$ar,$bpm,$sp75,$sp90,$den,$den90,$straight,$land,$arrival,$stable,$decel,$ideal,
                 $prof,$challenge,$rating,$count,$zone,$c300,$c100,$c50,$miss);
                SELECT Id FROM Plays WHERE ReplayHash=$rh;
                """;
            cmd.Parameters.AddWithValue("$rh", a.Replay.ReplayHash);
            cmd.Parameters.AddWithValue("$bh", a.Replay.BeatmapHash);
            cmd.Parameters.AddWithValue("$rp", a.ReplayPath ?? "");
            cmd.Parameters.AddWithValue("$bp", a.Beatmap.Path ?? "");
            cmd.Parameters.AddWithValue("$ts", a.Replay.TimestampUtc.ToString("O"));
            cmd.Parameters.AddWithValue("$player", a.Replay.PlayerName);
            cmd.Parameters.AddWithValue("$map", a.Beatmap.DisplayName);
            cmd.Parameters.AddWithValue("$mods", a.Replay.Mods);
            cmd.Parameters.AddWithValue("$modst", ModUtils.ToShortString(a.Replay.Mods));
            cmd.Parameters.AddWithValue("$status", a.Beatmap.RankedStatus);
            cmd.Parameters.AddWithValue("$sr", a.StarRating);
            cmd.Parameters.AddWithValue("$srs", a.StarSource);
            cmd.Parameters.AddWithValue("$ar", a.EffectiveAr);
            cmd.Parameters.AddWithValue("$bpm", a.MeanBpm);
            cmd.Parameters.AddWithValue("$sp75", a.SpacingP75);
            cmd.Parameters.AddWithValue("$sp90", a.SpacingP90);
            cmd.Parameters.AddWithValue("$den", a.MeanDensity);
            cmd.Parameters.AddWithValue("$den90", a.DensityP90);
            cmd.Parameters.AddWithValue("$straight", a.MeanStraightness);
            cmd.Parameters.AddWithValue("$land", a.MeanLanding);
            cmd.Parameters.AddWithValue("$arrival", a.MeanArrival);
            cmd.Parameters.AddWithValue("$stable", a.MeanStability);
            cmd.Parameters.AddWithValue("$decel", a.MeanDeceleration);
            cmd.Parameters.AddWithValue("$ideal", a.MeanIdealPathMatch);
            cmd.Parameters.AddWithValue("$prof", a.Proficiency);
            cmd.Parameters.AddWithValue("$challenge", a.AimChallenge);
            cmd.Parameters.AddWithValue("$rating", a.RawAimRating);
            cmd.Parameters.AddWithValue("$count", a.TransitionCount);
            cmd.Parameters.AddWithValue("$zone", a.TrainingZone);
            cmd.Parameters.AddWithValue("$c300", a.Replay.Count300);
            cmd.Parameters.AddWithValue("$c100", a.Replay.Count100);
            cmd.Parameters.AddWithValue("$c50", a.Replay.Count50);
            cmd.Parameters.AddWithValue("$miss", a.Replay.CountMiss);
            long playId = Convert.ToInt64(cmd.ExecuteScalar());

            using var update = connection.CreateCommand();
            update.Transaction = tx;
            update.CommandText = "UPDATE Plays SET ReplayPath=CASE WHEN ReplayPath='' THEN $rp ELSE ReplayPath END, BeatmapPath=CASE WHEN BeatmapPath='' THEN $bp ELSE BeatmapPath END, EffectiveAr=CASE WHEN EffectiveAr=0 THEN $ar ELSE EffectiveAr END WHERE Id=$id";
            update.Parameters.AddWithValue("$rp", a.ReplayPath ?? "");
            update.Parameters.AddWithValue("$bp", a.Beatmap.Path ?? "");
            update.Parameters.AddWithValue("$ar", a.EffectiveAr);
            update.Parameters.AddWithValue("$id", playId);
            update.ExecuteNonQuery();

            using var check = connection.CreateCommand();
            check.Transaction = tx;
            check.CommandText = "SELECT COUNT(*) FROM Transitions WHERE PlayId=$p";
            check.Parameters.AddWithValue("$p", playId);
            if (Convert.ToInt32(check.ExecuteScalar()) == 0)
            {
                foreach (var t in a.Transitions)
                {
                    using var ic = connection.CreateCommand();
                    ic.Transaction = tx;
                    ic.CommandText = """
                        INSERT INTO Transitions
                        (PlayId,ObjectIndex,TimeMs,Bpm,Spacing,NormalizedSpacing,IntervalMs,Velocity,Density,Angle,Straightness,Landing,Arrival,Stability,Deceleration,IdealPathMatch,Proficiency,AxialError,LateralError,ErrorClass,Challenge)
                        VALUES($p,$oi,$tm,$bpm,$sp,$nsp,$int,$vel,$den,$ang,$str,$land,$arr,$stab,$dec,$ideal,$prof,$ax,$lat,$cls,$ch)
                        """;
                    ic.Parameters.AddWithValue("$p", playId); ic.Parameters.AddWithValue("$oi", t.ObjectIndex); ic.Parameters.AddWithValue("$tm", t.TimeMs);
                    ic.Parameters.AddWithValue("$bpm", t.Bpm); ic.Parameters.AddWithValue("$sp", t.Spacing); ic.Parameters.AddWithValue("$nsp", t.NormalizedSpacing);
                    ic.Parameters.AddWithValue("$int", t.IntervalMs); ic.Parameters.AddWithValue("$vel", t.Velocity); ic.Parameters.AddWithValue("$den", t.Density); ic.Parameters.AddWithValue("$ang", t.Angle);
                    ic.Parameters.AddWithValue("$str", t.Straightness); ic.Parameters.AddWithValue("$land", t.Landing); ic.Parameters.AddWithValue("$arr", t.Arrival);
                    ic.Parameters.AddWithValue("$stab", t.Stability); ic.Parameters.AddWithValue("$dec", t.Deceleration); ic.Parameters.AddWithValue("$ideal", ScoringConfig.EffectiveIdealPathMatch(t)); ic.Parameters.AddWithValue("$prof", t.Proficiency);
                    ic.Parameters.AddWithValue("$ax", t.AxialError); ic.Parameters.AddWithValue("$lat", t.LateralError); ic.Parameters.AddWithValue("$cls", t.ErrorClass); ic.Parameters.AddWithValue("$ch", t.Challenge);
                    ic.ExecuteNonQuery();
                }
            }
            tx.Commit();
            return playId;
        }
    }

    public List<PlayRow> LoadPlays(DateTime? sinceUtc = null)
    {
        lock (gate)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                SELECT Id,ReplayHash,BeatmapHash,ReplayPath,BeatmapPath,TimestampUtc,MapName,Player,Mods,ModsText,
                       CASE WHEN (Count300+Count100+Count50+CountMiss)>0 THEN
                            100.0*(Count300*300.0+Count100*100.0+Count50*50.0)/(300.0*(Count300+Count100+Count50+CountMiss)) ELSE 0 END AS Accuracy,
                       StarRating,EffectiveAr,MeanBpm,SpacingP75,DensityP90,Proficiency,AimChallenge,RawAimRating,TransitionCount,Zone,
                       Straightness,Landing,Arrival,Stability,Deceleration,IdealPathMatch,Count300,Count100,Count50,CountMiss
                FROM Plays
                WHERE ($since IS NULL OR TimestampUtc >= $since)
                ORDER BY TimestampUtc ASC
                """;
            cmd.Parameters.AddWithValue("$since", sinceUtc.HasValue ? sinceUtc.Value.ToString("O") : DBNull.Value);
            using var r = cmd.ExecuteReader();
            var list = new List<PlayRow>();
            while (r.Read())
            {
                var row = new PlayRow
                {
                    Id = r.GetInt64(0), ReplayHash = r.GetString(1), BeatmapHash = r.GetString(2), ReplayPath = r.GetString(3), BeatmapPath = r.GetString(4),
                    TimestampUtc = DateTime.Parse(r.GetString(5)).ToUniversalTime(), Map = r.GetString(6), Player = r.GetString(7), Mods = r.GetInt32(8), ModsText = r.GetString(9), Accuracy = r.GetDouble(10),
                    StarRating = r.GetDouble(11), EffectiveAr = r.GetDouble(12), MeanBpm = r.GetDouble(13), SpacingP75 = r.GetDouble(14), DensityP90 = r.GetDouble(15), Proficiency = r.GetDouble(16), AimChallenge = r.GetDouble(17), RawAimRating = r.GetDouble(18),
                    TransitionCount = r.GetInt32(19), Straightness = r.GetDouble(21), Landing = r.GetDouble(22), Arrival = r.GetDouble(23), Stability = r.GetDouble(24), Deceleration = r.GetDouble(25), IdealPathMatch = r.GetDouble(26)
                };
                row.AimTension = ScoringConfig.InferredAimTension(row.Stability, row.Deceleration, row.Straightness, row.Landing, null, row.MeanBpm, row.EffectiveAr, row.SpacingP75, row.DensityP90);
                int c300 = r.GetInt32(27), c100 = r.GetInt32(28), c50 = r.GetInt32(29), miss = r.GetInt32(30);
                row.MissCount = miss;
                row.PpEstimate = PpUtils.Estimate(row.StarRating, row.Accuracy, row.MissCount, row.Mods);
                row.Grade = ScoreUtils.Grade(c300, c100, c50, miss);
                row.Zone = ProductionScoring.ClassifyZone(row.Proficiency, row.Accuracy, row.MissCount);
                list.Add(row);
            }
            return list;
        }
    }

    public PlayRow? LoadPlay(long playId)
    {
        lock (gate)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                SELECT Id,ReplayHash,BeatmapHash,ReplayPath,BeatmapPath,TimestampUtc,MapName,Player,Mods,ModsText,
                       CASE WHEN (Count300+Count100+Count50+CountMiss)>0 THEN
                            100.0*(Count300*300.0+Count100*100.0+Count50*50.0)/(300.0*(Count300+Count100+Count50+CountMiss)) ELSE 0 END AS Accuracy,
                       StarRating,EffectiveAr,MeanBpm,SpacingP75,DensityP90,Proficiency,AimChallenge,RawAimRating,TransitionCount,Zone,
                       Straightness,Landing,Arrival,Stability,Deceleration,IdealPathMatch,Count300,Count100,Count50,CountMiss
                FROM Plays WHERE Id=$id LIMIT 1
                """;
            cmd.Parameters.AddWithValue("$id", playId);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;
            var row = new PlayRow
            {
                Id = r.GetInt64(0), ReplayHash = r.GetString(1), BeatmapHash = r.GetString(2), ReplayPath = r.GetString(3), BeatmapPath = r.GetString(4),
                TimestampUtc = DateTime.Parse(r.GetString(5)).ToUniversalTime(), Map = r.GetString(6), Player = r.GetString(7), Mods = r.GetInt32(8), ModsText = r.GetString(9), Accuracy = r.GetDouble(10),
                StarRating = r.GetDouble(11), EffectiveAr = r.GetDouble(12), MeanBpm = r.GetDouble(13), SpacingP75 = r.GetDouble(14), DensityP90 = r.GetDouble(15), Proficiency = r.GetDouble(16), AimChallenge = r.GetDouble(17), RawAimRating = r.GetDouble(18),
                TransitionCount = r.GetInt32(19), Straightness = r.GetDouble(21), Landing = r.GetDouble(22), Arrival = r.GetDouble(23), Stability = r.GetDouble(24), Deceleration = r.GetDouble(25), IdealPathMatch = r.GetDouble(26)
            };
            row.AimTension = ScoringConfig.InferredAimTension(row.Stability, row.Deceleration, row.Straightness, row.Landing, null, row.MeanBpm, row.EffectiveAr, row.SpacingP75, row.DensityP90);
            int c300 = r.GetInt32(27), c100 = r.GetInt32(28), c50 = r.GetInt32(29), miss = r.GetInt32(30);
            row.MissCount = miss;
            row.PpEstimate = PpUtils.Estimate(row.StarRating, row.Accuracy, row.MissCount, row.Mods);
            row.Grade = ScoreUtils.Grade(c300, c100, c50, miss);
            row.Zone = ProductionScoring.ClassifyZone(row.Proficiency, row.Accuracy, row.MissCount);
            return row;
        }
    }

    public List<TransitionMetric> LoadTransitions(long playId)
    {
        lock (gate)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                SELECT PlayId,ObjectIndex,TimeMs,Bpm,Spacing,NormalizedSpacing,IntervalMs,Velocity,Density,Angle,Straightness,Landing,Arrival,Stability,Deceleration,IdealPathMatch,Proficiency,AxialError,LateralError,ErrorClass,Challenge
                FROM Transitions WHERE PlayId=$p ORDER BY TimeMs
                """;
            cmd.Parameters.AddWithValue("$p", playId);
            return ReadTransitions(cmd);
        }
    }

    public List<TransitionMetric> LoadTransitions(IEnumerable<long> playIds)
    {
        var ids = playIds.Distinct().ToArray();
        var output = new List<TransitionMetric>();
        if (ids.Length == 0) return output;
        lock (gate)
        {
            for (int offset = 0; offset < ids.Length; offset += 400)
            {
                var chunk = ids.Skip(offset).Take(400).ToArray();
                using var cmd = connection.CreateCommand();
                var names = new List<string>();
                for (int i = 0; i < chunk.Length; i++) { string n = "$p" + i; names.Add(n); cmd.Parameters.AddWithValue(n, chunk[i]); }
                cmd.CommandText = $"SELECT PlayId,ObjectIndex,TimeMs,Bpm,Spacing,NormalizedSpacing,IntervalMs,Velocity,Density,Angle,Straightness,Landing,Arrival,Stability,Deceleration,IdealPathMatch,Proficiency,AxialError,LateralError,ErrorClass,Challenge FROM Transitions WHERE PlayId IN ({string.Join(',', names)})";
                output.AddRange(ReadTransitions(cmd));
            }
        }
        return output;
    }

    private static List<TransitionMetric> ReadTransitions(SqliteCommand cmd)
    {
        using var r = cmd.ExecuteReader();
        var list = new List<TransitionMetric>();
        while (r.Read())
        {
            list.Add(new TransitionMetric
            {
                PlayId = r.GetInt64(0), ObjectIndex = r.GetInt32(1), TimeMs = r.GetInt64(2), Bpm = r.GetDouble(3), Spacing = r.GetDouble(4), NormalizedSpacing = r.GetDouble(5), IntervalMs = r.GetDouble(6), Velocity = r.GetDouble(7), Density = r.GetDouble(8), Angle = r.GetDouble(9),
                Straightness = r.GetDouble(10), Landing = r.GetDouble(11), Arrival = r.GetDouble(12), Stability = r.GetDouble(13), Deceleration = r.GetDouble(14), IdealPathMatch = r.GetDouble(15), Proficiency = r.GetDouble(16), AxialError = r.GetDouble(17), LateralError = r.GetDouble(18), ErrorClass = r.GetString(19), Challenge = r.GetDouble(20)
            });
            list[^1].IdealPathMatch = ScoringConfig.EffectiveIdealPathMatch(list[^1]);
            list[^1].Proficiency = ProductionScoring.TransitionScore(list[^1]);
            list[^1].AimTension = ScoringConfig.InferredAimTension(list[^1].Stability, list[^1].Deceleration, list[^1].Straightness, list[^1].Landing, list[^1].ErrorClass, list[^1].Bpm, 0, list[^1].NormalizedSpacing, list[^1].Density);
        }
        return list;
    }

    public Dictionary<string, int> LoadErrorClasses(IEnumerable<long> playIds)
    {
        var ids = playIds.Distinct().ToArray();
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (ids.Length == 0) return result;
        lock (gate)
        {
            for (int offset = 0; offset < ids.Length; offset += 500)
            {
                var chunk = ids.Skip(offset).Take(500).ToArray();
                using var cmd = connection.CreateCommand();
                var names = new List<string>();
                for (int i = 0; i < chunk.Length; i++) { string n = "$p" + i; names.Add(n); cmd.Parameters.AddWithValue(n, chunk[i]); }
                cmd.CommandText = $"SELECT ErrorClass, COUNT(*) FROM Transitions WHERE PlayId IN ({string.Join(',', names)}) GROUP BY ErrorClass";
                using var r = cmd.ExecuteReader();
                while (r.Read()) result[r.GetString(0)] = result.GetValueOrDefault(r.GetString(0)) + r.GetInt32(1);
            }
        }
        return result;
    }

    public void Dispose() => connection.Dispose();
}
