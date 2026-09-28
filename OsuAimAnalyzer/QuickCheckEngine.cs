namespace OsuAimAnalyzer;

public sealed class QuickCheckResult
{
    public string Label { get; set; } = "";
    public double InputRawProficiency { get; set; }
    public double FinalProficiency { get; set; }
    public int Misses { get; set; }
    public double AimPerformance { get; set; }
    public double BaseCapability { get; set; }
    public double ConsistencyMultiplier { get; set; }
    public double HardExecution { get; set; }
    public double AimActiveSeconds { get; set; }
    public PlayAnalysis Analysis { get; set; } = new();
    public PerformanceBreakdown Performance { get; set; } = null!;
}

public static class QuickCheckEngine
{
    public static QuickCheckResult Simulate(BeatmapData map, int mods, double rawProficiency, int misses, TuningConfig tuning)
    {
        var transitions = BuildGeometryTransitions(map, mods, rawProficiency);
        if (transitions.Count == 0)
            throw new InvalidOperationException("This difficulty has no analyzable aim transitions.");

        var replay = new ReplayData
        {
            Mode = 0,
            PlayerName = "Synthetic",
            BeatmapHash = map.Hash,
            ReplayHash = $"synthetic:{map.Hash}:{mods}:{rawProficiency:0.###}:{misses}",
            Mods = mods,
            CountMiss = (ushort)Math.Clamp(misses, 0, ushort.MaxValue),
            Count300 = (ushort)Math.Clamp(Math.Max(1, map.HitObjects.Count - misses), 1, ushort.MaxValue),
            TimestampUtc = DateTime.UtcNow
        };

        var (star, starSource) = AimAnalyzer.ResolveStarRating(map, mods);
        double finalProf = TuningScorer.RemapProficiency(rawProficiency, tuning);
        var analysis = new PlayAnalysis
        {
            Replay = replay,
            Beatmap = map,
            StarRating = star,
            StarSource = starSource,
            ClockRate = ModUtils.ClockRate(mods),
            EffectiveAr = AimAnalyzer.EffectiveApproachRate(map.AR, mods),
            Transitions = transitions,
            TransitionCount = transitions.Count,
            MeanBpm = transitions.Average(t => t.Bpm),
            SpacingP75 = Percentile(transitions.Select(t => t.NormalizedSpacing), .75),
            SpacingP90 = Percentile(transitions.Select(t => t.NormalizedSpacing), .90),
            MeanDensity = transitions.Average(t => t.Density),
            DensityP90 = Percentile(transitions.Select(t => t.Density), .90),
            MeanStraightness = rawProficiency,
            MeanLanding = rawProficiency,
            MeanArrival = rawProficiency,
            MeanStability = rawProficiency,
            MeanDeceleration = rawProficiency,
            MeanIdealPathMatch = rawProficiency,
            MeanTension = 0,
            Proficiency = finalProf,
            AimChallenge = Percentile(transitions.Select(t => t.Challenge), .85)
        };

        var perf = TuningScorer.Performance(analysis, rawProficiency, tuning);
        analysis.RawAimRating = perf.Score;
        return new QuickCheckResult
        {
            Label = $"SIM · raw {rawProficiency:0} · {misses} miss",
            InputRawProficiency = rawProficiency,
            FinalProficiency = finalProf,
            Misses = misses,
            AimPerformance = perf.Score,
            BaseCapability = perf.BaseCapabilityScore,
            ConsistencyMultiplier = perf.ConsistencyMultiplier,
            HardExecution = perf.HardExecutionProficiency,
            AimActiveSeconds = perf.AimActiveSeconds,
            Analysis = analysis,
            Performance = perf
        };
    }

