using System.Drawing.Drawing2D;

namespace OsuAimAnalyzer;

/// <summary>
/// Three synchronized song-position plots: local proficiency, local aim performance, and
/// telemetry-only map aim difficulty.  Hovering one lane reads all three at the same point.
/// </summary>
public sealed class SongTimelineControl : Control
{
    private IReadOnlyList<SongTimelinePoint> points = Array.Empty<SongTimelinePoint>();
    private double? overallProficiency;
    private double? overallPerformance;
    private int hoverIndex = -1;

    public SongTimelineControl()
    {
        DoubleBuffered = true;
        BackColor = Theme.Panel;
        ForeColor = Theme.Text;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
    }

    public void SetData(IReadOnlyList<SongTimelinePoint>? data, double? playProficiency = null, double? playPerformance = null)
    {
        if (data == null)
        {
            points = Array.Empty<SongTimelinePoint>();
        }
        else
        {
            points = data.Where(p => double.IsFinite(p.Seconds) && double.IsFinite(p.Proficiency)
                                     && double.IsFinite(p.AimPerformance) && double.IsFinite(p.Difficulty))
                         .OrderBy(p => p.Seconds)
                         .ToList();
        }
        overallProficiency = playProficiency is double prof && double.IsFinite(prof) ? prof : null;
        overallPerformance = playPerformance is double perf && double.IsFinite(perf) ? perf : null;
        hoverIndex = -1;
        Invalidate();
    }

