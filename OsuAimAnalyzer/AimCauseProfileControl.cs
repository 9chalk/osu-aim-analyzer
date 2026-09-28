using System.Drawing.Drawing2D;

namespace OsuAimAnalyzer;

/// <summary>
/// Human-readable "why did control break?" view. Directional landing errors remain in
/// AimErrorProfileControl; this control interprets trajectory-derived mechanics as likely causes.
/// </summary>
public sealed class AimCauseProfileControl : Control
{
    private AimCauseSummary summary = new();
    private readonly ToolTip tip = new() { AutoPopDelay = 15000, InitialDelay = 250, ReshowDelay = 100 };

    public AimCauseProfileControl()
    {
        DoubleBuffered = true;
        BackColor = Theme.Panel;
        ForeColor = Theme.Text;
        MinimumSize = new Size(300, 120);
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
    }

    public AimCauseSummary Summary => summary;

    public void SetData(IEnumerable<TransitionMetric>? transitions)
    {
        summary = AimErrorDiagnostics.Analyze(transitions);
        AccessibleName = "Likely aim error causes";
        AccessibleDescription = summary.TotalCount == 0
            ? "No trajectory diagnostics available."
            : $"Primary likely cause {summary.PrimaryCause}. {summary.PrimaryExplanation}";
        tip.SetToolTip(this, summary.PrimaryExplanation);
        Invalidate();
    }

