using System.Security.Cryptography;
using OsuAimAnalyzer;

namespace OsuAimAnalyzer.Tests;

public class PracticePlannerTests
{
    internal static PracticeSourceIdentity Identity(BeatmapDocument document, int mods = 0, string? path = null)
        => new(path ?? Path.Combine(Path.GetTempPath(), "practice-test.osu"), Convert.ToHexString(MD5.HashData(document.ToBytes())), 1, mods);
    private static RecommendationEvidence Evidence(string unit, double observed, double lower, double upper)
        => new(unit, unit, observed, (lower + upper) / 2, lower, upper, 80, 1.4, 40, 80, "above", "test evidence", "test adjustment");
    private static readonly RecommendationEvidence[] Strong = { Evidence("bpm", 200, 140, 160), Evidence("spacing", 200, 140, 160), Evidence("ar", 7, 5, 6) };

    [Fact]
    public void Planner_SparseEvidenceUsesFourLabeledDefaultsAndExplainsFifth()
    {
        var document = BeatmapDocument.Parse(BeatmapDocumentTests.Map);
        var result = PracticeSeriesPlanner.Plan(Identity(document), document, 1, Array.Empty<RecommendationEvidence>(), PracticePitchPolicy.PreservePitch);
        Assert.Equal(4, result.Variants.Count);
        Assert.All(result.Variants, v => Assert.Contains("Conservative", v.Reason));
        Assert.Equal(.95, result.Variants[0].Options.SourceClockRate);
        Assert.Equal(.9, result.Variants[1].Options.SourceClockRate);
        Assert.Equal(1, result.Variants[2].Options.SourceClockRate);
        Assert.Contains(result.Notes, n => n.Contains("without compatible qualifying AR evidence"));
        Assert.Equal(BeatmapDocumentTests.Map, document.ToString());
    }

    [Fact]
    public void Planner_StrongEvidenceProducesFiveBoundedDistinctDeterministicVariants()
    {
        var document = BeatmapDocument.Parse(BeatmapDocumentTests.Map);
        var first = PracticeSeriesPlanner.Plan(Identity(document), document, 1, Strong, PracticePitchPolicy.ChangeWithRate);
        var second = PracticeSeriesPlanner.Plan(Identity(document), document, 1, Strong.Reverse().ToArray(), PracticePitchPolicy.ChangeWithRate);
        Assert.Equal(5, first.Variants.Count);
        Assert.Equal(first.Variants.Select(v => v.Preview.Document.ToString()), second.Variants.Select(v => v.Preview.Document.ToString()));
        Assert.Equal(5, first.Variants.Select(v => v.Preview.Document.ToString()).Distinct().Count());
        Assert.All(first.Variants, v =>
        {
            Assert.InRange(v.Options.SourceClockRate, .8, 1);
            Assert.InRange(v.Options.SpacingMultiplier, .8, 1);
            Assert.Equal(v.Options.SourceDifficulty.Od, v.Options.GeneratedDifficulty.Od);
            Assert.Equal(v.Options.SourceDifficulty.Cs, v.Options.GeneratedDifficulty.Cs);
            Assert.Empty(BeatmapParser.ParseDocument(v.Preview.Document).StandardStars);
        });
        Assert.Equal(6, first.Variants[^1].Options.GeneratedDifficulty.Ar);
        Assert.Throws<NotSupportedException>(() => ((IList<PracticeVariant>)first.Variants).Clear());
    }

    [Theory]
    [InlineData(ModUtils.DoubleTime)]
    [InlineData(ModUtils.Nightcore)]
    [InlineData(ModUtils.HalfTime)]
    [InlineData(ModUtils.HardRock)]
    [InlineData(ModUtils.Easy)]
    public void Planner_ModdedPlayDoesNotBakeModsOrUseIncompatibleEvidence(int mods)
    {
        var document = BeatmapDocument.Parse(BeatmapDocumentTests.Map);
        var result = PracticeSeriesPlanner.Plan(Identity(document, mods), document, 1, Strong, PracticePitchPolicy.PreservePitch);
        Assert.Equal(4, result.Variants.Count);
        Assert.Equal(.95, result.Variants[0].Options.SourceClockRate);
        Assert.Equal(mods, result.Source.PlayedMods);
        Assert.Contains(result.Notes, n => n.Contains("Modded-play evidence"));
    }

    [Fact]
    public void Planner_ConflictingEvidenceOmitsAffectedReductions()
    {
        var document = BeatmapDocument.Parse(BeatmapDocumentTests.Map);
        var conflict = Strong.Concat(new[] { Evidence("bpm", 100, 180, 200) with { Direction = "below" } }).ToArray();
        var result = PracticeSeriesPlanner.Plan(Identity(document), document, 1, conflict, PracticePitchPolicy.PreservePitch);
        Assert.Single(result.Variants);
        Assert.Equal("Reduced spacing", result.Variants[0].Name);
        Assert.Contains(result.Notes, n => n.Contains("conflicts"));
    }

