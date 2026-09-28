using System.Drawing.Drawing2D;

namespace OsuAimAnalyzer;

/// <summary>
/// Normalizes every landing into the incoming-jump coordinate frame so error direction is easy
/// to read across the whole map: left = underaim, right = overaim, vertical = lateral error.
/// AxialError and LateralError are already stored in hit-circle-radius units by AimAnalyzer.
/// </summary>
public sealed class AimErrorProfileControl : Control
{
    private readonly List<TransitionMetric> samples = new();
    private double meanAxial;
    private double meanLateral;
    private double meanRadius;
    private double medianRadius;
    private double p90Radius;
    private Dictionary<string, int> classes = new(StringComparer.OrdinalIgnoreCase);

    public AimErrorProfileControl()
    {
        DoubleBuffered = true;
        BackColor = Theme.Panel;
        ForeColor = Theme.Text;
        MinimumSize = new Size(260, 140);
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
    }

    public void SetData(IEnumerable<TransitionMetric>? transitions)
    {
        samples.Clear();
        if (transitions != null)
        {
            samples.AddRange(transitions.Where(t => double.IsFinite(t.AxialError) && double.IsFinite(t.LateralError)));
        }

        if (samples.Count > 0)
        {
            meanAxial = samples.Average(t => t.AxialError);
            meanLateral = samples.Average(t => t.LateralError);
            var radial = samples.Select(t => Math.Sqrt(t.AxialError * t.AxialError + t.LateralError * t.LateralError))
                .Where(double.IsFinite).OrderBy(x => x).ToArray();
            meanRadius = radial.Length == 0 ? 0 : radial.Average();
            medianRadius = Percentile(radial, .50);
            p90Radius = Percentile(radial, .90);
            classes = samples.GroupBy(t => string.IsNullOrWhiteSpace(t.ErrorClass) ? "Plain error" : t.ErrorClass,
                                      StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            meanAxial = meanLateral = meanRadius = medianRadius = p90Radius = 0;
            classes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }
        Invalidate();
    }

    public void Clear() => SetData(null);

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);

