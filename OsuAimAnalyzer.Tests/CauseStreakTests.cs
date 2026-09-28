using OsuAimAnalyzer;

namespace OsuAimAnalyzer.Tests;

public class CauseStreakTests
{
    private static TransitionMetric Problem(long play, int index, long time)
    {
        var metric = DiagnosisCompatibilityTests.Fixture(600).Transitions[0];
        metric.PlayId = play; metric.ObjectIndex = index; metric.TimeMs = time;
        return metric;
    }

    [Fact]
    public void Analyze_DifferentPlaysCannotFormOneStreak()
    {
        var result = AimErrorDiagnostics.Analyze(new[] { Problem(1, 0, 0), Problem(2, 1, 400) });
        Assert.Null(result.LongestStreak);
        Assert.Equal(2, result.ProblemCount);
    }

    [Fact]
    public void Analyze_InterleavedPlaysPreserveEachLocalSequence()
    {
        var clean = new TransitionMetric { PlayId = 2, ObjectIndex = 0, TimeMs = 200, Landing = 1000, Arrival = 1000, Stability = 1000,
            Deceleration = 1000, Straightness = 1000, IdealPathMatch = 1000, ErrorClass = "Clean" };
        var rows = new[] { Problem(1, 0, 0), clean, Problem(1, 1, 400), Problem(1, 2, 800) };
        var result = AimErrorDiagnostics.Analyze(rows);
        Assert.Equal(3, result.LongestStreak?.Count);
        Assert.Equal(3, result.CauseCounts[AimErrorDiagnostics.ShakeOff]);
        Assert.Equal(4, result.TotalCount);
    }

    [Theory]
    [InlineData(2, 1200, true)]
    [InlineData(3, 1200, false)]
    [InlineData(2, 1201, false)]
    [InlineData(0, 400, false)]
    [InlineData(-1, 400, false)]
    public void Analyze_AdjacencyPreservesLimitsAndRejectsRepeatedOrBackwardObjects(int gap, long time, bool expected)
    {
        var result = AimErrorDiagnostics.Analyze(new[] { Problem(1, 10, 0), Problem(1, 10 + gap, time) });
        Assert.Equal(expected, result.LongestStreak is not null);
    }

    [Fact]
    public void Analyze_CleanWithinPlayBreaksSequence()
    {
        var rows = new[] { Problem(1, 0, 0), new TransitionMetric { PlayId = 1, ObjectIndex = 1, TimeMs = 400,
            Landing = 1000, Arrival = 1000, Stability = 1000, Deceleration = 1000, Straightness = 1000, IdealPathMatch = 1000, ErrorClass = "Clean" }, Problem(1, 2, 800) };
        Assert.Null(AimErrorDiagnostics.Analyze(rows).LongestStreak);
        Assert.Null(AimErrorDiagnostics.Analyze(null).LongestStreak);
    }

    [Fact]
    public void Analyze_TiesAreDeterministicAndRangesBelongToChosenPlay()
    {
        var rows = new[] { Problem(9, 10, 0), Problem(3, 20, 0), Problem(9, 11, 400), Problem(3, 21, 400) };
        var first = AimErrorDiagnostics.Analyze(rows).LongestStreak!;
        Assert.Equal(3, first.PlayId);
        Assert.Equal(21, first.StartObject); Assert.Equal(22, first.EndObject);
        Assert.Equal(first, AimErrorDiagnostics.Analyze(rows.Reverse()).LongestStreak);
        Assert.Equal("play #3 · objects 21–22", first.Location);
    }

    [Fact]
    public void Analyze_UnknownPlayIdentityContributesCountsButNotStreaks()
    {
        var rows = new[] { Problem(0, 0, 0), Problem(0, 1, 400), Problem(-1, 2, 800) };
        var summary = AimErrorDiagnostics.Analyze(rows);
        Assert.Equal(3, summary.ProblemCount);
        Assert.Null(summary.LongestStreak);
    }

    [Fact]
    public void Analyze_SinglePlayPreservesDiagnosesCountsAndInputs()
    {
        var rows = DiagnosisCompatibilityTests.Fixture(600).Transitions;
        var expected = rows.Select(AimErrorDiagnostics.Diagnose).ToArray();
        var before = rows.Select(r => System.Text.Json.JsonSerializer.Serialize(r)).ToArray();
        var summary = AimErrorDiagnostics.Analyze(rows);
        Assert.Equal(expected, summary.Diagnoses);
        Assert.Equal(30, summary.ProblemCount); Assert.Equal(30, summary.TotalCount);
        Assert.Equal(30, summary.LongestStreak!.Count); Assert.Equal(10, summary.LongestStreak.PlayId);
        Assert.Equal(before, rows.Select(r => System.Text.Json.JsonSerializer.Serialize(r)).ToArray());
        Assert.All(rows, r => Assert.Equal("Overaim", r.ErrorClass));
    }

    [Fact]
    public void Profile_ExposesPlayContextAndClearsItWhenSelectionChanges()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var control = new AimCauseProfileControl();
                control.SetData(new[] { Problem(4, 0, 0), Problem(4, 1, 400), Problem(4, 2, 800), Problem(9, 0, 0) });
                Assert.Contains("play #4", control.AccessibleDescription);
                Assert.Contains("objects 1–3", control.AccessibleDescription);
                control.SetData(new[] { Problem(9, 0, 0) });
                Assert.DoesNotContain("play #4", control.AccessibleDescription);
                control.Clear();
                Assert.Equal("No trajectory diagnostics available.", control.AccessibleDescription);
                Assert.Null(control.Summary.LongestStreak);
            }
            catch (Exception e) { failure = e; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

}
