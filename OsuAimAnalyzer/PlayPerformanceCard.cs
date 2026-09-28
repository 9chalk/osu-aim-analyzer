using System.Drawing.Drawing2D;

namespace OsuAimAnalyzer;

/// <summary>
/// Compact osu!-inspired play header used by the dashboard inspector.  It deliberately borrows
/// the information hierarchy (banner, large grade/score, badges and right-side stats) without
/// depending on osu!'s web assets or fonts.
/// </summary>
public sealed class PlayPerformanceCard : Control
{
    private PlayRow? play;
    private Image? backgroundImage;

    public PlayPerformanceCard()
    {
        DoubleBuffered = true;
        BackColor = Theme.Panel2;
        ForeColor = Theme.Text;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        Resize += (_, _) => ToolkitUi.RoundControl(this, 10);
    }

    public void SetPlay(PlayRow? value)
    {
        play = value;
        Invalidate();
    }

    public void SetBackgroundImage(Image? image)
    {
        var old = backgroundImage;
        backgroundImage = image;
        old?.Dispose();
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            backgroundImage?.Dispose();
            backgroundImage = null;
        }
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.Panel2);

        const float headerH = 31;
        const float infoH = 48;
        using (var hb = new SolidBrush(Color.FromArgb(29, 39, 46))) g.FillRectangle(hb, 0, 0, Width, headerH);
        using (var f = new Font("Segoe UI Semibold", 10.5f))
            g.DrawString("performance", f, Brushes.White, 36, 6);
        DrawPerformanceIcon(g, 18, 15.5f);

        var infoRect = new RectangleF(0, headerH, Width, infoH);
        using (var ib = new SolidBrush(Color.FromArgb(42, 47, 50))) g.FillRectangle(ib, infoRect);
        var banner = new RectangleF(0, headerH + infoH, Width, Math.Max(0, Height - headerH - infoH));
        if (backgroundImage != null && banner.Width > 1 && banner.Height > 1)
            DrawImageCover(g, backgroundImage, banner);
        else
        {
            using var fallback = new LinearGradientBrush(Rectangle.Round(banner), Theme.Panel2, Color.FromArgb(64, 43, 59), LinearGradientMode.Horizontal);
            g.FillRectangle(fallback, banner);
        }
        using (var shade = new LinearGradientBrush(Rectangle.Round(banner), Color.FromArgb(205, 12, 15, 20), Color.FromArgb(105, 12, 15, 20), LinearGradientMode.Horizontal))
            g.FillRectangle(shade, banner);

        if (play == null)
        {
            using var f = new Font("Segoe UI Semibold", 11f);
            TextRenderer.DrawText(g, "Finish a play, or select one from the table.", f, Rectangle.Round(new RectangleF(0, headerH, Width, Height - headerH)), Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            return;
        }

        var (mainTitle, diff) = SplitMap(play.Map);
        using var titleFont = new Font("Segoe UI Semibold", Width < 520 ? 9.8f : 11.5f);
        using var metaFont = new Font("Segoe UI", 7.8f);
        using var scoreFont = new Font("Segoe UI Light", Width < 520 ? 25f : 30f);
        using var gradeFont = new Font("Segoe UI Black", Width < 520 ? 38f : 46f, FontStyle.Italic);
        using var smallBold = new Font("Segoe UI Semibold", 7.6f);
        using var tierFont = new Font("Segoe UI Semibold", 8.2f);
        using var white = new SolidBrush(Theme.Text);
        using var muted = new SolidBrush(Color.FromArgb(210, Theme.Text));
        using var accentBrush = new SolidBrush(Theme.Accent);

        // Map header strip, matching the information hierarchy of osu!'s performance page.
        var titleRect = new RectangleF(17, headerH + 4, Math.Max(120, Width - 175), 21);
        g.DrawString(mainTitle, titleFont, white, titleRect);
        float badgeY = headerH + 26;
        float starW = DrawPill(g, $"★ {play.StarRating:0.00}", 17, badgeY, Theme.Accent, Color.White, smallBold);
        string meta = string.IsNullOrWhiteSpace(diff) ? $"played by {play.Player}" : $"{diff}  ·  played by {play.Player}";
        g.DrawString(meta, metaFont, muted, 22 + starW, badgeY + 3);
        string when = play.TimestampUtc.ToLocalTime().ToString("MMM d · h:mm tt");
        var whenSize = g.MeasureString(when, metaFont);
        g.DrawString(when, metaFont, muted, Math.Max(18, Width - whenSize.Width - 13), headerH + 6);

        // Banner: grade + large analyzer score on the left, compact result stats on the right.
        float by = banner.Top;
        float modW = !string.Equals(play.ModsText, "NM", StringComparison.OrdinalIgnoreCase)
            ? DrawPill(g, play.ModsText, 18, by + 7, Color.FromArgb(225, 221, 77, 95), Color.White, smallBold)
            : DrawPill(g, "NM", 18, by + 7, Color.FromArgb(210, 75, 80, 92), Color.White, smallBold);
        DrawPill(g, $"AR {play.EffectiveAr:0.0}", 18 + modW + 5, by + 7, Color.FromArgb(205, 75, 80, 92), Color.White, smallBold);

        float gradeY = by + Math.Max(24, banner.Height - 67);
        g.DrawString(play.Grade, gradeFont, white, 18, gradeY - 9);
        float scoreX = Width < 520 ? 94 : 108;
        g.DrawString(play.Proficiency.ToString("0"), scoreFont, white, scoreX, gradeY - 1);
        g.DrawString(Humanize.ProductionScoreShort(play.Proficiency).Split('·').LastOrDefault()?.Trim() ?? play.Zone,
            tierFont, accentBrush, scoreX + 3, gradeY + 34);

        float statsX = Math.Max(scoreX + 136, Width - 214);
        DrawStat(g, "ACCURACY", play.Accuracy.ToString("0.00") + "%", statsX, gradeY + 1, Theme.Good, smallBold, tierFont);
        DrawStat(g, "MISS", play.MissCount.ToString(), statsX + 78, gradeY + 1, play.MissCount == 0 ? Theme.Good : Theme.Bad, smallBold, tierFont);
        DrawStat(g, "AIM", play.RawAimRating.ToString("0"), statsX + 132, gradeY + 1, Theme.Text, smallBold, tierFont);
    }

    private static void DrawStat(Graphics g, string label, string value, float x, float y, Color valueColor, Font small, Font valueFont)
    {
        using var lb = new SolidBrush(Color.FromArgb(195, Theme.Text));
        using var vb = new SolidBrush(valueColor);
        g.DrawString(label, small, lb, x, y);
        g.DrawString(value, valueFont, vb, x, y + 16);
    }

    private static (string Title, string Difficulty) SplitMap(string map)
    {
        int open = map.LastIndexOf('['), close = map.LastIndexOf(']');
        string baseText = open >= 0 ? map[..open].Trim() : map.Trim();
        string diff = open >= 0 && close > open ? map[open..(close + 1)] : "";
        int dash = baseText.IndexOf(" - ", StringComparison.Ordinal);
        if (dash > 0 && dash + 3 < baseText.Length)
        {
            string artist = baseText[..dash].Trim();
            string title = baseText[(dash + 3)..].Trim();
            return ($"{title}  by {artist}", diff);
        }
        return (baseText, diff);
    }

    private static float DrawPill(Graphics g, string text, float x, float y, Color fill, Color textColor, Font font)
    {
        var size = g.MeasureString(text, font);
        float w = size.Width + 14, h = 21;
        var r = new RectangleF(x, y, w, h);
        using var b = new SolidBrush(fill);
        g.FillRoundedRectangle(b, r, h / 2);
        using var tb = new SolidBrush(textColor);
        g.DrawString(text, font, tb, x + 7, y + 3);
        return w;
    }

    private static void DrawPerformanceIcon(Graphics g, float x, float y)
    {
        using var pen = new Pen(Color.White, 1.5f);
        var pts = new[]
        {
            new PointF(x - 7, y - 5), new PointF(x, y - 9), new PointF(x + 7, y - 5),
            new PointF(x + 8, y + 4), new PointF(x, y + 9), new PointF(x - 8, y + 4)
        };
        g.DrawPolygon(pen, pts);
        g.DrawEllipse(pen, x - 2.5f, y - 2.5f, 5, 5);
    }

    private static void DrawImageCover(Graphics g, Image image, RectangleF dest)
    {
        double scale = Math.Max(dest.Width / image.Width, dest.Height / image.Height);
        float srcW = (float)(dest.Width / scale);
        float srcH = (float)(dest.Height / scale);
        float srcX = Math.Max(0, (image.Width - srcW) / 2f);
        float srcY = Math.Max(0, (image.Height - srcH) / 2f);
        g.DrawImage(image, dest, new RectangleF(srcX, srcY, srcW, srcH), GraphicsUnit.Pixel);
    }
}
