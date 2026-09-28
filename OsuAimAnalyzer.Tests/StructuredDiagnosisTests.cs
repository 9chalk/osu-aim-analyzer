using System.Globalization;
using System.Text.Json;
using OsuAimAnalyzer;

namespace OsuAimAnalyzer.Tests;

public class StructuredDiagnosisTests
{
    [Fact]
    public void RunDiagnosis_PersistedHistory_MatchesLoadedEvidence()
    {
        using var db = new AnalyzerDatabase(":memory:");
        db.Initialize();
        var (play, rows, _) = DiagnosisCompatibilityTests.Fixture(600);
        var cleanRows = Enumerable.Range(0, 50).Select(i => new TransitionMetric
        {
            ObjectIndex = i, TimeMs = i * 400, Bpm = 160, NormalizedSpacing = 180,
            Spacing = 180, Density = 2, Landing = 1000, Arrival = 1000, Stability = 1000,
            Straightness = 1000, Deceleration = 1000, IdealPathMatch = 1000, ErrorClass = "Clean"
        }).ToList();
        db.Save(new PlayAnalysis
        {
            Replay = new ReplayData { ReplayHash = "synthetic-history", TimestampUtc = play.TimestampUtc.AddDays(-1) },
            Proficiency = 900, EffectiveAr = 9, MeanBpm = 160, SpacingP75 = 180, DensityP90 = 2,
            Transitions = cleanRows, TransitionCount = cleanRows.Count
        });
        var history = db.LoadPlays();
        var loaded = AimTrainingDiagnosisEngine.BuildRunDiagnosisData(play, rows, history, db.LoadTransitions(history.Select(p => p.Id)));
        var persisted = AimTrainingDiagnosisEngine.BuildRunDiagnosisData(play, rows, db, history);
        Assert.NotEmpty(persisted.Evidence);
        Assert.Equal(loaded.Evidence, persisted.Evidence);
        Assert.Equal(loaded.DisplayText, persisted.DisplayText);
        Assert.Equal(50, db.LoadTransitions(history[0].Id).Count);
    }

    [Fact]
    public void RunDiagnosis_EmptyInput_DistinguishesNoTelemetryFromNoFactors()
    {
        var (play, rows, history) = DiagnosisCompatibilityTests.Fixture(0);
        var result = AimTrainingDiagnosisEngine.BuildRunDiagnosisData(play, rows, history, Array.Empty<TransitionMetric>());
        Assert.False(result.HasTelemetry);
        Assert.Empty(result.Evidence);
        Assert.Equal("", result.SelectionKey);
        Assert.Equal("No transition telemetry is available for this run.", result.DisplayText);
    }

    [Fact]
    public void RunDiagnosis_LoadedAndDatabasePaths_AgreeWithoutMutatingInputs()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        using var db = new AnalyzerDatabase(":memory:");
        db.Initialize();
        var (play, rows, history) = DiagnosisCompatibilityTests.Fixture(800);
        var before = JsonSerializer.Serialize(new { play, rows, history });
        var loaded = AimTrainingDiagnosisEngine.BuildRunDiagnosisData(play, rows, history, Array.Empty<TransitionMetric>());
        var persisted = AimTrainingDiagnosisEngine.BuildRunDiagnosisData(play, rows, db, history);
        Assert.Equal(persisted.DisplayText, loaded.DisplayText);
        Assert.Equal(persisted.Category, loaded.Category);
        Assert.True(loaded.HasTelemetry);
        Assert.Equal("cause:Shake-off", loaded.SelectionKey);
        Assert.Equal(30, loaded.TransitionCount);
        Assert.Equal(30, loaded.ErrorCount);
        Assert.Equal(100, loaded.ErrorRate);
        Assert.Single(loaded.TrainingResponse);
        Assert.Equal(before, JsonSerializer.Serialize(new { play, rows, history }));
        rows.Clear(); history.Clear(); play.Map = "mutated";
        Assert.Equal(30, loaded.TransitionCount);
        Assert.Single(loaded.TrainingResponse);
    }

    [Fact]
    public void RunDiagnosis_ControlledHistory_ExposesNumericEvidenceAndIgnoresUnrelatedRows()
    {
        var (play, rows, _) = DiagnosisCompatibilityTests.Fixture(600);
        var controlled = new PlayRow { Id = 20, Proficiency = 900, MeanBpm = 160, EffectiveAr = 9, SpacingP75 = 180, DensityP90 = 2 };
        var history = new List<PlayRow> { controlled, play };
        var otherRows = Enumerable.Range(0, 50).Select(i => new TransitionMetric
        {
            PlayId = 20, Bpm = 160, NormalizedSpacing = 180, Spacing = 180, Density = 2, Challenge = 100,
            Landing = 1000, Arrival = 1000, Stability = 1000, Straightness = 1000, Deceleration = 1000,
            IdealPathMatch = 1000, ErrorClass = "Clean", ObjectIndex = i, TimeMs = i * 400
        }).ToList();
        var before = JsonSerializer.Serialize(new { play, rows, history, otherRows });
        var result = AimTrainingDiagnosisEngine.BuildRunDiagnosisData(play, rows, history, otherRows);
        var bpm = Assert.Single(result.Evidence.Where(f => f.UnitKind == "bpm"));
        Assert.Equal(180, bpm.ObservedMedian);
        Assert.Equal(160, bpm.ControlledMedian);
        Assert.Equal(160, bpm.ControlledLowerQuartile);
        Assert.Equal(160, bpm.ControlledUpperQuartile);
        Assert.Equal(30, bpm.ErrorSamples);
        Assert.Equal(50, bpm.ComparisonSamples);
        Assert.InRange(bpm.ConfidencePercent, 8, 94);
        Assert.Equal(before, JsonSerializer.Serialize(new { play, rows, history, otherRows }));
        otherRows.Add(new TransitionMetric { PlayId = 999, Bpm = 9999 });
        Assert.Equal(result.Evidence, AimTrainingDiagnosisEngine.BuildRunDiagnosisData(play, rows, history, otherRows).Evidence);
        Assert.Throws<NotSupportedException>(() => ((IList<RecommendationEvidence>)result.Evidence).Clear());
        rows[0].Bpm = 999; otherRows[0].Bpm = 999;
        Assert.Equal(180, bpm.ObservedMedian);
    }

    [Fact]
    public void RunDiagnosis_CleanRun_PreservesDirectionFallback()
    {
        var (play, rows, history) = DiagnosisCompatibilityTests.Fixture(950);
        foreach (var row in rows)
        {
            row.Landing = row.Arrival = row.Stability = row.Straightness = row.Deceleration = row.IdealPathMatch = 1000;
            row.AxialError = row.LateralError = 0;
            row.ErrorClass = "Clean";
        }
        var result = AimTrainingDiagnosisEngine.BuildRunDiagnosisData(play, rows, history, Array.Empty<TransitionMetric>());
        Assert.Equal("direction:Clean", result.SelectionKey);
        Assert.Empty(result.Evidence);
    }
}
