using System.Drawing.Drawing2D;

namespace OsuAimAnalyzer;

public enum AimMetricKind
{
    Centering,
    Arrival,
    Straightness,
    Stability,
    Braking,
    IdealPath,
    RawAim
}

/// <summary>
/// A deliberately non-graphical summary of one aim mechanic. The number is real analyzer
/// telemetry; the small drawing is a visual metaphor for what the metric means so a player
/// does not need to understand a time-series chart before the score is useful.
/// </summary>
public sealed class AimMetricCardControl : Control
{
    private readonly AimMetricKind kind;
    private double lifetime;
    private double recent;
    private double? auxiliary;
    private string title = "Aim metric";
    private string primary = "—";
    private string secondary = "";
    private string explanation = "";
    private bool capabilityScale;
    private readonly ToolTip tip = new() { AutoPopDelay = 12000, InitialDelay = 250, ReshowDelay = 100 };

    public AimMetricCardControl(AimMetricKind kind)
    {
        this.kind = kind;
        DoubleBuffered = true;
        BackColor = Theme.Panel;
        ForeColor = Theme.Text;
        MinimumSize = new Size(230, 150);
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
    }

    public string Explanation => explanation;
    public string MetricTitle => title;

    public void SetMetric(
        string title,
        double lifetimeScore,
        double recentScore,
        string primaryText,
        string secondaryText,
        string explanation,
        double? auxiliaryValue = null,
        bool capabilityScale = false)
    {
        this.title = title;
        lifetime = double.IsFinite(lifetimeScore) ? Math.Max(0, lifetimeScore) : 0;
        recent = double.IsFinite(recentScore) ? Math.Max(0, recentScore) : 0;
        primary = string.IsNullOrWhiteSpace(primaryText) ? "—" : primaryText;
        secondary = secondaryText ?? "";
        this.explanation = explanation ?? "";
        auxiliary = auxiliaryValue is double a && double.IsFinite(a) ? a : null;
        this.capabilityScale = capabilityScale;
        AccessibleName = title;
        AccessibleDescription = $"{primary}. {secondary}. {explanation}";
        tip.SetToolTip(this, explanation);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.Background);