        if (samples.Count == 0)
        {
            using var emptyFont = new Font("Segoe UI", 10f);
            TextRenderer.DrawText(g, "No landing-error data for this play.", emptyFont, ClientRectangle,
                Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        bool compact = Height < 190;
        using var labelFont = new Font("Segoe UI Semibold", 8.5f);
        using var smallFont = new Font("Segoe UI", 8f);
        using var statFont = new Font("Segoe UI Semibold", compact ? 14f : 16f);
        using var bodyFont = new Font("Segoe UI", 8.5f);
        using var mutedBrush = new SolidBrush(Theme.Muted);
        using var textBrush = new SolidBrush(Theme.Text);
        using var borderPen = new Pen(Theme.Border, 1);
        using var gridPen = new Pen(Color.FromArgb(120, Theme.Grid), 1);

        float pad = 8;
        float plotWidth = Width >= 620 ? Math.Min(Width * .53f, Height * 1.55f) : Width * .49f;
        var plot = new RectangleF(pad, pad, Math.Max(120, plotWidth - pad), Math.Max(100, Height - pad * 2));
        float rightLeft = plot.Right + 10;
        var stats = new RectangleF(rightLeft, pad, Math.Max(100, Width - rightLeft - pad), Math.Max(100, Height - pad * 2));

        float cx = plot.Left + plot.Width * .50f;
        float cy = plot.Top + plot.Height * .48f;
        float r = Math.Max(24, Math.Min(plot.Width * .245f, plot.Height * .31f));

        // 1.0R is the target edge. The outer guide is 1.4R so misses just outside the circle
        // still have spatial context without a few wild samples destroying the scale.
        using var outerPen = new Pen(Color.FromArgb(100, Theme.Muted), 1) { DashStyle = DashStyle.Dash };
        g.DrawEllipse(outerPen, cx - 1.4f * r, cy - 1.4f * r, 2.8f * r, 2.8f * r);
        using var circleFill = new SolidBrush(Color.FromArgb(26, Theme.Accent2));
        using var circlePen = new Pen(Color.FromArgb(210, Theme.Accent2), 2);
        g.FillEllipse(circleFill, cx - r, cy - r, 2 * r, 2 * r);
        g.DrawEllipse(circlePen, cx - r, cy - r, 2 * r, 2 * r);
        g.DrawLine(gridPen, cx - 1.42f * r, cy, cx + 1.42f * r, cy);
        g.DrawLine(gridPen, cx, cy - 1.42f * r, cx, cy + 1.42f * r);

        // A faint ring at the average absolute center error makes the summary immediately visual.
        float avgRing = (float)Math.Clamp(meanRadius, 0, 1.4) * r;
        if (avgRing > 2)
        {
            using var avgPen = new Pen(Color.FromArgb(155, Theme.Warn), 1.3f) { DashStyle = DashStyle.Dot };
            g.DrawEllipse(avgPen, cx - avgRing, cy - avgRing, 2 * avgRing, 2 * avgRing);
        }

        foreach (var t in Downsample(samples, 260))
        {
            double ax = t.AxialError;
            double lat = t.LateralError;
            double mag = Math.Sqrt(ax * ax + lat * lat);
            if (mag > 1.42 && mag > 0)
            {
                double scale = 1.42 / mag;
                ax *= scale;
                lat *= scale;
            }
            float x = cx + (float)ax * r;
            float y = cy + (float)lat * r;
            Color color = ErrorColor(t.ErrorClass);
            using var dot = new SolidBrush(Color.FromArgb(92, color));
            float d = mag > 1.42 ? 3.2f : 4.0f;
            g.FillEllipse(dot, x - d / 2, y - d / 2, d, d);
        }

        // Mean signed bias. This is intentionally separate from average radial error: opposite
        // errors can cancel as a bias even while the player is still far from center on average.
        double biasMag = Math.Sqrt(meanAxial * meanAxial + meanLateral * meanLateral);
        double drawAx = meanAxial, drawLat = meanLateral;
        if (biasMag > 1.42 && biasMag > 0)
        {
            double scale = 1.42 / biasMag;
            drawAx *= scale;
            drawLat *= scale;
        }
        float bx = cx + (float)drawAx * r;
        float by = cy + (float)drawLat * r;
        using var biasPen = new Pen(Theme.Accent, 2.4f) { EndCap = LineCap.ArrowAnchor };
        g.DrawLine(biasPen, cx, cy, bx, by);
        using var biasBrush = new SolidBrush(Theme.Accent);
        g.FillEllipse(biasBrush, bx - 4.5f, by - 4.5f, 9, 9);
        using var centerBrush = new SolidBrush(Theme.Text);
        g.FillEllipse(centerBrush, cx - 2.2f, cy - 2.2f, 4.4f, 4.4f);

        TextRenderer.DrawText(g, "UNDER", labelFont,
            Rectangle.Round(new RectangleF(plot.Left, cy - 10, Math.Max(1, cx - r - plot.Left - 4), 20)),
            Theme.Accent2, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, "OVER", labelFont,
            Rectangle.Round(new RectangleF(cx + r + 4, cy - 10, Math.Max(1, plot.Right - (cx + r + 4)), 20)),
            Theme.Bad, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, "LATERAL", smallFont,
            Rectangle.Round(new RectangleF(cx - 45, plot.Top, 90, 18)), Theme.Warn,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, "incoming jump  →  target", smallFont,
            Rectangle.Round(new RectangleF(plot.Left, plot.Bottom - 20, plot.Width, 18)), Theme.Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        // Stats / distribution panel. The recent-play card is short, so compact mode uses
        // a tighter vertical rhythm instead of letting the last rows disappear under the panel.
        g.DrawString("AVG CENTER ERROR · R = CIRCLE RADIUS", labelFont, mutedBrush, stats.Left, stats.Top + 1);
        g.DrawString($"{meanRadius:0.00}R", statFont, textBrush, stats.Left, stats.Top + (compact ? 13 : 15));
        g.DrawString($"median {medianRadius:0.00}R   ·   P90 {p90Radius:0.00}R", bodyFont, mutedBrush, stats.Left, stats.Top + (compact ? 37 : 46));
        g.DrawString(BiasText(), bodyFont, textBrush, stats.Left, stats.Top + (compact ? 53 : 66));
        g.DrawString(DominantErrorText(), bodyFont, mutedBrush, stats.Left, stats.Top + (compact ? 68 : 86));

        float barY = stats.Top + (compact ? 84 : 102);
        string[] ordered = { "Overaim", "Underaim", "Lateral", "Correction", "Plain error", "Clean" };
        int total = Math.Max(1, samples.Count);
        if (compact && stats.Width >= 330)
        {
            // At recent-play height, use two compact columns instead of simply clipping
            // Correction / Plain error / Clean off the bottom of the control.
            float gap = 10;
            float colW = (stats.Width - gap) / 2f;
            var leftBars = new RectangleF(stats.Left, stats.Top, colW, stats.Height);
            var rightBars = new RectangleF(stats.Left + colW + gap, stats.Top, colW, stats.Height);
            float leftY = barY, rightY = barY;
            for (int i = 0; i < ordered.Length; i++)
            {
                string name = ordered[i];
                int count = classes.GetValueOrDefault(name);
                double pct = count * 100.0 / total;
                if (i < 3) DrawBar(g, leftBars, ref leftY, name, pct, ErrorColor(name), labelFont, smallFont);
                else DrawBar(g, rightBars, ref rightY, name, pct, ErrorColor(name), labelFont, smallFont);
            }
        }
        else
        {
            foreach (string name in ordered)
            {
                int count = classes.GetValueOrDefault(name);
                double pct = count * 100.0 / total;
                DrawBar(g, stats, ref barY, name, pct, ErrorColor(name), labelFont, smallFont);
                if (barY > stats.Bottom - 14) break;
            }
        }

        g.DrawRectangle(borderPen, plot.X, plot.Y, plot.Width, plot.Height);
    }

    private string BiasText()
    {
        string axial = Math.Abs(meanAxial) < .015 ? "centered axially"
            : meanAxial > 0 ? $"{meanAxial:0.00}R overaim bias" : $"{Math.Abs(meanAxial):0.00}R underaim bias";
        string lateral = Math.Abs(meanLateral) < .015 ? "little lateral bias" : $"{Math.Abs(meanLateral):0.00}R lateral bias";
        return $"Mean bias: {axial} · {lateral}";
    }

    private string DominantErrorText()
    {
        var errorsOnly = classes.Where(kv => !kv.Key.Equals("Clean", StringComparison.OrdinalIgnoreCase) && kv.Value > 0)
            .OrderByDescending(kv => kv.Value).ToList();
        if (errorsOnly.Count == 0) return "Error type: mostly centered / clean";
        var top = errorsOnly[0];
        return $"Most common classified error: {top.Key.ToLowerInvariant()} ({100.0 * top.Value / Math.Max(1, samples.Count):0.0}%)";
    }

    private static void DrawBar(Graphics g, RectangleF stats, ref float y, string name, double pct, Color color, Font labelFont, Font valueFont)
    {
        const float labelW = 72;
        const float pctW = 42;
        float barLeft = stats.Left + labelW;
        float barRight = Math.Max(barLeft + 20, stats.Right - pctW);
        float barW = barRight - barLeft;
        using var nameBrush = new SolidBrush(Theme.Text);
        g.DrawString(name, labelFont, nameBrush, stats.Left, y - 1);
        using var back = new SolidBrush(Theme.Panel2);
        using var fill = new SolidBrush(Color.FromArgb(190, color));
        var track = new RectangleF(barLeft, y + 3, barW, 8);
        g.FillRoundedRectangle(back, track, 4);
        if (pct > 0) g.FillRoundedRectangle(fill, new RectangleF(track.Left, track.Top, Math.Max(2, track.Width * (float)Math.Clamp(pct / 100.0, 0, 1)), track.Height), 4);
        using var valueBrush = new SolidBrush(Theme.Muted);
        g.DrawString($"{pct:0}%", valueFont, valueBrush, barRight + 5, y - 2);
        y += 16;
    }

    private static IEnumerable<TransitionMetric> Downsample(IReadOnlyList<TransitionMetric> input, int max)
    {
        if (input.Count <= max) return input;
        int step = Math.Max(1, (int)Math.Ceiling(input.Count / (double)max));
        return input.Where((_, i) => i % step == 0);
    }

    private static double Percentile(double[] sorted, double p)
    {
        if (sorted.Length == 0) return 0;
        double pos = (sorted.Length - 1) * Math.Clamp(p, 0, 1);
        int lo = (int)Math.Floor(pos), hi = (int)Math.Ceiling(pos);
        if (lo == hi) return sorted[lo];
        double f = pos - lo;
        return sorted[lo] + (sorted[hi] - sorted[lo]) * f;
    }

    private static Color ErrorColor(string? errorClass)
    {
        return errorClass?.Trim() switch
        {
            "Overaim" => Theme.Bad,
            "Underaim" => Theme.Accent2,
            "Lateral" => Theme.Warn,
            "Correction" => Theme.Accent,
            "Plain error" => Color.FromArgb(205, 147, 245),
            "Clean" => Theme.Good,
            _ => Theme.Muted
        };
    }
}
