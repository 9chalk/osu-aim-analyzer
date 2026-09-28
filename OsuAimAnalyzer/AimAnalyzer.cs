namespace OsuAimAnalyzer;

public sealed class AimAnalyzer
{
    private readonly AppSettings settings;

    public AimAnalyzer(AppSettings settings) => this.settings = settings;

    public PlayAnalysis Analyze(ReplayData replay, BeatmapData map)
    {
        if (replay.Mode != 0 || map.Mode != 0)
            throw new InvalidOperationException("Only osu!standard replays are supported.");
        if (replay.Frames.Count < 3)
            throw new InvalidDataException("Replay does not contain enough cursor frames.");
        if (map.HitObjects.Count < 2)
            throw new InvalidDataException("Beatmap does not contain enough hit objects.");

        double rate = ModUtils.ClockRate(replay.Mods);
        double modCs = ModUtils.ApplyDifficultyMods(map.CS, replay.Mods);
        double modAr = ModUtils.ApplyDifficultyMods(map.AR, replay.Mods);
        double radius = CircleRadius(modCs);
        double cs4Radius = CircleRadius(4);
        double preemptRaw = ApproachPreempt(modAr); // raw map-clock ms; speed mods scale both object time and AR duration.

        var sampler = new CursorSampler(replay.Frames);
        var transitions = new List<TransitionMetric>();

        for (int i = 1; i < map.HitObjects.Count; i++)
        {
            var a0 = map.HitObjects[i - 1];
            var b0 = map.HitObjects[i];

            if (settings.AnalyzeOnlyPureJumps)
            {
                if (a0.Kind != HitObjectKind.Circle || b0.Kind != HitObjectKind.Circle) continue;
            }
            else
            {
                if (a0.Kind is HitObjectKind.Spinner or HitObjectKind.Other || b0.Kind is HitObjectKind.Spinner or HitObjectKind.Other) continue;
            }

            long rawInterval = b0.TimeMs - a0.TimeMs;
            if (rawInterval <= 0 || rawInterval > 2000) continue;

            var a = TransformForMods(a0, replay.Mods);
            var b = TransformForMods(b0, replay.Mods);
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance < 18) continue; // stacks/tiny movements are not useful raw-aim samples.

            double intervalRealMs = rawInterval / rate;
            if (intervalRealMs < 20) continue;

            double stepRawMs = Math.Max(1.0, settings.ResampleIntervalMs * rate);
            var path = sampler.Sample(a.TimeMs, b.TimeMs, stepRawMs);
            if (path.Count < 3) continue;

            double bpm = BeatmapParser.BpmAt(map, b.TimeMs, rate);
            double normalizedSpacing = distance * (cs4Radius / radius);
            double velocity = distance / (intervalRealMs / 1000.0);
            double density = VisibleDensity(map, i, preemptRaw);
            double angle = JumpAngle(map, i, replay.Mods);

            var metric = AnalyzeTransition(a, b, path, sampler, radius, rate, bpm);
            metric.ObjectIndex = b.Index;
            metric.TimeMs = b.TimeMs;
            metric.Bpm = bpm;
            metric.Spacing = distance;
            metric.NormalizedSpacing = normalizedSpacing;
            metric.IntervalMs = intervalRealMs;
            metric.Velocity = velocity;
            metric.Density = density;
            metric.Angle = angle;
            metric.AimTension = ScoringConfig.InferredAimTension(metric.Stability, metric.Deceleration, metric.Straightness, metric.Landing, metric.ErrorClass, bpm, EffectiveApproachRate(map.AR, replay.Mods), normalizedSpacing, density);
            metric.Challenge = Challenge(normalizedSpacing, velocity, angle, density, radius, cs4Radius);
            metric.Proficiency = ScoringConfig.TransitionProficiency(metric);
            transitions.Add(metric);
        }

        if (transitions.Count == 0)
            throw new InvalidOperationException("No analyzable jump transitions were found in this replay/map pair.");