        var card = new RectangleF(1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3));
        using var fill = new SolidBrush(Theme.Panel);
        using var border = new Pen(Theme.Border, 1f);
        g.FillRoundedRectangle(fill, card, 10);
        g.DrawRoundedRectangle(border, card, 10);

        using var titleFont = new Font("Segoe UI Semibold", 9.25f);
        using var valueFont = new Font("Segoe UI Semibold", 16f);
        using var bodyFont = new Font("Segoe UI", 8.5f);
        using var tinyFont = new Font("Segoe UI", 7.8f);
        using var titleBrush = new SolidBrush(Theme.Text);
        using var mutedBrush = new SolidBrush(Theme.Muted);
        using var goodBrush = new SolidBrush(Theme.Good);
        using var badBrush = new SolidBrush(Theme.Bad);

        float pad = 13;
        g.DrawString(title.ToUpperInvariant(), titleFont, mutedBrush, pad, 10);
        g.DrawString(primary, valueFont, titleBrush, pad, 29);

        double delta = recent - lifetime;
        string deltaText;
        if (kind == AimMetricKind.Centering && auxiliary.HasValue)
            deltaText = Math.Abs(delta) < .5 ? "recent ≈ lifetime" : delta > 0 ? $"recent +{delta:0}" : $"recent {delta:0}";
        else
            deltaText = Math.Abs(delta) < .5 ? "recent ≈ lifetime" : delta > 0 ? $"recent +{delta:0}" : $"recent {delta:0}";
        var deltaBrush = Math.Abs(delta) < .5 ? mutedBrush : delta > 0 ? goodBrush : badBrush;
        g.DrawString(deltaText, tinyFont, deltaBrush, pad, 58);

        var visual = new RectangleF(pad, 78, Math.Min(122, Width * .38f), Math.Max(52, Height - 116));
        DrawMetricVisual(g, visual);

        float textLeft = visual.Right + 14;
        float textWidth = Math.Max(40, Width - textLeft - pad);
        using var sf = new StringFormat { Trimming = StringTrimming.EllipsisWord };
        var secondaryRect = new RectangleF(textLeft, 79, textWidth, 38);
        g.DrawString(secondary, bodyFont, titleBrush, secondaryRect, sf);

        string state = capabilityScale ? Humanize.CapabilityTier(lifetime) : MechanicState(lifetime);
        using var stateBrush = new SolidBrush(StateColor(lifetime, capabilityScale));
        g.DrawString(state, titleFont, stateBrush, textLeft, 119);

        // Lifetime track + recent marker. Unlike a historical graph, this answers one simple
        // question immediately: where are you now relative to your own longer-term level?
        float trackY = Height - 26;
        float trackLeft = pad;
        float trackRight = Width - pad;
        float trackW = Math.Max(20, trackRight - trackLeft);
        double scaleMax = capabilityScale ? Math.Max(1200, Math.Ceiling(Math.Max(lifetime, recent) / 250.0) * 250.0) : 1000.0;
        using var trackBrush = new SolidBrush(Theme.Panel2);
        using var lifeBrush = new SolidBrush(Color.FromArgb(185, Theme.Accent2));
        g.FillRoundedRectangle(trackBrush, new RectangleF(trackLeft, trackY, trackW, 7), 3.5f);
        float lifeW = trackW * (float)Math.Clamp(lifetime / scaleMax, 0, 1);
        if (lifeW > 1) g.FillRoundedRectangle(lifeBrush, new RectangleF(trackLeft, trackY, lifeW, 7), 3.5f);
        float rx = trackLeft + trackW * (float)Math.Clamp(recent / scaleMax, 0, 1);
        using var recentPen = new Pen(Theme.Accent, 2f);
        g.DrawLine(recentPen, rx, trackY - 3, rx, trackY + 10);
        g.DrawString("lifetime", tinyFont, mutedBrush, trackLeft, trackY - 16);
        var rSize = g.MeasureString("recent", tinyFont);
        g.DrawString("recent", tinyFont, mutedBrush, trackRight - rSize.Width, trackY - 16);
    }

    private void DrawMetricVisual(Graphics g, RectangleF r)
    {
        using var guide = new Pen(Color.FromArgb(150, Theme.Border), 1f);
        using var accent = new Pen(Theme.Accent2, 2f);
        using var hot = new Pen(Theme.Accent, 2.2f);
        using var muted = new Pen(Theme.Muted, 1.4f);
        using var good = new Pen(Theme.Good, 2f);
        using var fill = new SolidBrush(Color.FromArgb(42, Theme.Accent2));
        using var point = new SolidBrush(Theme.Accent);

        float cx = r.Left + r.Width / 2f;
        float cy = r.Top + r.Height / 2f;
        double quality = Math.Clamp(lifetime / 1000.0, 0, 1);
        double deficit = 1 - quality;

        switch (kind)
        {
            case AimMetricKind.Centering:
            {
                float rr = Math.Min(r.Width, r.Height) * .34f;
                g.FillEllipse(fill, cx - rr, cy - rr, rr * 2, rr * 2);
                g.DrawEllipse(accent, cx - rr, cy - rr, rr * 2, rr * 2);
                g.DrawLine(guide, cx - rr, cy, cx + rr, cy);
                g.DrawLine(guide, cx, cy - rr, cx, cy + rr);
                double err = Math.Clamp(auxiliary ?? (1 - quality), 0, 1.35);
                float ring = rr * (float)err;
                if (ring > 2)
                {
                    using var ep = new Pen(Theme.Warn, 1.3f) { DashStyle = DashStyle.Dot };
                    g.DrawEllipse(ep, cx - ring, cy - ring, ring * 2, ring * 2);
                }
                g.FillEllipse(point, cx - 3.5f, cy - 3.5f, 7, 7);
                break;
            }
            case AimMetricKind.Arrival:
            {
                float x0 = r.Left + 8, x1 = r.Right - 8;
                g.DrawLine(guide, x0, cy, x1, cy);
                float window = r.Width * .22f;
                using var zone = new SolidBrush(Color.FromArgb(38, Theme.Good));
                g.FillRoundedRectangle(zone, new RectangleF(cx - window / 2, cy - 12, window, 24), 6);
                g.DrawLine(good, cx, cy - 18, cx, cy + 18);
                float spread = 5 + (float)(deficit * r.Width * .24);
                using var spreadBrush = new SolidBrush(Color.FromArgb(105, Theme.Accent));
                g.FillEllipse(spreadBrush, cx - spread, cy - 5, spread * 2, 10);
                break;
            }
            case AimMetricKind.Straightness:
            {
                var a = new PointF(r.Left + 9, r.Bottom - 12);
                var b = new PointF(r.Right - 9, r.Top + 12);
                using var ideal = new Pen(Color.FromArgb(100, Theme.Good), 1.2f) { DashStyle = DashStyle.Dash };
                g.DrawLine(ideal, a, b);
                float bow = (float)(deficit * r.Height * .48);
                using var path = new GraphicsPath();
                path.AddBezier(a, new PointF(r.Left + r.Width * .34f, cy + bow), new PointF(r.Left + r.Width * .66f, cy - bow * .35f), b);
                g.DrawPath(hot, path);
                g.FillEllipse(point, a.X - 3, a.Y - 3, 6, 6);
                g.FillEllipse(point, b.X - 3, b.Y - 3, 6, 6);
                break;
            }
            case AimMetricKind.Stability:
            {
                float rr = Math.Min(r.Width, r.Height) * .28f;
                g.DrawEllipse(accent, cx - rr, cy - rr, rr * 2, rr * 2);
                g.DrawLine(guide, cx - rr, cy, cx + rr, cy);
                g.DrawLine(guide, cx, cy - rr, cx, cy + rr);
                PointF[] offsets =
                {
                    new(-.72f,-.18f), new(-.38f,.57f), new(-.05f,-.66f), new(.31f,.44f), new(.68f,-.32f), new(.15f,.08f), new(-.52f,-.49f)
                };
                float jitter = 3 + (float)(deficit * rr * .85);
                using var jb = new SolidBrush(Color.FromArgb(145, Theme.Warn));
                foreach (var o in offsets)
                    g.FillEllipse(jb, cx + o.X * jitter - 2.2f, cy + o.Y * jitter - 2.2f, 4.4f, 4.4f);
                g.FillEllipse(point, cx - 3, cy - 3, 6, 6);
                break;
            }
            case AimMetricKind.Braking:
            {
                float y = cy;
                float start = r.Left + 7;
                float target = r.Right - 24;
                g.DrawLine(guide, start, y, r.Right - 5, y);
                g.DrawEllipse(accent, target - 8, y - 8, 16, 16);
                using var arrow = new Pen(Theme.Accent, 2.4f) { EndCap = LineCap.ArrowAnchor };
                float stop = target + (float)(deficit * 24);
                g.DrawLine(arrow, start, y, stop, y);
                float h1 = r.Height * .30f, h2 = r.Height * .19f, h3 = r.Height * .10f;
                g.DrawLine(muted, start + r.Width * .25f, y - h1, start + r.Width * .25f, y + h1);
                g.DrawLine(muted, start + r.Width * .48f, y - h2, start + r.Width * .48f, y + h2);
                g.DrawLine(muted, start + r.Width * .68f, y - h3, start + r.Width * .68f, y + h3);
                break;
            }
            case AimMetricKind.IdealPath:
            {
                var a = new PointF(r.Left + 9, r.Bottom - 11);
                var b = new PointF(r.Right - 9, r.Top + 11);
                using var ideal = new Pen(Color.FromArgb(130, Theme.Good), 2f);
                using var idealPath = new GraphicsPath();
                idealPath.AddBezier(a, new PointF(r.Left + r.Width * .34f, r.Bottom - r.Height * .22f), new PointF(r.Left + r.Width * .66f, r.Top + r.Height * .22f), b);
                g.DrawPath(ideal, idealPath);
                float dev = (float)(deficit * r.Height * .36);
                using var actualPath = new GraphicsPath();
                actualPath.AddBezier(a, new PointF(r.Left + r.Width * .28f, cy + dev), new PointF(r.Left + r.Width * .70f, cy - dev * .20f), b);
                g.DrawPath(hot, actualPath);
                break;
            }
            case AimMetricKind.RawAim:
            {
                float rr = Math.Min(r.Width, r.Height) * .38f;
                var arcRect = new RectangleF(cx - rr, cy - rr, rr * 2, rr * 2);
                using var basePen = new Pen(Theme.Panel2, 8f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                using var valuePen = new Pen(Theme.Accent, 8f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawArc(basePen, arcRect, 145, 250);
                double max = Math.Max(1200, Math.Ceiling(Math.Max(lifetime, recent) / 250.0) * 250.0);
                float sweep = 250f * (float)Math.Clamp(lifetime / max, 0, 1);
                g.DrawArc(valuePen, arcRect, 145, sweep);
                // 1000 reference tick.
                double refFrac = Math.Clamp(1000.0 / max, 0, 1);
                double ang = (145 + 250 * refFrac) * Math.PI / 180.0;
                float ix = cx + (float)Math.Cos(ang) * (rr - 7);
                float iy = cy + (float)Math.Sin(ang) * (rr - 7);
                float ox = cx + (float)Math.Cos(ang) * (rr + 6);
                float oy = cy + (float)Math.Sin(ang) * (rr + 6);
                g.DrawLine(good, ix, iy, ox, oy);
                break;
            }
        }
    }

    private static string MechanicState(double score) => score switch
    {
        >= 930 => "exceptionally clean",
        >= 880 => "very controlled",
        >= 820 => "solid control",
        >= 760 => "developing",
        >= 700 => "inconsistent",
        _ => "breaking down"
    };

    private static Color StateColor(double score, bool capability) => capability
        ? score switch
        {
            >= 1050 => Theme.Good,
            >= 750 => Theme.Accent2,
            >= 525 => Theme.Warn,
            _ => Theme.Bad
        }
        : score switch
        {
            >= 880 => Theme.Good,
            >= 820 => Theme.Accent2,
            >= 760 => Theme.Warn,
            _ => Theme.Bad
        };
}
