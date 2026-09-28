using System.Drawing.Drawing2D;

namespace OsuAimAnalyzer;

public readonly record struct GraphPoint(double X, double Y, string Label, DateTime? Time = null);
public sealed record GraphSeries(string Name, IReadOnlyList<GraphPoint> Points, Color Color, float Opacity = 1f, bool ShowPoints = false, Color? PointColor = null);

public sealed class GraphControl : Control
{
    private List<GraphSeries> series = new();
    private string xTitle = "Time";
    private string yTitle = "Proficiency";
    private bool timeAxis;
    private int hoverSeries = -1;
    private int hoverIndex = -1;

    public GraphControl()
    {
        DoubleBuffered = true;
        BackColor = Theme.Panel;
        ForeColor = Theme.Text;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
    }

    public void SetData(IEnumerable<GraphPoint> data, string xAxisTitle, string yAxisTitle, bool isTimeAxis)
        => SetSeries(new[] { new GraphSeries(yAxisTitle, data.Where(p => double.IsFinite(p.X) && double.IsFinite(p.Y)).ToList(), Theme.Accent, 1f, true, Theme.Muted) }, xAxisTitle, yAxisTitle, isTimeAxis);

    public void SetSeries(IEnumerable<GraphSeries> data, string xAxisTitle, string yAxisTitle, bool isTimeAxis)
    {
        series = data.Select(s => s with { Points = s.Points.Where(p => double.IsFinite(p.X) && double.IsFinite(p.Y)).ToList() })
                     .Where(s => s.Points.Count > 0).ToList();
        xTitle = xAxisTitle;
        yTitle = yAxisTitle;
        timeAxis = isTimeAxis;
        hoverSeries = hoverIndex = -1;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        (hoverSeries, hoverIndex) = HitTest(e.Location);
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e); hoverSeries = hoverIndex = -1; Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(BackColor);
        var plot = new RectangleF(68, 28, Math.Max(20, Width - 94), Math.Max(20, Height - 84));
        using var gridPen = new Pen(Theme.Grid, 1);
        using var axisPen = new Pen(Theme.Border, 1.2f);
        using var textBrush = new SolidBrush(Theme.Muted);
        using var mainTextBrush = new SolidBrush(Theme.Text);
        using var font = new Font("Segoe UI", 8.5f);
        using var titleFont = new Font("Segoe UI Semibold", 9f);