    private static List<TransitionMetric> BuildGeometryTransitions(BeatmapData map, int mods, double q)
    {
        double rate = ModUtils.ClockRate(mods);
        double modCs = ModUtils.ApplyDifficultyMods(map.CS, mods);
        double modAr = ModUtils.ApplyDifficultyMods(map.AR, mods);
        double radius = AimAnalyzer.CircleRadius(modCs);
        double cs4 = AimAnalyzer.CircleRadius(4);
        double preemptRaw = AimAnalyzer.ApproachPreempt(modAr);
        var result = new List<TransitionMetric>();

        for (int i = 1; i < map.HitObjects.Count; i++)
        {
            var a0 = map.HitObjects[i - 1];
            var b0 = map.HitObjects[i];
            if (a0.Kind != HitObjectKind.Circle || b0.Kind != HitObjectKind.Circle) continue;
            long rawInterval = b0.TimeMs - a0.TimeMs;
            if (rawInterval <= 0 || rawInterval > 2000) continue;
            var a = Transform(a0, mods); var b = Transform(b0, mods);
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double distance = Math.Sqrt(dx*dx + dy*dy);
            if (distance < 18) continue;
            double interval = rawInterval / rate;
            if (interval < 20) continue;
            double bpm = BeatmapParser.BpmAt(map, b.TimeMs, rate);
            double spacing = distance * (cs4 / radius);
            double velocity = distance / (interval / 1000.0);
            double density = VisibleDensity(map, i, preemptRaw);
            double angle = JumpAngle(map, i, mods);
            double challenge = Challenge(spacing, velocity, angle, density, radius, cs4);
            double quality = Math.Clamp(q, 0, 1000);
            result.Add(new TransitionMetric
            {
                ObjectIndex = b.Index, TimeMs = b.TimeMs, Bpm = bpm, Spacing = distance,
                NormalizedSpacing = spacing, IntervalMs = interval, Velocity = velocity,
                Density = density, Angle = angle, Straightness = quality, Landing = quality,
                Arrival = quality, Stability = quality, Deceleration = quality, IdealPathMatch = quality,
                AimTension = Math.Clamp((1000-quality)/12.0, 0, 100), Proficiency = quality,
                AxialError = 0, LateralError = 0, ErrorClass = "Clean", Challenge = challenge
            });
        }
        return result;
    }

    private static HitObjectData Transform(HitObjectData o, int mods) => new()
    {
        Index = o.Index, X = o.X, Y = (mods & ModUtils.HardRock) != 0 ? 384-o.Y : o.Y,
        TimeMs = o.TimeMs, Kind = o.Kind
    };

    private static double VisibleDensity(BeatmapData map, int index, double preemptRaw)
    {
        long now = map.HitObjects[index].TimeMs; int count = 0;
        for (int i=index; i<map.HitObjects.Count; i++)
        {
            long dt = map.HitObjects[i].TimeMs-now;
            if (dt < 0) continue; if (dt > preemptRaw) break;
            if (map.HitObjects[i].Kind != HitObjectKind.Spinner) count++;
        }
        return count;
    }

    private static double JumpAngle(BeatmapData map, int index, int mods)
    {
        if (index < 2) return 0;
        var p=Transform(map.HitObjects[index-2],mods); var a=Transform(map.HitObjects[index-1],mods); var b=Transform(map.HitObjects[index],mods);
        double x1=a.X-p.X,y1=a.Y-p.Y,x2=b.X-a.X,y2=b.Y-a.Y;
        double m1=Math.Sqrt(x1*x1+y1*y1),m2=Math.Sqrt(x2*x2+y2*y2); if(m1<1||m2<1)return 0;
        return Math.Acos(Math.Clamp((x1*x2+y1*y2)/(m1*m2),-1,1))*180/Math.PI;
    }

    private static double Challenge(double spacing,double velocity,double angle,double density,double radius,double cs4)
    {
        double sf=.58+spacing/215.0;
        double vf=Math.Pow(Math.Max(.08,velocity/720.0),.86);
        double reversal=(1-Math.Cos(angle*Math.PI/180.0))/2.0;
        double af=1+.34*reversal;
        double df=1+Math.Max(0,density-2)*.055;
        double cf=Math.Pow(cs4/Math.Max(1,radius),.15);
        return 100*sf*vf*af*df*cf;
    }

    private static double Percentile(IEnumerable<double> source,double p)
    {
        var a=source.OrderBy(x=>x).ToArray(); if(a.Length==0)return 0;
        double pos=Math.Clamp(p,0,1)*(a.Length-1); int lo=(int)Math.Floor(pos),hi=(int)Math.Ceiling(pos);
        if(lo==hi)return a[lo]; double f=pos-lo; return a[lo]*(1-f)+a[hi]*f;
    }
}