        var result = new PlayAnalysis
        {
            Replay = replay,
            Beatmap = map,
            ClockRate = rate,
            EffectiveAr = EffectiveApproachRate(map.AR, replay.Mods),
            Transitions = transitions,
            TransitionCount = transitions.Count,
            MeanBpm = transitions.Average(x => x.Bpm),
            SpacingP75 = Percentile(transitions.Select(x => x.NormalizedSpacing), .75),
            SpacingP90 = Percentile(transitions.Select(x => x.NormalizedSpacing), .90),
            MeanDensity = transitions.Average(x => x.Density),
            DensityP90 = Percentile(transitions.Select(x => x.Density), .90),
            MeanStraightness = transitions.Average(x => x.Straightness),
            MeanLanding = transitions.Average(x => x.Landing),
            MeanArrival = transitions.Average(x => x.Arrival),
            MeanStability = transitions.Average(x => x.Stability),
            MeanDeceleration = transitions.Average(x => x.Deceleration),
            MeanIdealPathMatch = transitions.Average(x => ScoringConfig.EffectiveIdealPathMatch(x)),
            MeanTension = transitions.Average(x => x.AimTension),
            AimChallenge = Percentile(transitions.Select(x => x.Challenge), .85)
        };

        (result.StarRating, result.StarSource) = ResolveStarRating(map, replay.Mods);
        result.Proficiency = ScoringConfig.AggregateProficiency(transitions.Select(x => x.Proficiency));
        result.RawAimRating = result.AimChallenge * Math.Exp((result.Proficiency - ScoringConfig.AimRatingReferenceProficiency) / 220.0);
        double scoreAccuracy = ScoreUtils.Accuracy(replay.Count300, replay.Count100, replay.Count50, replay.CountMiss);
        result.TrainingZone = ScoringConfig.ClassifyZone(result.Proficiency, scoreAccuracy, replay.CountMiss);
        return result;
    }

    private static TransitionMetric AnalyzeTransition(HitObjectData a, HitObjectData b, List<CursorPoint> path, CursorSampler sampler, double radius, double rate, double bpm)
    {
        double idealDx = b.X - a.X, idealDy = b.Y - a.Y;
        double idealDist = Math.Sqrt(idealDx * idealDx + idealDy * idealDy);
        double ux = idealDx / idealDist, uy = idealDy / idealDist;
        double px = -uy, py = ux;

        double pathLength = 0;
        double lateralSq = 0;
        int lateralN = 0;
        for (int i = 1; i < path.Count; i++)
        {
            pathLength += Dist(path[i - 1], path[i]);
            double rx = path[i].X - a.X, ry = path[i].Y - a.Y;
            double lateral = rx * px + ry * py;
            lateralSq += lateral * lateral;
            lateralN++;
        }
        double efficiency = Math.Clamp(idealDist / Math.Max(idealDist, pathLength), 0, 1);
        double effScore = Clamp1000(1000 * (1 - 2.1 * (1 - efficiency)));
        double lateralRms = Math.Sqrt(lateralSq / Math.Max(1, lateralN));
        double deviationScore = 1000 * Math.Exp(-1.5 * Math.Pow(lateralRms / Math.Max(1, radius), 2));
        double straightness = .58 * effScore + .42 * deviationScore;

        var atHit = sampler.At(b.TimeMs);
        double ex = atHit.X - b.X, ey = atHit.Y - b.Y;
        double axial = (ex * ux + ey * uy) / radius;
        double lateralError = (ex * px + ey * py) / radius;
        double landingRatio = Math.Sqrt(ex * ex + ey * ey) / radius;
        double landing = Clamp1000(1000 * Math.Exp(-0.72 * landingRatio * landingRatio));

        double arrivalRealMs = FindStableArrivalMs(sampler, b, radius, rate);
        double arrival = ArrivalScore(arrivalRealMs);

        double stability = StabilityScore(sampler, b, radius, rate);
        string errorClass = ClassifyError(sampler, b, ux, uy, px, py, radius, rate, axial, lateralError, landingRatio);
        double deceleration = DecelerationScore(path, landing, errorClass);
        double idealPathMatch = IdealPathMatchScore(a, b, path, radius, bpm);
        double aimTension = ScoringConfig.InferredAimTension(stability, deceleration, straightness, landing, errorClass);

        return new TransitionMetric
        {
            Straightness = Clamp1000(straightness),
            Landing = landing,
            Arrival = arrival,
            Stability = stability,
            Deceleration = deceleration,
            IdealPathMatch = idealPathMatch,
            AimTension = aimTension,
            AxialError = axial,
            LateralError = lateralError,
            ErrorClass = errorClass
        };
    }

    /// <summary>
    /// Compares the real cursor trajectory with a synthetic "perfect computer" trajectory.
    /// The reference moves center-to-center on a straight line with a minimum-jerk timing
    /// profile (zero endpoint velocity/acceleration). This adds temporal path efficiency that
    /// straightness alone cannot see. It is deliberately only one component of proficiency.
    /// </summary>
    private static double IdealPathMatchScore(HitObjectData a, HitObjectData b, List<CursorPoint> path, double actualRadius, double bpm)
    {
        if (path.Count < 3) return 500;
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double distSq = dx * dx + dy * dy;
        if (distSq < 1) return 1000;
        double dist = Math.Sqrt(distSq);

        // A perfect osu! cursor does not need to spend the entire object interval moving.
        // Detect the player's actual movement window first, then compare that movement to a
        // perfectly efficient center-to-center minimum-jerk trajectory. Arrival timing and
        // post-arrival settling are already scored separately.
        int first = 0;
        for (int i = 0; i < path.Count; i++)
        {
            double progress = ((path[i].X - a.X) * dx + (path[i].Y - a.Y) * dy) / distSq;
            double fromA = DistanceTo(path[i], a.X, a.Y);
            if (progress >= .06 || fromA >= Math.Max(3.0, dist * .06)) { first = i; break; }
        }

        int last = path.Count - 1;
        for (int i = Math.Max(first + 2, 0); i < path.Count; i++)
        {
            double progress = ((path[i].X - a.X) * dx + (path[i].Y - a.Y) * dy) / distSq;
            double toB = DistanceTo(path[i], b.X, b.Y);
            if (progress >= .94 && toB <= Math.Max(actualRadius * .72, dist * .08)) { last = i; break; }
        }
        if (last - first < 2) { first = 0; last = path.Count - 1; }

        double startTime = path[first].TimeMs, endTime = path[last].TimeMs;
        double duration = endTime - startTime;
        if (duration <= 0) return 500;

        double cs4Radius = CircleRadius(4);
        // Large circles get only part of their extra radius as trajectory tolerance so low-CS
        // maps do not receive an inflated ideal-path score merely from generous hit geometry.
        double compareRadius = actualRadius <= cs4Radius
            ? actualRadius
            : cs4Radius + (actualRadius - cs4Radius) * .35;

        double speedPressure = Math.Clamp((bpm - 180.0) / 120.0, 0, 1);
        double tolerance = Math.Max(1, compareRadius * (1.0 + .18 * speedPressure));

        double weightedSq = 0, weightSum = 0;
        for (int i = first; i <= last; i++)
        {
            var p = path[i];
            double u = Math.Clamp((p.TimeMs - startTime) / duration, 0, 1);
            double u2 = u * u, u3 = u2 * u;
            double eased = 10 * u3 - 15 * u3 * u + 6 * u3 * u2; // 10u^3-15u^4+6u^5
            double ix = a.X + dx * eased, iy = a.Y + dy * eased;
            double ex = p.X - ix, ey = p.Y - iy;
            double e = Math.Sqrt(ex * ex + ey * ey) / tolerance;
            double w = .58 + .42 * (4 * u * (1 - u));
            weightedSq += w * e * e;
            weightSum += w;
        }

        double rms = Math.Sqrt(weightedSq / Math.Max(.001, weightSum));
        return Clamp1000(1000 * Math.Exp(-.22 * rms * rms));
    }

    private static double FindStableArrivalMs(CursorSampler sampler, HitObjectData target, double radius, double rate)
    {
        double start = target.TimeMs - 140 * rate;
        double end = target.TimeMs + 55 * rate;
        double step = 3 * rate;
        for (double t = start; t <= end; t += step)
        {
            var p = sampler.At(t);
            if (DistanceTo(p, target.X, target.Y) > radius * .68) continue;
            bool stable = true;
            for (double u = t; u <= Math.Min(end, t + 12 * rate); u += 3 * rate)
            {
                var q = sampler.At(u);
                if (DistanceTo(q, target.X, target.Y) > radius * .92) { stable = false; break; }
            }
            if (stable) return (t - target.TimeMs) / rate;
        }
        return 999;
    }

    private static double ArrivalScore(double arrivalMs)
    {
        if (arrivalMs > 200) return 0;
        // A small pre-hit settle is ideal. Late arrival is penalized more strongly than early arrival.
        double delta = arrivalMs + 18;
        double sigma = delta <= 0 ? 62 : 28;
        return Clamp1000(1000 * Math.Exp(-0.5 * Math.Pow(delta / sigma, 2)));
    }

    private static double StabilityScore(CursorSampler sampler, HitObjectData target, double radius, double rate)
    {
        double start = target.TimeMs - 38 * rate;
        double end = target.TimeMs;
        var pts = sampler.Sample(start, end, Math.Max(1, 3 * rate));
        if (pts.Count < 3) return 500;

        double path = 0;
        int reversals = 0;
        (double x, double y)? prevVec = null;
        for (int i = 1; i < pts.Count; i++)
        {
            double vx = pts[i].X - pts[i - 1].X, vy = pts[i].Y - pts[i - 1].Y;
            double mag = Math.Sqrt(vx * vx + vy * vy);
            path += mag;
            if (mag < .35) continue;
            if (prevVec is { } pv)
            {
                double pm = Math.Sqrt(pv.x * pv.x + pv.y * pv.y);
                if (pm > .35)
                {
                    double cos = Math.Clamp((pv.x * vx + pv.y * vy) / (pm * mag), -1, 1);
                    if (cos < 0.15) reversals++;
                }
            }
            prevVec = (vx, vy);
        }
        double net = Dist(pts[0], pts[^1]);
        double excess = Math.Max(0, path - net);
        double excessRatio = excess / Math.Max(1, radius);
        double score = 1000 * Math.Exp(-1.05 * excessRatio) * Math.Exp(-0.10 * reversals);
        return Clamp1000(score);
    }

    private static string ClassifyError(CursorSampler sampler, HitObjectData target, double ux, double uy, double px, double py,
        double radius, double rate, double axial, double lateral, double landingRatio)
    {
        double min = double.PositiveInfinity, max = double.NegativeInfinity;
        for (double t = target.TimeMs - 65 * rate; t <= target.TimeMs; t += 4 * rate)
        {
            var p = sampler.At(t);
            double e = ((p.X - target.X) * ux + (p.Y - target.Y) * uy) / radius;
            min = Math.Min(min, e); max = Math.Max(max, e);
        }
        if (min < -.25 && max > .25) return "Correction";
        if (landingRatio > 1.15 && Math.Abs(lateral) >= Math.Abs(axial) * .75) return "Plain error";
        if (axial > .25 && Math.Abs(axial) > Math.Abs(lateral) * .8) return "Overaim";
        if (axial < -.25 && Math.Abs(axial) > Math.Abs(lateral) * .8) return "Underaim";
        if (Math.Abs(lateral) > .38) return "Lateral";
        return "Clean";
    }

    private static double DecelerationScore(List<CursorPoint> path, double landingScore, string errorClass)
    {
        if (path.Count < 5) return 500;
        var v = new List<double>(path.Count - 1);
        for (int i = 1; i < path.Count; i++)
        {
            double dt = Math.Max(.001, (path[i].TimeMs - path[i - 1].TimeMs) / 1000.0);
            v.Add(Dist(path[i - 1], path[i]) / dt);
        }
        // 3-sample smoothing to reduce replay quantisation noise.
        var s = new double[v.Count];
        for (int i = 0; i < v.Count; i++)
        {
            int lo = Math.Max(0, i - 1), hi = Math.Min(v.Count - 1, i + 1);
            double sum = 0; for (int j = lo; j <= hi; j++) sum += v[j];
            s[i] = sum / (hi - lo + 1);
        }

        int start = Math.Max(0, s.Length / 4);
        int peakIndex = start;
        for (int i = start + 1; i < s.Length; i++) if (s[i] > s[peakIndex]) peakIndex = i;
        double peak = Math.Max(1, s[peakIndex]);
        double reaccel = 0;
        for (int i = peakIndex + 1; i < s.Length; i++) reaccel += Math.Max(0, s[i] - s[i - 1]);
        double reaccelRatio = reaccel / peak;
        int tailN = Math.Min(3, s.Length);
        double terminal = s.Skip(s.Length - tailN).Average() / peak;

        double smoothScore = 1000 * Math.Exp(-2.1 * reaccelRatio);
        double terminalPenalty = Math.Max(0, terminal - .32) / .68;
        double terminalScore = 1000 * Math.Exp(-.85 * terminalPenalty * terminalPenalty);
        double classFactor = errorClass == "Correction" ? .78 : errorClass is "Overaim" or "Underaim" ? .88 : 1.0;
        return Clamp1000((.55 * smoothScore + .25 * terminalScore + .20 * landingScore) * classFactor);
    }

    private static double Challenge(double spacing, double velocity, double angle, double density, double radius, double cs4Radius)
    {
        double spacingFactor = .58 + spacing / 215.0;
        double speedFactor = Math.Pow(Math.Max(.08, velocity / 720.0), .86);
        double reversal = (1 - Math.Cos(angle * Math.PI / 180.0)) / 2.0;
        double angleFactor = 1 + .34 * reversal;
        double densityFactor = 1 + Math.Max(0, density - 2) * .055;
        double csFactor = Math.Pow(cs4Radius / Math.Max(1, radius), .15);
        return 100 * spacingFactor * speedFactor * angleFactor * densityFactor * csFactor;
    }

    private static double VisibleDensity(BeatmapData map, int index, double preemptRawMs)
    {
        long now = map.HitObjects[index].TimeMs;
        int count = 0;
        for (int i = index; i < map.HitObjects.Count; i++)
        {
            long dt = map.HitObjects[i].TimeMs - now;
            if (dt < 0) continue;
            if (dt > preemptRawMs) break;
            if (map.HitObjects[i].Kind != HitObjectKind.Spinner) count++;
        }
        return count;
    }

    private static double JumpAngle(BeatmapData map, int index, int mods)
    {
        if (index < 2) return 0;
        var p = TransformForMods(map.HitObjects[index - 2], mods);
        var a = TransformForMods(map.HitObjects[index - 1], mods);
        var b = TransformForMods(map.HitObjects[index], mods);
        double v1x = a.X - p.X, v1y = a.Y - p.Y;
        double v2x = b.X - a.X, v2y = b.Y - a.Y;
        double m1 = Math.Sqrt(v1x * v1x + v1y * v1y), m2 = Math.Sqrt(v2x * v2x + v2y * v2y);
        if (m1 < 1 || m2 < 1) return 0;
        double cos = Math.Clamp((v1x * v2x + v1y * v2y) / (m1 * m2), -1, 1);
        return Math.Acos(cos) * 180.0 / Math.PI;
    }

    public static (double star, string source) ResolveStarRating(BeatmapData map, int mods)
    {
        if (map.StandardStars.TryGetValue(mods, out double exact))
            return (exact, "exact stable cache");

        // Stable replay masks and stable's star-cache masks do not always line up perfectly.
        // In particular Nightcore replays can include NC+DT while the cache only has the DT form.
        int normalized = ModUtils.NormalizeForStarLookup(mods);
        foreach (int candidate in StarLookupCandidates(normalized))
        {
            if (map.StandardStars.TryGetValue(candidate, out double cached))
                return (cached, candidate == normalized ? "normalized stable cache" : "equivalent stable cache");
        }

        // Local/trainer maps often have only a no-rate star value cached.  Falling all the way
        // back to that value makes a DT/NC 13* map look like an 8* map, which completely breaks
        // Aim Performance.  Estimate the rate-modified SR from the closest base-mod cache entry.
        double rate = ModUtils.ClockRate(mods);
        int withoutRate = normalized & ~(ModUtils.DoubleTime | ModUtils.Nightcore | ModUtils.HalfTime);
        double baseStar = 0;
        string baseSource = "";
        foreach (int candidate in StarLookupCandidates(withoutRate))
        {
            if (map.StandardStars.TryGetValue(candidate, out baseStar))
            {
                baseSource = candidate == 0 ? "NM" : ModUtils.ToShortString(candidate);
                break;
            }
        }
        if (baseStar <= 0 && map.StandardStars.TryGetValue(0, out double nm))
        {
            baseStar = nm;
            baseSource = "NM";
        }

        if (baseStar > 0)
        {
            if (Math.Abs(rate - 1.0) < .001)
                return (baseStar, $"{baseSource} fallback");

            // Empirical stable/lazer DT star growth on aim maps is usually somewhat steeper
            // than the clock-rate itself.  1.15 keeps an 8.1* NM aim map near ~12.9* under DT,
            // while still being conservative enough to serve only as a fallback estimate.
            const double rateExponent = 1.15;
            double estimated = baseStar * Math.Pow(rate, rateExponent);
            return (estimated, $"rate-estimated from {baseStar:0.00}★ {baseSource} cache");
        }

        return (0, "unavailable");
    }

    private static IEnumerable<int> StarLookupCandidates(int mods)
    {
        var seen = new HashSet<int>();
        void Add(List<int> list, int x) { if (seen.Add(x)) list.Add(x); }
        var list = new List<int>();

        Add(list, mods);

        // Canonical NC form for star caches: DT on, NC off.
        if ((mods & ModUtils.Nightcore) != 0)
            Add(list, (mods | ModUtils.DoubleTime) & ~ModUtils.Nightcore);

        // Visibility mods are not always represented in older/local stable cache entries.
        Add(list, mods & ~(ModUtils.Hidden | ModUtils.Flashlight));
        if ((mods & ModUtils.Nightcore) != 0)
            Add(list, ((mods | ModUtils.DoubleTime) & ~ModUtils.Nightcore) & ~(ModUtils.Hidden | ModUtils.Flashlight));

        // Score-only / non-difficulty mods should never prevent a lookup.
        int difficultyOnly = mods & (ModUtils.Easy | ModUtils.HardRock | ModUtils.DoubleTime | ModUtils.Nightcore | ModUtils.HalfTime | ModUtils.TouchDevice);
        Add(list, difficultyOnly);
        if ((difficultyOnly & ModUtils.Nightcore) != 0)
            Add(list, (difficultyOnly | ModUtils.DoubleTime) & ~ModUtils.Nightcore);

        return list;
    }

    private static HitObjectData TransformForMods(HitObjectData o, int mods)
        => new() { Index = o.Index, X = o.X, Y = (mods & ModUtils.HardRock) != 0 ? 384 - o.Y : o.Y, TimeMs = o.TimeMs, Kind = o.Kind };

    public static double CircleRadius(double cs) => 54.4 - 4.48 * cs;
    public static double ApproachPreempt(double ar) => ar < 5 ? 1800 - 120 * ar : 1200 - 150 * (ar - 5);

    public static double EffectiveApproachRate(double baseAr, int mods)
    {
        double ar = ModUtils.ApplyDifficultyMods(baseAr, mods);
        double realPreempt = ApproachPreempt(ar) / ModUtils.ClockRate(mods);
        double effective = realPreempt > 1200 ? (1800 - realPreempt) / 120.0 : 5 + (1200 - realPreempt) / 150.0;
        return Math.Clamp(effective, 0, 11);
    }

    private static double Percentile(IEnumerable<double> values, double p)
    {
        var a = values.OrderBy(x => x).ToArray();
        if (a.Length == 0) return 0;
        double pos = (a.Length - 1) * p;
        int lo = (int)Math.Floor(pos), hi = (int)Math.Ceiling(pos);
        if (lo == hi) return a[lo];
        return a[lo] + (a[hi] - a[lo]) * (pos - lo);
    }

    private static double Clamp1000(double x) => Math.Clamp(x, 0, 1000);
    private static double Dist(CursorPoint a, CursorPoint b) { double x = a.X - b.X, y = a.Y - b.Y; return Math.Sqrt(x * x + y * y); }
    private static double DistanceTo(CursorPoint a, double x, double y) { double dx = a.X - x, dy = a.Y - y; return Math.Sqrt(dx * dx + dy * dy); }
}

