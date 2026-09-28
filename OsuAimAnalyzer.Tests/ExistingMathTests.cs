using OsuAimAnalyzer;

namespace OsuAimAnalyzer.Tests;

public class ExistingMathTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(ModUtils.DoubleTime, 1.5)]
    [InlineData(ModUtils.Nightcore, 1.5)]
    [InlineData(ModUtils.Nightcore | ModUtils.DoubleTime, 1.5)]
    [InlineData(ModUtils.HalfTime, .75)]
    [InlineData(ModUtils.HardRock, 1)]
    public void ClockRate_ExistingMods_PreservesRate(int mods, double expected)
        => Assert.Equal(expected, ModUtils.ClockRate(mods));

    [Theory]
    [InlineData(9, 0, 9)]
    [InlineData(9, ModUtils.DoubleTime, 10.333333333333334)]
    [InlineData(9, ModUtils.HalfTime, 7.666666666666667)]
    [InlineData(9, ModUtils.HardRock, 10)]
    [InlineData(9, ModUtils.Easy, 4.5)]
    public void EffectiveAr_BaseAndMods_PreservesExistingValues(double ar, int mods, double expected)
        => Assert.Equal(expected, AimAnalyzer.EffectiveApproachRate(ar, mods), 10);

    [Theory]
    [InlineData(0, 1800)]
    [InlineData(5, 1200)]
    [InlineData(10, 450)]
    public void ApproachPreempt_Boundaries_PreservesMapClockMilliseconds(double ar, double expected)
        => Assert.Equal(expected, AimAnalyzer.ApproachPreempt(ar));

    [Fact]
    public void RadiusAndLookup_ExistingSemantics_AreUnchanged()
    {
        Assert.Equal(36.48, AimAnalyzer.CircleRadius(4), 10);
        Assert.Equal(10, ModUtils.ApplyDifficultyMods(9, ModUtils.HardRock));
        Assert.Equal(4.5, ModUtils.ApplyDifficultyMods(9, ModUtils.Easy));
        Assert.Equal(ModUtils.Nightcore | ModUtils.DoubleTime, ModUtils.NormalizeForStarLookup(ModUtils.Nightcore | ModUtils.NoFail));
        var map = new BeatmapData { StandardStars = new() { [ModUtils.DoubleTime] = 8.75 } };
        Assert.Equal((8.75, "exact stable cache"), AimAnalyzer.ResolveStarRating(map, ModUtils.DoubleTime));
    }
}
