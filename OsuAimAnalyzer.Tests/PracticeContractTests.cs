using OsuAimAnalyzer;

namespace OsuAimAnalyzer.Tests;

public class PracticeContractTests
{
    private static readonly SerializedDifficulty Stats = new(5, 4, 9, 8);
    private static PracticeSourceIdentity Source() => new(Path.Combine(Path.GetTempPath(), "source.osu"), new string('a', 32), 10, ModUtils.DoubleTime);

    [Fact]
    public void Source_FromResolvedMap_DetachesIdentityWithoutIo()
    {
        var map = new BeatmapData { Path = Source().BeatmapPath, Hash = new string('A', 32) };
        var source = PracticeSourceIdentity.FromResolvedMap(map, 10, ModUtils.Nightcore);
        map.Path = "changed"; map.Hash = "changed";
        Assert.Equal(new string('a', 32), source.BeatmapHash);
        Assert.Equal(Source().BeatmapPath, source.BeatmapPath);
        Assert.Equal(10, source.SelectedPlayId);
        Assert.Equal(ModUtils.Nightcore, source.PlayedMods);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-md5")]
    [InlineData("gggggggggggggggggggggggggggggggg")]
    public void Source_InvalidHash_RejectsUnresolvedIdentity(string hash)
        => Assert.Throws<ArgumentException>(() => new PracticeSourceIdentity(Source().BeatmapPath, hash, null, 0));

    [Fact]
    public void Source_RelativePathOrInvalidPlay_RejectsAmbiguity()
    {
        Assert.Throws<ArgumentException>(() => new PracticeSourceIdentity("map.osu", new string('a', 32), null, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PracticeSourceIdentity(Source().BeatmapPath, new string('a', 32), 0, 0));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Options_InvalidRateOrSpacing_RejectsInvalidNumbers(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PracticeTransformOptions(value, 1, Stats, Stats, PracticePitchPolicy.PreservePitch));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PracticeTransformOptions(1, value, Stats, Stats, PracticePitchPolicy.PreservePitch));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.NegativeInfinity)]
    public void Difficulty_InvalidComponent_RejectsInvalidNumbers(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SerializedDifficulty(value, 4, 9, 8));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SerializedDifficulty(5, value, 9, 8));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SerializedDifficulty(5, 4, value, 8));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SerializedDifficulty(5, 4, 9, value));
    }

    [Fact]
    public void Options_ExplicitValues_DoNotBakePlayedModsOrCompensateStats()
    {
        var options = new PracticeTransformOptions(.8, .9, Stats, new(4, 4, 8, 7), PracticePitchPolicy.ChangeWithRate);
        Assert.Equal(.8, options.SourceClockRate);
        Assert.Equal(9, options.SourceDifficulty.Ar);
        Assert.Equal(8, options.GeneratedDifficulty.Ar);
        Assert.Equal(ModUtils.DoubleTime, Source().PlayedMods);
        Assert.Throws<ArgumentOutOfRangeException>(() => new PracticeTransformOptions(1, 1, Stats, Stats, (PracticePitchPolicy)99));
    }

    [Fact]
    public void Package_Entries_AreDetachedAndReadOnly()
    {
        var names = new[] { "one.osu", "two.osu" };
        var package = new PublishedPracticePackage(Source(), Path.Combine(Path.GetTempPath(), "published.osz"), names);
        names[0] = "mutated.osu";
        Assert.Equal("one.osu", package.DifficultyEntries[0]);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)package.DifficultyEntries)[0] = "mutated.osu");
    }

    [Theory]
    [InlineData("../map.osu")]
    [InlineData("folder/map.osu")]
    [InlineData("C:map.osu")]
    [InlineData("map.txt")]
    public void Package_InvalidArchiveEntry_RejectsTraversalOrWrongType(string entry)
        => Assert.Throws<ArgumentException>(() => new PublishedPracticePackage(Source(), Path.Combine(Path.GetTempPath(), "published.osz"), new[] { entry }));

    [Fact]
    public void Package_EmptyDuplicateOrRelativeOutput_RejectsAmbiguousResult()
    {
        var output = Path.Combine(Path.GetTempPath(), "published.osz");
        Assert.Throws<ArgumentException>(() => new PublishedPracticePackage(Source(), output, Array.Empty<string>()));
        Assert.Throws<ArgumentException>(() => new PublishedPracticePackage(Source(), output, new[] { "a.osu", "A.osu" }));
        Assert.Throws<ArgumentException>(() => new PublishedPracticePackage(Source(), "relative.osz", new[] { "a.osu" }));
    }
}
