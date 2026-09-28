namespace OsuAimAnalyzer;

public sealed class PlayerInsight
{
    public string Category { get; init; } = "";
    public string Title { get; init; } = "";
    public string Summary { get; init; } = "";
    public string Detail { get; init; } = "";
    public double Priority { get; init; }
    public double Confidence { get; init; }
    public List<GraphPoint> Graph { get; init; } = new();
    public string GraphLabel { get; init; } = "Value";
}

public static class PlayerInsightsEngine
{
    public static List<PlayerInsight> Build(LifetimeAimAnalysisData data)
    {
        var results = new List<PlayerInsight>();
        if (data.Samples.Count < 8) return results;
        AddRecentTrends(data, results);
        AddModEffects(data, results);
        AddCategoryQuirks(data, results);
        AddTrainingResponses(data, results);
        return results.OrderByDescending(x => x.Priority).ThenByDescending(x => x.Confidence).Take(60).ToList();
    }

    private static void AddRecentTrends(LifetimeAimAnalysisData data, List<PlayerInsight> outList)
    {
        DateTime latest = data.Samples.Max(x => x.Play.TimestampUtc);
        DateTime cut = latest.AddDays(-30);
        DateTime prev = cut.AddDays(-30);
        var recent = data.Samples.Where(x => x.Play.TimestampUtc >= cut).ToList();
        var older = data.Samples.Where(x => x.Play.TimestampUtc >= prev && x.Play.TimestampUtc < cut).ToList();
        if (recent.Count < 6 || older.Count < 6) return;

        var specs = new (string Name, Func<LifetimeAimSample,double> F, string Unit)[]
        {
            ("aim proficiency", x=>x.Play.Proficiency, "points"),
            ("aim performance", x=>x.Play.RawAimRating, "points"),
            ("stability", x=>x.Play.Stability, "points"),
            ("arrival timing", x=>x.Play.Arrival, "points"),
            ("straightness", x=>x.Play.Straightness, "points"),
            ("centering", x=>x.Play.Landing, "points"),
        };
        foreach (var s in specs)
        {
            double a = recent.Average(s.F), b = older.Average(s.F), delta = a-b;
            double spread = StdDev(data.Samples.Select(s.F));
            double standardized = spread <= 1e-6 ? 0 : Math.Abs(delta)/spread;
            if (Math.Abs(delta) < 18 || standardized < .18) continue;
            double conf = Confidence(recent.Count + older.Count, standardized);
            string dir = delta > 0 ? "improved" : "declined";
            outList.Add(new PlayerInsight
            {
                Category = "Improvement",
                Title = $"{Cap(s.Name)} {dir} over the last 30 days",
                Summary = $"{delta:+0;-0} {s.Unit} vs the previous 30-day block · {conf:0}% evidence confidence",
                Detail = $"Your recent average {s.Name} is {a:0}, compared with {b:0} in the previous 30 days. The difference is {Math.Abs(delta):0} points ({(b == 0 ? 0 : 100*Math.Abs(delta)/Math.Abs(b)):0.#}%). This is a history comparison, not proof of one training cause.",
                Priority = Math.Abs(delta) * (0.6 + conf/100.0), Confidence = conf,
                Graph = Daily(data.Samples, s.F), GraphLabel = Cap(s.Name)
            });
        }
    }

    private static void AddModEffects(LifetimeAimAnalysisData data, List<PlayerInsight> outList)
    {
        AddBinaryMod(data, outList, ModUtils.Hidden, "Hidden", "HD");
        AddBinaryMod(data, outList, ModUtils.HardRock, "Hard Rock", "HR");
        AddBinaryMod(data, outList, ModUtils.DoubleTime | ModUtils.Nightcore, "DT/NC", "DT/NC");
    }

