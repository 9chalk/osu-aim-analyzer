using System.Globalization;

namespace OsuAimAnalyzer;

public sealed record BeatmapTransformResult(BeatmapDocument Document, bool RequiresAudioRendering,
    double AchievedHeadSpacingRatio, string SpacingMeasurement)
{
    public int RepositionedGroups { get; init; }
    public int RelaxedGroups { get; init; }
    public int PreservedOutsideAnchors { get; init; }
}

/// <summary>Pure preview transforms. No source mutation, audio rendering, exporting or star projection.</summary>
public static class BeatmapTransforms
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static double Number(string value)
        => double.TryParse(value, NumberStyles.Float, Invariant, out double n) && double.IsFinite(n)
            ? n : throw new FormatException($"Invalid finite number: {value}");
    private static string Format(double value)
        => double.IsFinite(value) ? value.ToString("G17", Invariant) : throw new FormatException("Transformation overflow.");
    private static string Milliseconds(double value)
    {
        if (!double.IsFinite(value) || value < int.MinValue || value > int.MaxValue) throw new FormatException("Timestamp outside supported range.");
        return Math.Round(value, MidpointRounding.AwayFromZero).ToString("0", Invariant);
    }

    public static BeatmapTransformResult Apply(BeatmapDocument source, PracticeTransformOptions options, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        bool rateChanged = options.SourceClockRate != 1;
        bool spacingChanged = options.SpacingMultiplier != 1;
        bool statsChanged = options.SourceDifficulty != options.GeneratedDifficulty;
        if (!rateChanged && !spacingChanged && !statsChanged) return new(source, false, 1, "Unchanged");
        var analysis = BeatmapParser.ParseDocument(source);
        if (analysis.Mode != 0) throw new NotSupportedException("Practice transforms currently support osu!standard only.");
        var actual = new SerializedDifficulty(analysis.HP, analysis.CS, analysis.AR, analysis.OD);
        if (actual != options.SourceDifficulty) throw new ArgumentException("Source difficulty does not match the document.", nameof(options));
        if (new[] { options.GeneratedDifficulty.Hp, options.GeneratedDifficulty.Cs, options.GeneratedDifficulty.Ar, options.GeneratedDifficulty.Od }.Any(v => v > 10))
            throw new NotSupportedException("Generated serialized difficulty must be in the range 0–10.");
        if (spacingChanged && options.SpacingMultiplier > 1) throw new NotSupportedException("This batch supports spacing reduction only.");
        var replacements = new Dictionary<int, string>();
        var statValues = new Dictionary<string, double>
        {
            ["HPDrainRate"] = options.GeneratedDifficulty.Hp, ["CircleSize"] = options.GeneratedDifficulty.Cs,
            ["ApproachRate"] = options.GeneratedDifficulty.Ar, ["OverallDifficulty"] = options.GeneratedDifficulty.Od
        };
        var seenStats = new HashSet<string>();
        var objects = new List<Geometry>();
        bool redLine = false;
        double lastObjectTime = double.NegativeInfinity;
        for (int index = 0; index < source.Lines.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            var line = source.Lines[index];
            if (!BeatmapDocument.IsContent(line)) continue;
            string text = line.Text.Trim();
            try
            {
                if (line.Section is "General" or "Editor" or "Difficulty")
                {
                    int colon = text.IndexOf(':');
                    if (colon < 1) throw new FormatException("Expected key:value.");
                    string key = text[..colon].Trim(), value = text[(colon + 1)..].Trim();
                    if (line.Section == "General" && key == "Mode" && Number(value) != 0) throw new NotSupportedException("Only osu!standard is supported.");
                    if (line.Section == "Difficulty" && statValues.TryGetValue(key, out double stat))
                    {
                        if (!seenStats.Add(key)) throw new FormatException("Duplicate difficulty key.");
                        if (Number(value) < 0) throw new FormatException("Negative serialized difficulty.");
                        if (statsChanged) replacements[index] = key + ":" + Format(stat);
                    }
                    if (rateChanged && line.Section == "General" && (key is "PreviewTime" or "AudioLeadIn"))
                    {
                        double time = Number(value);
                        if (time >= 0) replacements[index] = key + ":" + Milliseconds(time / options.SourceClockRate);
                    }
                    if (rateChanged && line.Section == "Editor" && key == "Bookmarks" && value.Length > 0)
                        replacements[index] = key + ":" + string.Join(',', value.Split(',').Select(v => Milliseconds(Number(v) / options.SourceClockRate)));
                }
                else if (line.Section == "TimingPoints")
                {
                    var p = BeatmapDocument.Csv(text);
                    if (p.Length < 8 || p[6].Trim() is not ("0" or "1")) throw new FormatException("Expected complete timing point.");
                    double time = Number(p[0]), beat = Number(p[1]);
                    bool red = p[6].Trim() == "1";
                    if (red ? beat <= 0 : beat >= 0) throw new FormatException("Invalid timing-point beat-length sign.");
                    redLine |= red;
                    if (rateChanged)
                    {
                        p[0] = Format(time / options.SourceClockRate);
                        if (red) p[1] = Format(beat / options.SourceClockRate);
                        replacements[index] = string.Join(',', p);
                    }
                }
                else if (line.Section == "HitObjects")
                {
                    var p = BeatmapDocument.Csv(text);
                    var geometry = Geometry.Parse(index, p);
                    double time = Number(p[2]);
                    if (time != Math.Truncate(time) || time < lastObjectTime) throw new FormatException("Hit-object times must be integer and chronological.");
                    lastObjectTime = time;
                    if (geometry.Spinner && Number(p[5]) < time) throw new FormatException("Spinner ends before it starts.");
                    if (rateChanged)
                    {
                        p[2] = Milliseconds(time / options.SourceClockRate);
                        if (geometry.Spinner) p[5] = Milliseconds(Number(p[5]) / options.SourceClockRate);
                    }
                    objects.Add(geometry);
                    if (rateChanged) replacements[index] = string.Join(',', p);
                }
                else if (line.Section == "Events" && rateChanged)
                    replacements[index] = RetimeEvent(text, options.SourceClockRate);
                else if (rateChanged && line.Section is not ("" or "Metadata" or "Colours" or "Events"))
                    throw new NotSupportedException($"Cannot retime unknown section [{line.Section}].");
            }
            catch (Exception e) when (e is FormatException or NotSupportedException)
            {
                throw new NotSupportedException($"Line {index + 1}: {e.Message}", e);
            }
        }
        if (objects.Count == 0 || !redLine) throw new NotSupportedException("A standard map needs hit objects and an uninherited timing point.");
        if (statsChanged && seenStats.Count != 4) throw new NotSupportedException("Explicit HP/CS/AR/OD keys are required for stat edits.");
        var fit = spacingChanged ? ReduceSpacing(objects, options.SpacingMultiplier, replacements, token) : new SpacingFit(1, 0, 0, 0);
        return new(source.Replace(replacements), rateChanged, fit.Ratio,
            "Consecutive head-distance ratio; slider exits use the last control point/repeat parity as a proxy, not evaluated curves.")
        { RepositionedGroups = fit.Repositioned, RelaxedGroups = fit.Relaxed, PreservedOutsideAnchors = fit.OutsideAnchors };
    }

    private static string RetimeEvent(string text, double rate)
    {
        var p = BeatmapDocument.Csv(text);
        switch (p[0].Trim())
        {
            case "0": case "Background":
                if (p.Length < 3 || Number(p[1]) != 0) throw new FormatException("Invalid background event.");
                return text;
            case "1": case "Video":
                if (p.Length < 3) throw new FormatException("Invalid video event.");
                // Retiming the offset alone would leave the video playback speed wrong.
                throw new NotSupportedException("Video needs a media-retiming policy before changing rate.");
            case "2": case "Break":
                if (p.Length != 3 || Number(p[2]) < Number(p[1])) throw new FormatException("Invalid break event.");
                p[1] = Milliseconds(Number(p[1]) / rate); p[2] = Milliseconds(Number(p[2]) / rate); break;
            case "5": case "Sample":
                if (p.Length < 4) throw new FormatException("Invalid sample event.");
                p[1] = Milliseconds(Number(p[1]) / rate); break;
            default: throw new NotSupportedException("Storyboard/unknown events cannot safely be retimed yet.");
        }
        return string.Join(',', p);
    }

    private sealed class Geometry
    {
        public int Line { get; init; }
        public required string[] Fields { get; init; }
        public required List<PointD> Points { get; init; }
        public bool Spinner { get; init; }
        public bool Slider { get; init; }
        public bool OddRepeat { get; init; }
        public double SourceTime { get; init; }
        public PointD Exit => Slider && OddRepeat ? Points[^1] : Points[0];
        public static Geometry Parse(int line, string[] p)
        {
            if (p.Length < 5 || !int.TryParse(p[3], out int type)) throw new FormatException("Invalid hit object.");
            int kind = type & (1 | 2 | 8 | 128);
            if (kind is not (1 or 2 or 8)) throw new NotSupportedException("Unsupported hit-object type.");
            var points = new List<PointD> { new(Number(p[0]), Number(p[1])) };
            bool odd = false;
            if (kind == 2)
            {
                if (p.Length < 8 || !int.TryParse(p[6], out int repeats) || repeats < 1 || Number(p[7]) <= 0) throw new FormatException("Invalid slider length/repeats.");
                var curve = p[5].Split('|');
                if (curve.Length < 2 || curve[0] is not ("B" or "L" or "P" or "C")) throw new NotSupportedException("Unsupported slider curve.");
                foreach (string cp in curve.Skip(1))
                {
                    var xy = cp.Split(':');
                    if (xy.Length != 2) throw new FormatException("Invalid slider control point.");
                    points.Add(new(Number(xy[0]), Number(xy[1])));
                }
                odd = repeats % 2 == 1;
            }
            if (kind == 8 && p.Length < 6) throw new FormatException("Missing spinner end.");
            return new() { Line = line, Fields = p, Points = points, Slider = kind == 2, Spinner = kind == 8, OddRepeat = odd, SourceTime = Number(p[2]) };
        }
    }

    private readonly record struct PointD(double X, double Y)
    {
        public static PointD operator +(PointD a, PointD b) => new(a.X + b.X, a.Y + b.Y);
        public static PointD operator -(PointD a, PointD b) => new(a.X - b.X, a.Y - b.Y);
        public static PointD operator *(PointD a, double scale) => new(a.X * scale, a.Y * scale);
        public double Length => Math.Sqrt(X * X + Y * Y);
    }

    private sealed record SpacingFit(double Ratio, int Repositioned, int Relaxed, int OutsideAnchors);

    private static SpacingFit ReduceSpacing(List<Geometry> objects, double multiplier, Dictionary<int, string> replacements, CancellationToken token)
    {
        PointD? previousExit = null, editedExit = null, previousHead = null, editedHead = null;
        double before = 0, after = 0;
        int repositioned = 0, relaxed = 0, outsideAnchors = 0;
        for (int start = 0; start < objects.Count;)
        {
            token.ThrowIfCancellationRequested();
            if (objects[start].Spinner) { previousExit = editedExit = previousHead = editedHead = null; start++; continue; }
            int end = start + 1;
            // Bound accumulated slider-exit drift; pauses and spinners start new patterns.
            while (end < objects.Count && end - start < 16 && !objects[end].Spinner &&
                objects[end].SourceTime - objects[end - 1].SourceTime <= 1000) end++;
            var group = objects.GetRange(start, end - start);
            foreach (var obj in group)
            {
                var head = obj.Points[0];
                if (head.X < 0 || head.X > 512 || head.Y < 0 || head.Y > 384)
                    throw new NotSupportedException("Source object heads outside the playfield are not supported for spacing edits.");
                outsideAnchors += obj.Points.Skip(1).Count(p => p.X < 0 || p.X > 512 || p.Y < 0 || p.Y > 384);
            }

            (PointD[] Deltas, PointD Min, PointD Max) Layout(double rate)
            {
                var deltas = new PointD[group.Count];
                double minX = double.NegativeInfinity, maxX = double.PositiveInfinity;
                double minY = double.NegativeInfinity, maxY = double.PositiveInfinity;
                for (int i = 0; i < group.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    var obj = group[i];
                    if (i > 0)
                    {
                        var prev = group[i - 1];
                        var desired = prev.Exit + deltas[i - 1] + (obj.Points[0] - prev.Exit) * rate;
                        var delta = desired - obj.Points[0];
                        deltas[i] = new(Math.Round(delta.X, MidpointRounding.AwayFromZero), Math.Round(delta.Y, MidpointRounding.AwayFromZero));
                    }
                    // A whole-slider translation preserves shape/length. Existing off-screen anchors
                    // may stay outside, but cannot extend the object's original envelope further.
                    double left = Math.Min(0, obj.Points.Min(p => p.X)), right = Math.Max(512, obj.Points.Max(p => p.X));
                    double top = Math.Min(0, obj.Points.Min(p => p.Y)), bottom = Math.Max(384, obj.Points.Max(p => p.Y));
                    for (int p = 0; p < obj.Points.Count; p++)
                    {
                        var moved = obj.Points[p] + deltas[i];
                        minX = Math.Max(minX, (p == 0 ? 0 : left) - moved.X);
                        maxX = Math.Min(maxX, (p == 0 ? 512 : right) - moved.X);
                        minY = Math.Max(minY, (p == 0 ? 0 : top) - moved.Y);
                        maxY = Math.Min(maxY, (p == 0 ? 384 : bottom) - moved.Y);
                    }
                }
                return (deltas, new(Math.Ceiling(minX), Math.Ceiling(minY)), new(Math.Floor(maxX), Math.Floor(maxY)));
            }
            bool Fits((PointD[] Deltas, PointD Min, PointD Max) layout) => layout.Min.X <= layout.Max.X && layout.Min.Y <= layout.Max.Y;
            var layout = Layout(multiplier);
            if (!Fits(layout))
            {
                // Keep a known feasible layout throughout the bounded search. Never distort points.
                double lo = multiplier, hi = 1;
                layout = Layout(hi);
                if (!Fits(layout)) throw new NotSupportedException("Source geometry cannot be translated safely.");
                for (int step = 0; step < 32; step++)
                {
                    double mid = (lo + hi) / 2;
                    var candidate = Layout(mid);
                    if (Fits(candidate)) { hi = mid; layout = candidate; } else lo = mid;
                }
                relaxed++;
            }
            var first = group[0].Points[0];
            var wanted = previousExit.HasValue ? editedExit!.Value + (first - previousExit.Value) * multiplier - first : new PointD(0, 0);
            wanted = new(Math.Round(wanted.X, MidpointRounding.AwayFromZero), Math.Round(wanted.Y, MidpointRounding.AwayFromZero));
            var shift = new PointD(Math.Clamp(wanted.X, layout.Min.X, layout.Max.X), Math.Clamp(wanted.Y, layout.Min.Y, layout.Max.Y));
            if (shift != wanted) repositioned++;
            for (int i = 0; i < group.Count; i++)
            {
                var obj = group[i];
                var delta = layout.Deltas[i] + shift;
                var moved = obj.Points.Select(p => p + delta).ToArray();
                if (previousHead.HasValue) { before += (obj.Points[0] - previousHead.Value).Length; after += (moved[0] - editedHead!.Value).Length; }
                obj.Fields[0] = Format(moved[0].X); obj.Fields[1] = Format(moved[0].Y);
                if (obj.Slider) obj.Fields[5] = obj.Fields[5].Split('|')[0] + "|" + string.Join('|', moved.Skip(1).Select(p => Format(p.X) + ":" + Format(p.Y)));
                replacements[obj.Line] = string.Join(',', obj.Fields);
                previousExit = obj.Exit; editedExit = obj.Exit + delta;
                previousHead = obj.Points[0]; editedHead = moved[0];
            }
            start = end;
        }
        return new(before > 0 ? after / before : 1, repositioned, relaxed, outsideAnchors);
    }
}
