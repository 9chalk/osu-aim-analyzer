using System.Drawing.Drawing2D;

namespace OsuAimAnalyzer;

public sealed class RadarChartControl : Control
{
    private List<AimAspectScore> aspects = new();
    private string centerLabel = "aim fingerprint";


    public List<AimAspectScore> Aspects
    {
        get => aspects;
        set { aspects = value ?? new(); Invalidate(); }
    }

    public string CenterLabel
    {
        get => centerLabel;
        set { centerLabel = string.IsNullOrWhiteSpace(value) ? "aim fingerprint" : value; Invalidate(); }
    }

    public RadarChartControl()
    {
        DoubleBuffered = true;
        BackColor = Theme.Panel;
        ForeColor = Theme.Text;
        ResizeRedraw = true;
        Padding = new Padding(24);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.Panel);

        if (aspects.Count < 3)
        {
            using var b = new SolidBrush(Theme.Muted);
            using var f = new Font("Segoe UI", 11f);
            var rect = ClientRectangle;
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("Not enough replay data for aim analysis yet.", f, b, rect, sf);
            return;
        }

        int w = ClientSize.Width;
        int h = ClientSize.Height;
        float cx = w / 2f;
        float cy = h / 2f + 8f;
        float radius = Math.Min(w, h) * 0.29f;

        double highest = aspects.Max(a => a.Score);
        double step = highest <= 1500 ? 250 : highest <= 2600 ? 500 : 1000;
        double axisMax = Math.Max(1000, Math.Ceiling((highest + step * .10) / step) * step);
        int ringCount = Math.Max(4, (int)Math.Round(axisMax / step));

        using var ringPen = new Pen(Color.FromArgb(54, Theme.Text), 1f);
        using var referencePen = new Pen(Color.FromArgb(150, Theme.Accent2), 1.7f);
        using var axisPen = new Pen(Color.FromArgb(70, Theme.Text), 1f);
        using var fillBrush = new SolidBrush(Color.FromArgb(92, Theme.Accent));
        using var pointBrush = new SolidBrush(Theme.Accent);
        using var polyPen = new Pen(Theme.Accent, 2.4f);
        using var labelBrush = new SolidBrush(Theme.Text);
        using var valueBrush = new SolidBrush(Theme.Muted);
        using var labelFont = new Font("Segoe UI Semibold", 9.5f);
        using var valueFont = new Font("Segoe UI", 8.4f);
        using var scaleFont = new Font("Segoe UI", 7.5f);
        using var centerFont = new Font("Segoe UI Semibold", 10f);
        using var centerBrush = new SolidBrush(Theme.Muted);

        for (int ring = 1; ring <= ringCount; ring++)
        {
            double value = ring * step;
            float rr = radius * (float)(value / axisMax);
            DrawPolygon(g, Math.Abs(value - 1000) < .1 ? referencePen : ringPen, null, cx, cy, rr, aspects.Count);
            if (ring == ringCount || Math.Abs(value - 1000) < .1 || ring == 1)
            {
                string label = value == 1000 ? "1000 reference" : value.ToString("0");
                if (Math.Abs(value - 1000) < .1)
                {
                    using var referenceBrush = new SolidBrush(Theme.Accent2);
                    g.DrawString(label, scaleFont, referenceBrush, cx + 4, cy - rr - 12);
                }
                else
                {
                    g.DrawString(label, scaleFont, valueBrush, cx + 4, cy - rr - 12);
                }
            }
        }

        for (int i = 0; i < aspects.Count; i++)
        {
            double ang = -Math.PI / 2 + i * 2 * Math.PI / aspects.Count;
            float x = cx + (float)Math.Cos(ang) * radius;
            float y = cy + (float)Math.Sin(ang) * radius;
            g.DrawLine(axisPen, cx, cy, x, y);

            float lx = cx + (float)Math.Cos(ang) * (radius + 34);
            float ly = cy + (float)Math.Sin(ang) * (radius + 34);
            var rect = new RectangleF(lx - 64, ly - 19, 128, 19);
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(aspects[i].Name.ToLowerInvariant(), labelFont, labelBrush, rect, sf);
            var vrect = new RectangleF(lx - 44, ly + 1, 88, 16);
            g.DrawString($"{aspects[i].Score:0}", valueFont, valueBrush, vrect, sf);
        }

        var points = new PointF[aspects.Count];
        for (int i = 0; i < aspects.Count; i++)
        {
            double ang = -Math.PI / 2 + i * 2 * Math.PI / aspects.Count;
            float rr = radius * (float)(Math.Max(0, aspects[i].Score) / axisMax);
            points[i] = new PointF(cx + (float)Math.Cos(ang) * rr, cy + (float)Math.Sin(ang) * rr);
        }
        g.FillPolygon(fillBrush, points);
        g.DrawPolygon(polyPen, points);
        foreach (var p in points) g.FillEllipse(pointBrush, p.X - 4, p.Y - 4, 8, 8);

        var centerRect = new RectangleF(cx - 95, cy - 24, 190, 48);
        var sf2 = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(centerLabel, centerFont, centerBrush, centerRect, sf2);
    }

    private static void DrawPolygon(Graphics g, Pen pen, Brush? fill, float cx, float cy, float r, int sides)
    {
        var pts = new PointF[sides];
        for (int i = 0; i < sides; i++)
        {
            double ang = -Math.PI / 2 + i * 2 * Math.PI / sides;
            pts[i] = new PointF(cx + (float)Math.Cos(ang) * r, cy + (float)Math.Sin(ang) * r);
        }
        if (fill != null) g.FillPolygon(fill, pts);
        g.DrawPolygon(pen, pts);
    }
}