    private static void AddBinaryMod(LifetimeAimAnalysisData data, List<PlayerInsight> outList, int mask, string name, string shortName)
    {
        var with = data.Samples.Where(x => (x.Play.Mods & mask) != 0).ToList();
        var without = data.Samples.Where(x => (x.Play.Mods & mask) == 0).ToList();
        if (with.Count < 6 || without.Count < 10) return;

        var pairs = new List<(LifetimeAimSample A, LifetimeAimSample B)>();
        foreach (var a in with)
        {
            var b = without.Select(x => (S:x, D:DifficultyDistance(a.Play,x.Play), Same:MapFamily(a.Play.Map)==MapFamily(x.Play.Map)))
                .Where(x=>x.D < 1.25).OrderByDescending(x=>x.Same).ThenBy(x=>x.D).FirstOrDefault();
            if (b.S != null) pairs.Add((a,b.S));
        }
        if (pairs.Count < 6) return;

        void Metric(string label, Func<LifetimeAimSample,double> f, bool lowerBetter=false)
        {
            var ds = pairs.Select(p => f(p.A)-f(p.B)).ToArray();
            double delta = ds.Average(), sd = StdDev(ds), effect = sd<=1e-6 ? 0 : Math.Abs(delta)/sd;
            if (Math.Abs(delta) < (label.Contains("rate") ? 2.0 : 14.0) || effect < .18) return;
            bool better = lowerBetter ? delta < 0 : delta > 0;
            double conf = Confidence(pairs.Count*2, effect);
            outList.Add(new PlayerInsight
            {
                Category = "Mod effects", Title = $"{name} is associated with {(better?"better":"worse")} {label}",
                Summary = $"{shortName}: {delta:+0.0;-0.0} vs difficulty-matched non-{shortName} plays · {pairs.Count} matched runs",
                Detail = $"Across {pairs.Count} nearest difficulty-matched comparisons (star rating, BPM, spacing and AR), {name} changes {label} by {Math.Abs(delta):0.0} on average. Evidence confidence {conf:0}%. Matching reduces obvious difficulty bias, but this remains an association rather than a causal claim.",
                Priority = Math.Abs(delta)*(0.7+conf/100.0), Confidence=conf,
                Graph = pairs.OrderBy(x=>x.A.Play.TimestampUtc).Select(x=>new GraphPoint(x.A.Play.TimestampUtc.ToLocalTime().ToOADate(), f(x.A)-f(x.B), $"{x.A.Play.Map} · {shortName} minus matched baseline", x.A.Play.TimestampUtc.ToLocalTime())).ToList(),
                GraphLabel = $"Δ {label}"
            });
        }
        Metric("proficiency", x=>x.Play.Proficiency);
        Metric("stability", x=>x.Play.Stability);
        Metric("arrival timing", x=>x.Play.Arrival);
        Metric("late-acquisition rate", x=>x.CausePercents.GetValueOrDefault(AimErrorDiagnostics.LateAcquisition), true);
        Metric("center error", x=>x.MeanCenterError, true);
    }

    private static void AddCategoryQuirks(LifetimeAimAnalysisData data, List<PlayerInsight> outList)
    {
        double overallStability = data.Samples.Average(x=>x.Play.Stability);
        double overallArrival = data.Samples.Average(x=>x.Play.Arrival);
        foreach (string cat in DiagnosticsEngine.PresetNames.Where(x=>!x.Equals("All aim",StringComparison.OrdinalIgnoreCase)))
        {
            var rows = data.Samples.Where(x=>DiagnosticsEngine.PlayMatchesPreset(x.Play,cat)).ToList();
            if (rows.Count < 7) continue;
            double stab = rows.Average(x=>x.Play.Stability), arr=rows.Average(x=>x.Play.Arrival);
            double d1=stab-overallStability, d2=arr-overallArrival;
            if (Math.Abs(d1) >= 28)
                outList.Add(CategoryInsight(cat,"stability",stab,overallStability,d1,rows.Count));
            if (Math.Abs(d2) >= 28)
                outList.Add(CategoryInsight(cat,"arrival timing",arr,overallArrival,d2,rows.Count));
        }
    }

