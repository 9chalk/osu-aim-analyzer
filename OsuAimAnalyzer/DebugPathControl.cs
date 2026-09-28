using System.Drawing.Drawing2D;

namespace OsuAimAnalyzer;

public sealed class DebugPathControl : Control
{
    private ReplayData? replay;
    private BeatmapData? map;
    private TransitionMetric? metric;

    public DebugPathControl()
    {
        DoubleBuffered = true;
        BackColor = Theme.Panel;
        ForeColor = Theme.Text;
        ResizeRedraw = true;
    }

    public void SetData(ReplayData? replay, BeatmapData? map, TransitionMetric? metric)
    {
        this.replay = replay; this.map = map; this.metric = metric;
        Invalidate();
    }

    public void ClearData()
    {
        replay = null;
        map = null;
        metric = null;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.Panel);
        if (replay == null || map == null || metric == null)
        {
            DrawCentered(g, "Load a replay, then select a transition.");
            return;
        }

        var to0 = map.HitObjects.FirstOrDefault(h => h.Index == metric.ObjectIndex);
        if (to0 == null)
        {
            DrawCentered(g, "Target object was not found in the beatmap.");
            return;
        }
        int pos = map.HitObjects.IndexOf(to0);
        if (pos <= 0) { DrawCentered(g, "No previous object for this transition."); return; }
        var from0 = map.HitObjects[pos - 1];
        var from = Transform(from0, replay.Mods);
        var to = Transform(to0, replay.Mods);
        var sampler = new CursorSampler(replay.Frames);
        double rate = ModUtils.ClockRate(replay.Mods);
        double step = Math.Max(1, 3 * rate);
        var path = sampler.Sample(from.TimeMs, to.TimeMs, step);
        if (path.Count < 2) { DrawCentered(g, "Replay path could not be reconstructed."); return; }

        var rect = RectangleF.Inflate(ClientRectangle, -34, -38);
        float scale = Math.Min(rect.Width / 512f, rect.Height / 384f);
        float ox = rect.Left + (rect.Width - 512f * scale) / 2f;
        float oy = rect.Top + (rect.Height - 384f * scale) / 2f;
        PointF P(double x, double y) => new(ox + (float)x * scale, oy + (float)y * scale);

        using var border = new Pen(Color.FromArgb(85, Theme.Text), 1f);
        g.DrawRectangle(border, ox, oy, 512 * scale, 384 * scale);

        double cs = ModUtils.ApplyDifficultyMods(map.CS, replay.Mods);
        float radius = (float)(AimAnalyzer.CircleRadius(cs) * scale);
        var pa = P(from.X, from.Y); var pb = P(to.X, to.Y);
        using var circlePen = new Pen(Color.FromArgb(180, Theme.Text), 1.5f);
        using var targetPen = new Pen(Theme.Accent, 2.0f);
        g.DrawEllipse(circlePen, pa.X - radius, pa.Y - radius, radius * 2, radius * 2);
        g.DrawEllipse(targetPen, pb.X - radius, pb.Y - radius, radius * 2, radius * 2);

        // Synthetic perfect computer path: straight geometry + minimum jerk time profile.
        var idealPts = new List<PointF>();
        int samples = Math.Max(24, path.Count);
        for (int i = 0; i < samples; i++)
        {
            double u = i / (double)(samples - 1);
            double u2 = u*u, u3 = u2*u;
            double eased = 10*u3 - 15*u3*u + 6*u3*u2;
            idealPts.Add(P(from.X + (to.X-from.X)*eased, from.Y + (to.Y-from.Y)*eased));
        }
        using var idealPen = new Pen(Theme.Good, 2f) { DashStyle = DashStyle.Dash };
        if (idealPts.Count > 1) g.DrawLines(idealPen, idealPts.ToArray());

        using var playerPen = new Pen(Theme.Accent, 2.6f) { LineJoin = LineJoin.Round };
        var playerPts = path.Select(q => P(q.X, q.Y)).ToArray();
        if (playerPts.Length > 1) g.DrawLines(playerPen, playerPts);

        var hit = sampler.At(to.TimeMs);
        var hp = P(hit.X, hit.Y);
        using var hitBrush = new SolidBrush(Theme.Warn);
        g.FillEllipse(hitBrush, hp.X - 5, hp.Y - 5, 10, 10);