public readonly record struct CursorPoint(double TimeMs, double X, double Y);

public sealed class CursorSampler
{
    private readonly ReplayFrame[] frames;
    public CursorSampler(IEnumerable<ReplayFrame> frames) => this.frames = frames.OrderBy(f => f.TimeMs).ToArray();

    public CursorPoint At(double timeMs)
    {
        if (frames.Length == 0) return new CursorPoint(timeMs, 256, 192);
        if (timeMs <= frames[0].TimeMs) return new CursorPoint(timeMs, frames[0].X, frames[0].Y);
        if (timeMs >= frames[^1].TimeMs) return new CursorPoint(timeMs, frames[^1].X, frames[^1].Y);

        int lo = 0, hi = frames.Length - 1;
        while (lo + 1 < hi)
        {
            int mid = (lo + hi) / 2;
            if (frames[mid].TimeMs <= timeMs) lo = mid; else hi = mid;
        }
        var a = frames[lo]; var b = frames[hi];
        double dt = b.TimeMs - a.TimeMs;
        double t = dt <= 0 ? 0 : (timeMs - a.TimeMs) / dt;
        return new CursorPoint(timeMs, a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
    }

    public List<CursorPoint> Sample(double startMs, double endMs, double stepMs)
    {
        var list = new List<CursorPoint>();
        if (endMs <= startMs || frames.Length == 0) return list;
        stepMs = Math.Max(1, stepMs);

        // Sampling is monotonic, so walk the replay frames once instead of doing a binary
        // search for every 3-4 ms sample. This is a hot path during post-play analysis.
        int lo = Array.BinarySearch(frames, new ReplayFrame((long)Math.Floor(startMs), 0, 0, 0), ReplayFrameTimeComparer.Instance);
        if (lo < 0) lo = Math.Max(0, ~lo - 1);
        lo = Math.Min(lo, frames.Length - 1);

        CursorPoint SampleAt(double t)
        {
            if (t <= frames[0].TimeMs) return new CursorPoint(t, frames[0].X, frames[0].Y);
            if (t >= frames[^1].TimeMs) return new CursorPoint(t, frames[^1].X, frames[^1].Y);
            while (lo + 1 < frames.Length && frames[lo + 1].TimeMs <= t) lo++;
            int hi = Math.Min(frames.Length - 1, lo + 1);
            var a = frames[lo]; var b = frames[hi];
            double dt = b.TimeMs - a.TimeMs;
            double u = dt <= 0 ? 0 : (t - a.TimeMs) / dt;
            return new CursorPoint(t, a.X + (b.X - a.X) * u, a.Y + (b.Y - a.Y) * u);
        }

        for (double t = startMs; t < endMs; t += stepMs) list.Add(SampleAt(t));
        list.Add(SampleAt(endMs));
        return list;
    }

    private sealed class ReplayFrameTimeComparer : IComparer<ReplayFrame>
    {
        public static readonly ReplayFrameTimeComparer Instance = new();
        public int Compare(ReplayFrame x, ReplayFrame y) => x.TimeMs.CompareTo(y.TimeMs);
    }
}