    public void Clear() => SetData(Array.Empty<SongTimelinePoint>());

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        hoverIndex = FindNearest(e.X);
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        hoverIndex = -1;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);

        using var labelFont = new Font("Segoe UI Semibold", 8.5f);
        using var tickFont = new Font("Segoe UI", 7.5f);
        using var valueFont = new Font("Segoe UI Semibold", 8f);
        using var mutedBrush = new SolidBrush(Theme.Muted);
        using var textBrush = new SolidBrush(Theme.Text);
        using var gridPen = new Pen(Theme.Grid, 1);
        using var borderPen = new Pen(Theme.Border, 1);

        if (points.Count == 0)
        {
            using var emptyFont = new Font("Segoe UI", 10f);
            TextRenderer.DrawText(g, "No timeline data for this play.", emptyFont, ClientRectangle,
                Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        const float labelWidth = 86;
        const float rightPad = 12;
        const float topPad = 6;
        const float bottomPad = 24;
        const float laneGap = 7;
        float usableH = Math.Max(60, Height - topPad - bottomPad - laneGap * 2);
        float laneH = usableH / 3f;
        float plotLeft = labelWidth;
        float plotRight = Math.Max(plotLeft + 40, Width - rightPad);
        double minX = points[0].Seconds;
        double maxX = points[^1].Seconds;
        if (Math.Abs(maxX - minX) < .001) maxX = minX + 1;

        RectangleF LaneRect(int lane) => new(plotLeft, topPad + lane * (laneH + laneGap), plotRight - plotLeft, laneH);
        float X(double seconds) => plotLeft + (float)((seconds - minX) / (maxX - minX)) * (plotRight - plotLeft);

        var profIncludes = new List<double> { ProductionScoring.ChallengingThreshold, ProductionScoring.ControlledThreshold };
        if (overallProficiency.HasValue) profIncludes.Add(overallProficiency.Value);
        var perfIncludes = overallPerformance.HasValue ? new[] { overallPerformance.Value } : Array.Empty<double>();
        var profRange = Range(points.Select(p => p.Proficiency), 0, 1000, 70, include: profIncludes);
        var perfRange = Range(points.Select(p => p.AimPerformance), 0, double.PositiveInfinity, 55, include: perfIncludes);
        var diffRange = Range(points.Select(p => p.Difficulty), 0, double.PositiveInfinity, .12, include: new[] { 1.0 });

        var lanes = new[]
        {
            new Lane("PROFICIENCY", p => p.Proficiency, profRange.Min, profRange.Max, Theme.Accent, v => v.ToString("0")),
            new Lane("AIM PERF.", p => p.AimPerformance, perfRange.Min, perfRange.Max, Theme.Accent2, v => v.ToString("0")),
            new Lane("MAP DIFF.", p => p.Difficulty, diffRange.Min, diffRange.Max, Theme.Warn, v => v.ToString("0.00") + "×")
        };

        for (int lane = 0; lane < 3; lane++)
        {
            var rect = LaneRect(lane);
            var spec = lanes[lane];
            g.DrawString(spec.Title, labelFont, textBrush, 4, rect.Top + 2);
            // The recent-play timeline is intentionally compact. Drawing separate max and min
            // labels in a ~40 px lane made them collide at common 1080p/DPI layouts. In compact
            // lanes show one readable range line instead; taller diagnostics views retain max/min.
            if (rect.Height < 56)
            {
                string rangeText = lane == 2
                    ? $"{spec.Min:0.00}–{spec.Max:0.00}×"
                    : $"{spec.Min:0}–{spec.Max:0}";
                TextRenderer.DrawText(g, rangeText, tickFont,
                    Rectangle.Round(new RectangleF(5, rect.Top + 19, labelWidth - 9, 18)),
                    Theme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
            else
            {
                g.DrawString(spec.Format(spec.Max), tickFont, mutedBrush, 6, rect.Top + 20);
                g.DrawString(spec.Format(spec.Min), tickFont, mutedBrush, 6, rect.Bottom - 14);
            }

            for (int j = 0; j <= 2; j++)
            {
                float y = rect.Top + rect.Height * j / 2f;
                g.DrawLine(gridPen, rect.Left, y, rect.Right, y);
            }
            for (int j = 0; j <= 4; j++)
            {
                float x = rect.Left + rect.Width * j / 4f;
                g.DrawLine(gridPen, x, rect.Top, x, rect.Bottom);
            }
            g.DrawRectangle(borderPen, rect.X, rect.Y, rect.Width, rect.Height);

            if (lane == 0)
            {
                DrawReferenceLine(g, rect, ProductionScoring.ChallengingThreshold, spec.Min, spec.Max, Color.FromArgb(70, Theme.Warn));
                DrawReferenceLine(g, rect, ProductionScoring.ControlledThreshold, spec.Min, spec.Max, Color.FromArgb(65, Theme.Good));
                if (overallProficiency.HasValue) DrawReferenceLine(g, rect, overallProficiency.Value, spec.Min, spec.Max, Color.FromArgb(115, Theme.Text));
            }
            else if (lane == 1 && overallPerformance.HasValue)
            {
                DrawReferenceLine(g, rect, overallPerformance.Value, spec.Min, spec.Max, Color.FromArgb(115, Theme.Text));
            }
            else if (lane == 2)
            {
                DrawReferenceLine(g, rect, 1.0, spec.Min, spec.Max, Color.FromArgb(100, Theme.Muted));
            }

            DrawCurve(g, rect, spec, X);
        }

        for (int j = 0; j <= 4; j++)
        {
            double sec = minX + (maxX - minX) * j / 4.0;
            string label = FormatTime(sec);
            var size = g.MeasureString(label, tickFont);
            float xx = plotLeft + (plotRight - plotLeft) * j / 4f;
            g.DrawString(label, tickFont, mutedBrush, xx - size.Width / 2, Height - 19);
        }

        if (hoverIndex >= 0 && hoverIndex < points.Count)
        {
            var p = points[hoverIndex];
            float xx = X(p.Seconds);
            using var hoverPen = new Pen(Color.FromArgb(180, Theme.Text), 1) { DashStyle = DashStyle.Dash };
            g.DrawLine(hoverPen, xx, LaneRect(0).Top, xx, LaneRect(2).Bottom);

            for (int lane = 0; lane < 3; lane++)
            {
                var rect = LaneRect(lane);
                var spec = lanes[lane];
                double val = spec.Value(p);
                float yy = MapY(val, spec.Min, spec.Max, rect);
                using var dot = new SolidBrush(spec.Color);
                g.FillEllipse(dot, xx - 3.5f, yy - 3.5f, 7, 7);
            }

            string tip = $"{FormatTime(p.Seconds)}  ·  obj {p.ObjectIndex + 1}\n" +
                         $"Prof {p.Proficiency:0}   Aim {p.AimPerformance:0}   Difficulty {p.Difficulty:0.00}×\n" +
                         $"{p.Bpm:0} BPM   {p.Spacing / Humanize.Cs4CircleDiameter:0.0} circles   {p.ErrorClass}";
            var sz = g.MeasureString(tip, valueFont);
            float bx = Math.Min(plotRight - sz.Width - 14, Math.Max(plotLeft + 4, xx + 10));
            if (bx + sz.Width + 14 > plotRight) bx = Math.Max(plotLeft + 4, xx - sz.Width - 20);
            float by = LaneRect(0).Top + 5;
            var box = new RectangleF(bx, by, sz.Width + 12, sz.Height + 8);
            using var bb = new SolidBrush(Color.FromArgb(238, Theme.Popup));
            using var bp = new Pen(Theme.Border);
            g.FillRoundedRectangle(bb, box, 7);
            g.DrawRoundedRectangle(bp, box, 7);
            g.DrawString(tip, valueFont, textBrush, box.Left + 6, box.Top + 4);
        }

    }

    private void DrawCurve(Graphics g, RectangleF rect, Lane spec, Func<double, float> xMap)
    {
        var clipState = g.Save();
        g.SetClip(rect);
        using var fill = new SolidBrush(Color.FromArgb(19, spec.Color));

        var chunks = new List<List<PointF>>();
        var current = new List<PointF>();
        double medianGap = MedianGap();
        double breakGap = Math.Max(3.5, medianGap * 8.0);

        SongTimelinePoint? previous = null;
        foreach (var p in points)
        {
            if (previous != null && p.Seconds - previous.Seconds > breakGap)
            {
                if (current.Count > 0) chunks.Add(current);
                current = new List<PointF>();
            }
            current.Add(new PointF(xMap(p.Seconds), MapY(spec.Value(p), spec.Min, spec.Max, rect)));
            previous = p;
        }
        if (current.Count > 0) chunks.Add(current);

        foreach (var raw in chunks)
        {
            if (raw.Count >= 2)
            {
                // Presentation smoothing only: exact values remain in the hover readout. The curve is
                // softened to expose section-level shape instead of every object-to-object staircase.
                var smooth = Smooth(raw);
                var area = new List<PointF>(smooth.Count + 2) { new(smooth[0].X, rect.Bottom) };
                area.AddRange(smooth);
                area.Add(new PointF(smooth[^1].X, rect.Bottom));
                g.FillPolygon(fill, area.ToArray());

                using var path = new GraphicsPath();
                if (smooth.Count == 2) path.AddLine(smooth[0], smooth[1]);
                else path.AddCurve(smooth.ToArray(), .20f);
                using (var glow = new Pen(Color.FromArgb(22, spec.Color), 9f) { LineJoin = LineJoin.Round }) g.DrawPath(glow, path);
                using (var bloom = new Pen(Color.FromArgb(55, spec.Color), 4.8f) { LineJoin = LineJoin.Round }) g.DrawPath(bloom, path);
                using (var core = new Pen(Color.FromArgb(238, spec.Color), 2.15f) { LineJoin = LineJoin.Round }) g.DrawPath(core, path);
                using var inner = new Pen(Color.FromArgb(245, Lighten(spec.Color, .50f)), .85f) { LineJoin = LineJoin.Round };
                g.DrawPath(inner, path);
            }
            else if (raw.Count == 1)
            {
                using var dot = new SolidBrush(Color.FromArgb(205, Theme.Muted));
                g.FillEllipse(dot, raw[0].X - 2, raw[0].Y - 2, 4, 4);
            }
        }
        g.Restore(clipState);
    }

    private static List<PointF> Smooth(IReadOnlyList<PointF> raw)
    {
        if (raw.Count <= 3) return raw.ToList();
        int radius = Math.Clamp(raw.Count / 28, 1, 5);
        var output = new List<PointF>(raw.Count);
        for (int i = 0; i < raw.Count; i++)
        {
            int lo = Math.Max(0, i - radius), hi = Math.Min(raw.Count - 1, i + radius);
            double y = 0, weights = 0;
            for (int j = lo; j <= hi; j++)
            {
                double d = Math.Abs(j - i);
                double w = 1.0 / (1.0 + d * d * .7);
                y += raw[j].Y * w;
                weights += w;
            }
            output.Add(new PointF(raw[i].X, (float)(y / Math.Max(.001, weights))));
        }
        return output;
    }

    private static Color Lighten(Color color, float amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        int Blend(int v) => (int)Math.Round(v + (255 - v) * amount);
        return Color.FromArgb(Blend(color.R), Blend(color.G), Blend(color.B));
    }

    private int FindNearest(int mouseX)
    {
        if (points.Count == 0 || Width <= 100) return -1;
        const float left = 78;
        float right = Math.Max(left + 40, Width - 12);
        double frac = Math.Clamp((mouseX - left) / Math.Max(1.0, right - left), 0, 1);
        double target = points[0].Seconds + frac * (points[^1].Seconds - points[0].Seconds);

        int lo = 0, hi = points.Count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (points[mid].Seconds < target) lo = mid + 1; else hi = mid;
        }
        if (lo > 0 && Math.Abs(points[lo - 1].Seconds - target) < Math.Abs(points[lo].Seconds - target)) lo--;
        return lo;
    }

    private double MedianGap()
    {
        if (points.Count < 2) return 1;
        var gaps = new List<double>(points.Count - 1);
        for (int i = 1; i < points.Count; i++)
        {
            double d = points[i].Seconds - points[i - 1].Seconds;
            if (d > 0 && d < 10) gaps.Add(d);
        }
        if (gaps.Count == 0) return 1;
        gaps.Sort();
        return gaps[gaps.Count / 2];
    }

    private static void DrawReferenceLine(Graphics g, RectangleF rect, double value, double min, double max, Color color)
    {
        if (value < min || value > max || max <= min) return;
        float y = MapY(value, min, max, rect);
        using var pen = new Pen(color, 1) { DashStyle = DashStyle.Dot };
        g.DrawLine(pen, rect.Left, y, rect.Right, y);
    }

    private static float MapY(double value, double min, double max, RectangleF rect)
    {
        if (max <= min) return rect.Bottom;
        double f = Math.Clamp((value - min) / (max - min), 0, 1);
        return rect.Bottom - (float)f * rect.Height;
    }

    private static (double Min, double Max) Range(IEnumerable<double> values, double hardMin, double hardMax, double pad, IEnumerable<double>? include = null)
    {
        var a = values.Where(double.IsFinite).ToList();
        if (include != null) a.AddRange(include.Where(double.IsFinite));
        if (a.Count == 0) return (0, 1);
        double min = a.Min(), max = a.Max();
        if (Math.Abs(max - min) < 1e-6) { min -= pad; max += pad; }
        else { min -= pad; max += pad; }
        min = Math.Max(hardMin, min);
        if (double.IsFinite(hardMax)) max = Math.Min(hardMax, max);
        if (max <= min) max = min + Math.Max(1, pad);
        return (min, max);
    }

    private static string FormatTime(double seconds)
    {
        seconds = Math.Max(0, seconds);
        int whole = (int)Math.Round(seconds);
        return $"{whole / 60}:{whole % 60:00}";
    }

    private sealed record Lane(string Title, Func<SongTimelinePoint, double> Value, double Min, double Max, Color Color, Func<double, string> Format);
}