        // Equal-time markers make timing/deceleration differences visible even though both
        // trajectories share the same spatial endpoints.
        using var playerDot = new SolidBrush(Color.FromArgb(210, Theme.Accent));
        using var idealDot = new SolidBrush(Color.FromArgb(210, Theme.Good));
        for (int k = 1; k < 10; k++)
        {
            double u = k / 10.0;
            double t = from.TimeMs + (to.TimeMs - from.TimeMs) * u;
            var q = sampler.At(t);
            var qp = P(q.X, q.Y);
            g.FillEllipse(playerDot, qp.X - 2.5f, qp.Y - 2.5f, 5, 5);
            double u2 = u*u, u3 = u2*u;
            double eased = 10*u3 - 15*u3*u + 6*u3*u2;
            var ip = P(from.X + (to.X-from.X)*eased, from.Y + (to.Y-from.Y)*eased);
            g.FillEllipse(idealDot, ip.X - 2.2f, ip.Y - 2.2f, 4.4f, 4.4f);
        }

        // Nearest key-down edge around the target: useful for separating cursor timing from tap timing.
        ReplayFrame? tapFrame = null;
        int previousKeys = 0;
        long bestTapDt = long.MaxValue;
        foreach (var f in replay.Frames)
        {
            bool pressed = f.Keys != 0 && previousKeys == 0;
            previousKeys = f.Keys;
            if (!pressed) continue;
            long dt = Math.Abs(f.TimeMs - to.TimeMs);
            if (dt <= 120 && dt < bestTapDt) { bestTapDt = dt; tapFrame = f; }
        }
        if (tapFrame is ReplayFrame tf)
        {
            var tp = P(tf.X, tf.Y);
            using var tapPen = new Pen(Theme.Accent2, 2f);
            g.DrawEllipse(tapPen, tp.X - 7, tp.Y - 7, 14, 14);
            g.DrawLine(tapPen, tp.X - 5, tp.Y, tp.X + 5, tp.Y);
            g.DrawLine(tapPen, tp.X, tp.Y - 5, tp.X, tp.Y + 5);
        }

        using var fromBrush = new SolidBrush(Theme.Muted);
        using var targetBrush = new SolidBrush(Theme.Accent);
        g.FillEllipse(fromBrush, pa.X - 4, pa.Y - 4, 8, 8);
        g.FillEllipse(targetBrush, pb.X - 4, pb.Y - 4, 8, 8);

        using var font = new Font("Segoe UI", 9f);
        using var small = new Font("Segoe UI", 8f);
        using var text = new SolidBrush(Theme.Text);
        using var muted = new SolidBrush(Theme.Muted);
        g.DrawString($"Object {metric.ObjectIndex} · {metric.Bpm:0} BPM · {Humanize.Spacing(metric.NormalizedSpacing, true)} · {metric.ErrorClass}", font, text, 18, 12);
        using var accentText = new SolidBrush(Theme.Accent);
        using var goodText = new SolidBrush(Theme.Good);
        using var warnText = new SolidBrush(Theme.Warn);
        g.DrawString("PLAYER", small, accentText, 18, ClientSize.Height - 24);
        g.DrawString("IDEAL COMPUTER", small, goodText, 86, ClientSize.Height - 24);
        using var tapText = new SolidBrush(Theme.Accent2);
        g.DrawString("HIT-TIME CURSOR", small, warnText, 204, ClientSize.Height - 24);
        g.DrawString("TAP EDGE", small, tapText, 322, ClientSize.Height - 24);
    }

    private static HitObjectData Transform(HitObjectData h, int mods)
    {
        bool hr = (mods & ModUtils.HardRock) != 0;
        return new HitObjectData { Index=h.Index, X=h.X, Y=hr ? 384-h.Y : h.Y, TimeMs=h.TimeMs, Kind=h.Kind };
    }

    private void DrawCentered(Graphics g, string msg)
    {
        using var b = new SolidBrush(Theme.Muted);
        using var f = new Font("Segoe UI", 11f);
        TextRenderer.DrawText(g, msg, f, ClientRectangle, Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}