    [Fact]
    public void Planner_InvalidAndWeakEvidenceFallsBackWithoutExtremeOptions()
    {
        var document = BeatmapDocument.Parse(BeatmapDocumentTests.Map);
        var evidence = new[] { Strong[0] with { ControlledUpperQuartile = double.NaN }, Strong[1] with { ErrorSamples = 2 }, Strong[2] with { ConfidencePercent = 15 } };
        var result = PracticeSeriesPlanner.Plan(Identity(document), document, 1, evidence, PracticePitchPolicy.PreservePitch);
        Assert.Equal(4, result.Variants.Count);
        Assert.Equal(.9, result.Variants[1].Options.SourceClockRate);
        Assert.All(result.Variants, v => Assert.Contains("Conservative", v.Reason));
    }

    [Fact]
    public void Planner_OmitsVideoBeforePlanningAllRecipes()
    {
        var document = BeatmapDocument.Parse(BeatmapDocumentTests.Map.Replace("2,1000,2000", "Video,0,\"movie.mp4\""));
        var result = PracticeSeriesPlanner.Plan(Identity(document), document, 1, Strong, PracticePitchPolicy.PreservePitch);
        Assert.Equal(5, result.Variants.Count);
        Assert.All(result.Variants, v => Assert.DoesNotContain("movie.mp4", v.Preview.Document.ToString()));
        Assert.Contains(result.Notes, n => n.Contains("omit videos"));
    }

    [Fact]
    public void Planner_UnchangedSpacingIsOmittedInsteadOfCountingDuplicateVariants()
    {
        string text = BeatmapDocumentTests.Map[..BeatmapDocumentTests.Map.IndexOf("[HitObjects]", StringComparison.Ordinal)]
            + "[HitObjects]\n100,100,101,1,0\n100,100,501,1,0";
        var document = BeatmapDocument.Parse(text);
        var result = PracticeSeriesPlanner.Plan(Identity(document), document, 1, Array.Empty<RecommendationEvidence>(), PracticePitchPolicy.PreservePitch);
        Assert.Equal(2, result.Variants.Count);
        Assert.Contains(result.Notes, n => n.Contains("duplicate") || n.Contains("does not meaningfully reduce"));
    }

    [Fact]
    public void Planner_RejectsWrongPlayOrChangedContentAndHonorsCancellation()
    {
        var document = BeatmapDocument.Parse(BeatmapDocumentTests.Map);
        var source = Identity(document);
        Assert.Throws<ArgumentException>(() => PracticeSeriesPlanner.Plan(source, document, 2, Strong, PracticePitchPolicy.PreservePitch));
        Assert.Throws<InvalidOperationException>(() => PracticeSeriesPlanner.Plan(source, BeatmapDocument.Parse(document + "\n// changed"), 1, Strong, PracticePitchPolicy.PreservePitch));
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => PracticeSeriesPlanner.Plan(source, document, 1, Strong, PracticePitchPolicy.PreservePitch, cancel.Token));
        Assert.Throws<OperationCanceledException>(() => BeatmapTransforms.Apply(document, BeatmapDocumentTests.Options(.9), cancel.Token));
    }

    [Fact]
    public async Task Source_RejectsChangedOrMissingFileAndNeverWrites()
    {
        string path = Path.Combine(Path.GetTempPath(), "practice-source-" + Guid.NewGuid() + ".osu");
        var document = BeatmapDocument.Parse(BeatmapDocumentTests.Map);
        var source = Identity(document, path: path);
        try
        {
            await File.WriteAllBytesAsync(path, document.ToBytes());
            Assert.Equal(document.ToBytes(), (await PracticePreviewSource.ReadVerifiedAsync(source)).ToBytes());
            Assert.Equal(document.ToBytes(), await File.ReadAllBytesAsync(path));
            await File.AppendAllTextAsync(path, "\n// changed");
            await Assert.ThrowsAsync<InvalidOperationException>(() => PracticePreviewSource.ReadVerifiedAsync(source));
            File.Delete(path);
            await Assert.ThrowsAsync<FileNotFoundException>(() => PracticePreviewSource.ReadVerifiedAsync(source));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task Session_RejectsLateCompletionAfterSelectionRetryOrCancel()
    {
        using var session = new PracticePreviewSession();
        var first = session.Begin(1);
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var late = Task.Run(async () => { await completion.Task; return first; });
        var second = session.Begin(2);
        completion.SetResult(true);
        Assert.False(session.IsCurrent(await late, 2));
        Assert.True(first.Token.IsCancellationRequested);
        Assert.False(session.IsCurrent(second, 1));
        Assert.True(session.IsCurrent(second, 2));
        var retry = session.Begin(2);
        Assert.False(session.IsCurrent(second, 2));
        Assert.True(session.IsCurrent(retry, 2));
        session.Cancel();
        Assert.False(session.IsCurrent(retry, 2));
    }

    [Fact]
    public void Readout_LabelsSourceModsDeltasAudioAndUncertainty()
    {
        var document = BeatmapDocument.Parse(BeatmapDocumentTests.Map);
        var result = PracticeSeriesPlanner.Plan(Identity(document), document, 1, Strong, PracticePitchPolicy.PreservePitch);
        string text = PracticePreviewControl.FormatPreview(result);
        Assert.Contains("targets: NM source", text);
        Assert.Contains("achieved head spacing", text);
        Assert.Contains("rendered on export", text);
        Assert.Contains("not proof of improvement", text);
        Assert.Contains("OD 6 → 6", text);
    }
}