    private static PlayerInsight CategoryInsight(string cat,string metric,double value,double baseValue,double delta,int n)
    {
        double conf=Confidence(n,Math.Abs(delta)/100.0);
        return new PlayerInsight{Category="Aim quirks",Title=$"{metric} is unusually {(delta>0?"strong":"weak")} on {cat}",Summary=$"{delta:+0;-0} vs your overall baseline · {n} runs",Detail=$"On {cat}, your average {metric} is {value:0} versus {baseValue:0} across your full history. This is useful as a player-specific quirk: the same mechanic behaves differently for you on this pattern family.",Priority=Math.Abs(delta)*(0.7+conf/100),Confidence=conf};
    }

    private static void AddTrainingResponses(LifetimeAimAnalysisData data, List<PlayerInsight> outList)
    {
        var ordered=data.Samples.OrderBy(x=>x.Play.TimestampUtc).ToList();
        for(int i=1;i<ordered.Count;i++)
        {
            var after=ordered[i];
            var previous=ordered.Take(i).Where(x=>MapFamily(x.Play.Map)==MapFamily(after.Play.Map)).OrderByDescending(x=>x.Play.TimestampUtc).FirstOrDefault();
            if(previous==null) continue;
            double gain=after.Play.Proficiency-previous.Play.Proficiency;
            if(gain<45) continue;
            var practice=ordered.Where(x=>x.Play.TimestampUtc>previous.Play.TimestampUtc && x.Play.TimestampUtc<after.Play.TimestampUtc && (after.Play.TimestampUtc-x.Play.TimestampUtc).TotalHours<=3).ToList();
            if(practice.Count==0) continue;
            var related=practice.OrderBy(x=>DifficultyDistance(x.Play,after.Play)).First();
            double dd=DifficultyDistance(related.Play,after.Play);
            if(dd>2.2 && MapFamily(related.Play.Map)!=MapFamily(after.Play.Map)) continue;
            double baseGain=TypicalRetestGain(ordered,MapFamily(after.Play.Map),previous.Play.TimestampUtc);
            double excess=gain-baseGain;
            if(excess<25) continue;
            double conf=Math.Clamp(28 + Math.Min(40, gain/3.5) + Math.Min(22, practice.Count*4) - dd*8,20,88);
            string deltaDesc=DescribeDemandChange(related.Play,after.Play);
            outList.Add(new PlayerInsight{
                Category="Training response",Title=$"A practice variant preceded a +{gain:0} proficiency jump on {ShortMap(after.Play.Map)}",
                Summary=$"{ShortMap(related.Play.Map)} [{related.Play.ModsText}] → target improvement · {conf:0}% evidence confidence",
                Detail=$"Your previous target run was {previous.Play.Proficiency:0}; the later target run was {after.Play.Proficiency:0} (+{gain:0}). Between them you played {practice.Count} run(s). The closest practice run was {ShortMap(related.Play.Map)} [{related.Play.ModsText}], {deltaDesc}. Your typical retest gain on this map family is about {baseGain:+0;-0}, so this improvement was {excess:0} points larger than normal. Treat this as a promising training-response pattern, not proof that the intervening run caused the gain.",
                Priority=gain*(.65+conf/100),Confidence=conf,
                Graph=new(){new GraphPoint(previous.Play.TimestampUtc.ToLocalTime().ToOADate(),previous.Play.Proficiency,"Previous target",previous.Play.TimestampUtc.ToLocalTime()),new GraphPoint(related.Play.TimestampUtc.ToLocalTime().ToOADate(),related.Play.Proficiency,"Intervening practice",related.Play.TimestampUtc.ToLocalTime()),new GraphPoint(after.Play.TimestampUtc.ToLocalTime().ToOADate(),after.Play.Proficiency,"Later target",after.Play.TimestampUtc.ToLocalTime())},GraphLabel="Proficiency"
            });
        }
    }