        var all = series.SelectMany(s => s.Points).ToList();
        if (all.Count == 0)
        {
            using var emptyFont = new Font("Segoe UI", 11f);
            TextRenderer.DrawText(g, "No analyzed plays match these filters yet.", emptyFont, Rectangle.Round(plot), Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        double minX = all.Min(p => p.X), maxX = all.Max(p => p.X);
        double minY = all.Min(p => p.Y), maxY = all.Max(p => p.Y);
        if (Math.Abs(maxX - minX) < 1e-9) { minX -= .5; maxX += .5; }
        if (Math.Abs(maxY - minY) < 1e-9) { minY -= 1; maxY += 1; }
        double yPad = (maxY - minY) * .08;
        minY = Math.Max(0, minY - yPad); maxY += yPad;

        PointF Map(double x, double y) => new(
            plot.Left + (float)((x - minX) / (maxX - minX) * plot.Width),
            plot.Bottom - (float)((y - minY) / (maxY - minY) * plot.Height));

        int ySteps = yTitle.Contains("Proficiency", StringComparison.OrdinalIgnoreCase)
                  || yTitle.Contains("Control", StringComparison.OrdinalIgnoreCase) ? 8 : 5;
        for (int i = 0; i <= ySteps; i++)
        {
            float yy = plot.Top + plot.Height * i / ySteps;
            g.DrawLine(gridPen, plot.Left, yy, plot.Right, yy);
            double val = maxY - (maxY - minY) * i / ySteps;
            string label = val >= 100 ? val.ToString("0") : val.ToString("0.0");
            g.DrawString(label, font, textBrush, 8, yy - 7);
        }
        for (int i = 0; i <= 5; i++)
        {
            float xx = plot.Left + plot.Width * i / 5f;
            g.DrawLine(gridPen, xx, plot.Top, xx, plot.Bottom);
            double val = minX + (maxX - minX) * i / 5.0;
            string label;
            if (timeAxis)
            {
                try { label = DateTime.FromOADate(val).ToString((maxX - minX) > 10 ? "M/d" : "M/d HH:mm"); }
                catch { label = ""; }
            }
            else label = val >= 100 ? val.ToString("0") : val.ToString("0.0");
            var size = g.MeasureString(label, font);
            g.DrawString(label, font, textBrush, xx - size.Width / 2, plot.Bottom + 8);
        }
        g.DrawRectangle(axisPen, plot.X, plot.Y, plot.Width, plot.Height);

        var seriesState = g.Save();
        g.SetClip(plot);
        foreach (var s in series)
        {
            var sorted = s.Points.OrderBy(p => p.X).ToArray();
            if (sorted.Length >= 2)
            {
                // Visual smoothing is intentionally presentation-only. Hover/readout points remain
                // the exact analyzed values; the curve is a soft moving trend so it reads at a glance.
                int w = Math.Clamp(sorted.Length / 18, 2, 17);
                var trend = new List<PointF>(sorted.Length);
                for (int i = 0; i < sorted.Length; i++)
                {
                    int lo = Math.Max(0, i - w), hi = Math.Min(sorted.Length - 1, i + w);
                    double weighted = 0, weights = 0;
                    for (int j = lo; j <= hi; j++)
                    {
                        double distance = Math.Abs(j - i);
                        double weight = 1.0 / (1.0 + distance * distance * .45);
                        weighted += sorted[j].Y * weight;
                        weights += weight;
                    }
                    trend.Add(Map(sorted[i].X, weighted / Math.Max(.0001, weights)));
                }
                DrawBloomCurve(g, trend, s.Color, s.Opacity);
            }
            if (s.ShowPoints)
            {
                Color pointColor = s.PointColor ?? Color.FromArgb(190, Theme.Muted);
                using var brush = new SolidBrush(Color.FromArgb((int)(185 * Math.Clamp(s.Opacity, .15f, 1f)), pointColor));
                foreach (var p in s.Points)
                {
                    var pt = Map(p.X, p.Y);
                    g.FillEllipse(brush, pt.X - 2.4f, pt.Y - 2.4f, 4.8f, 4.8f);
                }
            }
        }
        g.Restore(seriesState);

        g.DrawString(yTitle, titleFont, mainTextBrush, plot.Left, 4);
        var xSize = g.MeasureString(xTitle, titleFont);
        g.DrawString(xTitle, titleFont, mainTextBrush, plot.Left + (plot.Width - xSize.Width) / 2, Height - 23);

        float lx = plot.Right - 8;
        foreach (var s in series.AsEnumerable().Reverse())
        {
            var sz = g.MeasureString(s.Name, font);
            lx -= sz.Width + 26;
            using var pen = new Pen(Color.FromArgb((int)(255 * Math.Clamp(s.Opacity, .15f, 1f)), s.Color), 3);
            g.DrawLine(pen, lx, 14, lx + 14, 14);
            g.DrawString(s.Name, font, textBrush, lx + 18, 7);
        }

        if (hoverSeries >= 0 && hoverSeries < series.Count && hoverIndex >= 0 && hoverIndex < series[hoverSeries].Points.Count)
        {
            var hp = series[hoverSeries].Points[hoverIndex]; var pos = Map(hp.X, hp.Y);
            string tip = $"{series[hoverSeries].Name}: {hp.Y:0.0}\n{hp.Label}";
            var sz = g.MeasureString(tip, font);
            var box = new RectangleF(Math.Min(plot.Right - sz.Width - 16, pos.X + 10), Math.Max(plot.Top, pos.Y - sz.Height - 14), sz.Width + 12, sz.Height + 8);
            using var boxBrush = new SolidBrush(Theme.Popup);
            using var boxPen = new Pen(Theme.Border);
            g.FillRoundedRectangle(boxBrush, box, 6);
            g.DrawRoundedRectangle(boxPen, box, 6);
            g.DrawString(tip, font, mainTextBrush, box.Left + 6, box.Top + 4);
        }
    }

    private static void DrawBloomCurve(Graphics g, IReadOnlyList<PointF> points, Color color, float opacity)
    {
        if (points.Count < 2) return;
        using var path = new GraphicsPath();
        if (points.Count == 2) path.AddLine(points[0], points[1]);
        else path.AddCurve(points.ToArray(), .22f);

        int a = (int)(255 * Math.Clamp(opacity, .15f, 1f));
        using (var glow = new Pen(Color.FromArgb(Math.Max(8, a / 11), color), 10f) { LineJoin = LineJoin.Round }) g.DrawPath(glow, path);
        using (var bloom = new Pen(Color.FromArgb(Math.Max(16, a / 5), color), 5.4f) { LineJoin = LineJoin.Round }) g.DrawPath(bloom, path);
        using (var core = new Pen(Color.FromArgb(a, color), 2.3f) { LineJoin = LineJoin.Round }) g.DrawPath(core, path);
        using var inner = new Pen(Color.FromArgb(Math.Min(255, a), Lighten(color, .52f)), .9f) { LineJoin = LineJoin.Round };
        g.DrawPath(inner, path);
    }

    private static Color Lighten(Color color, float amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        int Blend(int v) => (int)Math.Round(v + (255 - v) * amount);
        return Color.FromArgb(Blend(color.R), Blend(color.G), Blend(color.B));
    }

    private (int seriesIndex, int pointIndex) HitTest(Point p)
    {
        var all = series.SelectMany(s => s.Points).ToList();
        if (all.Count == 0) return (-1, -1);
        var plot = new RectangleF(68, 28, Math.Max(20, Width - 94), Math.Max(20, Height - 84));
        double minX = all.Min(q => q.X), maxX = all.Max(q => q.X), minY = all.Min(q => q.Y), maxY = all.Max(q => q.Y);
        if (Math.Abs(maxX - minX) < 1e-9) { minX -= .5; maxX += .5; }
        if (Math.Abs(maxY - minY) < 1e-9) { minY -= 1; maxY += 1; }
        double yPad = (maxY - minY) * .08; minY = Math.Max(0, minY - yPad); maxY += yPad;
        int bs = -1, bi = -1; double bestD = 100;
        for (int si = 0; si < series.Count; si++)
        for (int i = 0; i < series[si].Points.Count; i++)
        {
            var q = series[si].Points[i];
            double x = plot.Left + (q.X - minX) / (maxX - minX) * plot.Width;
            double y = plot.Bottom - (q.Y - minY) / (maxY - minY) * plot.Height;
            double d = Math.Sqrt((x - p.X) * (x - p.X) + (y - p.Y) * (y - p.Y));
            if (d < bestD) { bestD = d; bs = si; bi = i; }
        }
        return bestD <= 9 ? (bs, bi) : (-1, -1);
    }
}

internal static class GraphicsExtensions
{
    public static void FillRoundedRectangle(this Graphics g, Brush brush, RectangleF r, float radius)
    {
        using var path = RoundedPath(r, radius); g.FillPath(brush, path);
    }
    public static void DrawRoundedRectangle(this Graphics g, Pen pen, RectangleF r, float radius)
    {
        using var path = RoundedPath(r, radius); g.DrawPath(pen, path);
    }
    private static GraphicsPath RoundedPath(RectangleF r, float radius)
    {
        float d = radius * 2;
        var p = new GraphicsPath();
        p.AddArc(r.Left, r.Top, d, d, 180, 90); p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90); p.CloseFigure(); return p;
    }
}