    public void Clear() => SetData(null);

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);

        if (summary.TotalCount == 0)
        {
            using var f = new Font("Segoe UI", 10f);
            TextRenderer.DrawText(g, "No trajectory diagnostics for this selection.", f, ClientRectangle,
                Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        using var eyebrow = new Font("Segoe UI Semibold", 8.2f);
        bool compact = Height < 190;
        using var title = new Font("Segoe UI Semibold", compact ? 14.5f : 17f);
        using var body = new Font("Segoe UI", 8.8f);
        using var small = new Font("Segoe UI", 8f);
        using var textBrush = new SolidBrush(Theme.Text);
        using var mutedBrush = new SolidBrush(Theme.Muted);
        using var border = new Pen(Theme.Border, 1f);

        float pad = 13;
        bool wide = Width >= 650;
        float leftW = wide ? Width * .44f : Width;
        var left = new RectangleF(pad, pad, Math.Max(120, leftW - pad * 2), Math.Max(100, Height - pad * 2));
        var right = wide
            ? new RectangleF(leftW + 4, pad, Math.Max(120, Width - leftW - pad - 4), Math.Max(100, Height - pad * 2))
            : RectangleF.Empty;

        g.DrawString("LIKELY MOVEMENT CAUSE", eyebrow, mutedBrush, left.Left, left.Top);
        Color primaryColor = CauseColor(summary.PrimaryCause);
        using var primaryBrush = new SolidBrush(primaryColor);
        g.DrawString(summary.PrimaryCause.ToUpperInvariant(), title, primaryBrush, left.Left, left.Top + 18);

        string rate = summary.ProblemCount == 0
            ? "No recurring control failures detected"
            : $"{summary.ProblemCount:N0}/{summary.TotalCount:N0} jumps show a diagnosable control issue · {summary.ProblemRate:0}%";
        g.DrawString(rate, body, textBrush, new RectangleF(left.Left, left.Top + (compact ? 45 : 52), left.Width, compact ? 24 : 34));

        string meaning = summary.PrimaryCause == AimErrorDiagnostics.Clean
            ? "The stored path, timing, landing, stability and braking metrics do not point to one recurring failure."
            : AimErrorDiagnostics.CauseDescription(summary.PrimaryCause);
        g.DrawString(meaning, body, mutedBrush, new RectangleF(left.Left, left.Top + (compact ? 69 : 88), left.Width, compact ? 38 : (wide ? 58 : 48)));

        if (summary.LongestStreak is { } streak)
        {
            using var streakBrush = new SolidBrush(streak.Count >= 4 ? Theme.Bad : Theme.Warn);
            if (compact)
            {
                string shortStreak = $"REPEAT · {streak.Cause} ×{streak.Count} · obj {streak.StartObject}–{streak.EndObject}";
                g.DrawString(shortStreak, eyebrow, streakBrush, new RectangleF(left.Left, left.Bottom - 20, left.Width, 18));
            }
            else
            {
                string streakText = $"REPEATED PATTERN · {streak.Cause} ×{streak.Count} · objects {streak.StartObject}–{streak.EndObject}";
                g.DrawString(streakText, eyebrow, streakBrush, new RectangleF(left.Left, left.Bottom - 54, left.Width, 18));
                g.DrawString(AimErrorDiagnostics.RepeatedMeaning(streak.Cause), small, mutedBrush,
                    new RectangleF(left.Left, left.Bottom - 36, left.Width, 36));
            }
        }
        else if (!compact && summary.PrimaryCause != AimErrorDiagnostics.Clean)
        {
            g.DrawString("No long same-cause streak. This looks more scattered than section-wide.", small, mutedBrush,
                new RectangleF(left.Left, left.Bottom - 30, left.Width, 24));
        }

        if (!wide) return;

        g.DrawString("BREAKDOWN OF DIAGNOSED JUMPS", eyebrow, mutedBrush, right.Left, right.Top);
        var ranked = AimErrorDiagnostics.OrderedCauses
            .Select(c => (Cause: c, Count: summary.CauseCounts.GetValueOrDefault(c)))
            .Where(x => x.Count > 0)
            .OrderByDescending(x => x.Count)
            .Take(6)
            .ToList();

        if (ranked.Count == 0)
        {
            g.DrawString("Mostly clean control", body, textBrush, right.Left, right.Top + 27);
            return;
        }

        float y = right.Top + 27;
        int denom = Math.Max(1, summary.ProblemCount);
        // Reserve a real footer area. v26 allowed the sixth bar label to paint into the
        // explanatory sentence when the control was short, which is the overlap visible
        // in the user's screenshot.
        float footerTop = right.Bottom - (compact ? 25 : 48);
        foreach (var row in ranked)
        {
            if (y + 22 > footerTop) break;
            double pct = 100.0 * row.Count / denom;
            DrawBar(g, right, ref y, row.Cause, pct, CauseColor(row.Cause), body, small);
        }

        string footer = compact
            ? "Inferred from trajectory + landing; direction is separate."
            : "Cause is inferred from landing, arrival, stability, braking, straightness, ideal-path and signed center error. Direction and cause are intentionally separate.";
        g.DrawString(footer, small, mutedBrush, new RectangleF(right.Left, footerTop + 4, right.Width, Math.Max(18, right.Bottom - footerTop - 4)));
        g.DrawRectangle(border, right.X, right.Y, right.Width, right.Height);
    }

    private static void DrawBar(Graphics g, RectangleF area, ref float y, string name, double pct, Color color, Font labelFont, Font smallFont)
    {
        const float labelW = 128;
        float barLeft = area.Left + labelW;
        float barRight = area.Right - 42;
        float barW = Math.Max(20, barRight - barLeft);
        using var track = new SolidBrush(Theme.Panel2);
        using var fill = new SolidBrush(Color.FromArgb(195, color));
        using var text = new SolidBrush(Theme.Text);
        using var muted = new SolidBrush(Theme.Muted);
        g.DrawString(name, labelFont, text, area.Left, y - 1);
        g.FillRoundedRectangle(track, new RectangleF(barLeft, y + 3, barW, 8), 4);
        float w = barW * (float)Math.Clamp(pct / 100.0, 0, 1);
        if (w > 1) g.FillRoundedRectangle(fill, new RectangleF(barLeft, y + 3, w, 8), 4);
        string value = $"{pct:0}%";
        var size = g.MeasureString(value, smallFont);
        g.DrawString(value, smallFont, muted, area.Right - size.Width, y - 1);
        y += 24;
    }

    public static Color CauseColor(string cause) => cause switch
    {
        AimErrorDiagnostics.ShakeOff => Theme.Warn,
        AimErrorDiagnostics.BrakingOvershoot => Theme.Bad,
        AimErrorDiagnostics.StoppedShort => Theme.Accent2,
        AimErrorDiagnostics.LateAcquisition => Color.FromArgb(185, 135, 255),
        AimErrorDiagnostics.CorrectionLoop => Theme.Accent,
        AimErrorDiagnostics.CurvedApproach => Color.FromArgb(103, 196, 198),
        AimErrorDiagnostics.LateralDrift => Color.FromArgb(219, 170, 86),
        AimErrorDiagnostics.BrakingFailure => Color.FromArgb(232, 128, 104),
        AimErrorDiagnostics.GeneralImprecision => Theme.Muted,
        _ => Theme.Good
    };
}
