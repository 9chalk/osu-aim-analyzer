using System.Globalization;
using OsuAimAnalyzer;

namespace OsuAimAnalyzer.Tests;

public class DiagnosisCompatibilityTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(600)]
    [InlineData(800)]
    [InlineData(950)]
    public void BuildRunDiagnosis_BaselineFixtures_PreservesText(int score)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        using var db = new AnalyzerDatabase(":memory:");
        db.Initialize();
        var (play, transitions, history) = Fixture(score);
        var actual = AimTrainingDiagnosisEngine.BuildRunDiagnosis(play, transitions, db, history).Replace("\r\n", "\n");
        var snapshot = Path.Combine(AppContext.BaseDirectory, "Snapshots", $"run-{score}.txt");
        Assert.Equal(File.ReadAllText(snapshot).Replace("\r\n", "\n"), actual);
    }

    internal static (PlayRow Play, List<TransitionMetric> Transitions, List<PlayRow> History) Fixture(int score)
    {
        var play = new PlayRow { Id = 10, Proficiency = score, Map = "Fixture [Target]", EffectiveAr = 9, MeanBpm = 180, SpacingP75 = 180, DensityP90 = 2, TimestampUtc = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc) };
        var rows = Enumerable.Range(0, score == 0 ? 0 : 30).Select(i => new TransitionMetric
        {
            PlayId = play.Id, ObjectIndex = i, TimeMs = i * 400, Bpm = 180, NormalizedSpacing = 180,
            Spacing = 180, Density = 2, Challenge = 100, Landing = 400, Arrival = 900, Stability = 200,
            Deceleration = 900, Straightness = 900, IdealPathMatch = 900, AxialError = .6, ErrorClass = "Overaim"
        }).ToList();
        var history = new List<PlayRow>
        {
            new() { Id = 1, Map = "Fixture [Previous]", TimestampUtc = play.TimestampUtc.AddHours(-2), Proficiency = 500, MeanBpm = 180, SpacingP75 = 180, EffectiveAr = 9 },
            new() { Id = 2, Map = "Practice [Easy]", TimestampUtc = play.TimestampUtc.AddHours(-1), Proficiency = 850, MeanBpm = 160, SpacingP75 = 170, EffectiveAr = 9 }
        };
        return (play, rows, history);
    }
}