    public static List<string> TrainingResponseForRun(PlayRow target, IReadOnlyList<PlayRow> history)
    {
        var ordered=history.Where(x=>x.TimestampUtc<target.TimestampUtc).OrderBy(x=>x.TimestampUtc).ToList();
        var previous=ordered.Where(x=>MapFamily(x.Map)==MapFamily(target.Map)).LastOrDefault();
        if(previous==null) return new();
        var between=ordered.Where(x=>x.TimestampUtc>previous.TimestampUtc && (target.TimestampUtc-x.TimestampUtc).TotalHours<=3).ToList();
        if(between.Count==0) return new();
        double gain=target.Proficiency-previous.Proficiency;
        return between.OrderBy(x=>DifficultyDistance(x,target)).Take(3).Select(p=>$"{ShortMap(p.Map)} [{p.ModsText}] preceded this retest ({DescribeDemandChange(p,target)}); target proficiency changed {gain:+0;-0} from the previous same-map-family run.").ToList();
    }

    private static double DifficultyDistance(PlayRow a,PlayRow b)
    {
        double star=Math.Abs(a.StarRating-b.StarRating)/.7;
        double bpm=Math.Abs(a.MeanBpm-b.MeanBpm)/Math.Max(25,Math.Max(a.MeanBpm,b.MeanBpm)*.12);
        double spacing=Math.Abs(a.SpacingP75-b.SpacingP75)/Math.Max(35,Math.Max(a.SpacingP75,b.SpacingP75)*.15);
        double ar=Math.Abs(a.EffectiveAr-b.EffectiveAr)/.7;
        return Math.Sqrt(star*star+bpm*bpm+spacing*spacing+ar*ar);
    }
    private static string DescribeDemandChange(PlayRow a,PlayRow b)
    {
        var parts=new List<string>();
        if(a.MeanBpm>0&&b.MeanBpm>0) parts.Add($"{a.MeanBpm:0} vs {b.MeanBpm:0} BPM ({100*(a.MeanBpm/b.MeanBpm-1):+0;-0}%)");
        if(a.SpacingP75>0&&b.SpacingP75>0) parts.Add($"{a.SpacingP75:0}px vs {b.SpacingP75:0}px spacing ({100*(a.SpacingP75/b.SpacingP75-1):+0;-0}%)");
        if(a.EffectiveAr>0&&b.EffectiveAr>0) parts.Add($"AR {a.EffectiveAr:0.0} vs {b.EffectiveAr:0.0}");
        return string.Join(", ",parts);
    }
    private static double TypicalRetestGain(List<LifetimeAimSample> rows,string family,DateTime before)
    {
        var f=rows.Where(x=>MapFamily(x.Play.Map)==family && x.Play.TimestampUtc<before).OrderBy(x=>x.Play.TimestampUtc).Select(x=>x.Play.Proficiency).ToList();
        if(f.Count<2)return 0; return f.Zip(f.Skip(1),(a,b)=>b-a).OrderBy(x=>x).ElementAt((f.Count-1)/2);
    }
    private static string MapFamily(string map){int i=map.IndexOf('[');return (i>0?map[..i]:map).Trim().ToLowerInvariant();}
    private static string ShortMap(string map){var s=map; if(s.Length>54)s=s[..51]+"…";return s;}
    private static List<GraphPoint> Daily(IEnumerable<LifetimeAimSample> rows,Func<LifetimeAimSample,double> f)=>rows.GroupBy(x=>x.Play.TimestampUtc.ToLocalTime().Date).OrderBy(g=>g.Key).Select(g=>new GraphPoint(g.Key.ToOADate(),g.Average(f),$"{g.Key:d} · {g.Count()} plays",g.Key)).ToList();
    private static double StdDev(IEnumerable<double> v){var a=v.Where(double.IsFinite).ToArray();if(a.Length<2)return 0;double m=a.Average();return Math.Sqrt(a.Sum(x=>(x-m)*(x-m))/(a.Length-1));}
    private static double Confidence(int n,double effect)=>Math.Clamp(25 + 45*Math.Clamp(effect,0,1.2)/1.2 + 30*(1-Math.Exp(-n/24.0)),15,94);
    private static string Cap(string s)=>string.IsNullOrEmpty(s)?s:char.ToUpperInvariant(s[0])+s[1..];
}
