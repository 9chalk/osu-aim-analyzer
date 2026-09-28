namespace OsuAimAnalyzer;

public sealed class DiagnosisCoachControl : UserControl
{
    private readonly FlowLayoutPanel stack = new()
    {
        Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
        AutoScroll = true, BackColor = Theme.Background, Padding = new Padding(6)
    };

    public DiagnosisCoachControl()
    {
        BackColor = Theme.Background;
        Controls.Add(stack);
        Resize += (_, _) => ResizeCards();
    }

    public void SetEmpty(string text)
    {
        stack.Controls.Clear();
        stack.Controls.Add(Card("DIAGNOSIS", text, Theme.Muted));
        ResizeCards();
    }

    public void SetDiagnosis(AimCategoryDiagnosis d)
    {
        stack.SuspendLayout();
        stack.Controls.Clear();
        Color status = d.Status.Contains("Master", StringComparison.OrdinalIgnoreCase) ? Theme.Good
            : d.Status.Contains("Controlled", StringComparison.OrdinalIgnoreCase) ? Theme.Accent2
            : d.Status.Contains("Main", StringComparison.OrdinalIgnoreCase) ? Theme.Warn : Theme.Bad;

        stack.Controls.Add(Card("OBJECTIVE DIAGNOSIS", $"{d.Category.ToUpperInvariant()}  ·  {d.Status}\n{d.ErrorLabel}: {d.ErrorRate:0.0}% across {d.TransitionCount:N0} matching jumps\n\n{d.Summary}", status, 136));

        if (d.Factors.Count > 0)
        {
            var f = d.Factors[0];
            stack.Controls.Add(Card("STRONGEST MEASURABLE SIGNAL", $"{f.Metric}  ·  {f.Confidence:0}% evidence confidence\n{f.Observed} vs controlled range {f.ComfortBand}\n{f.Explanation}", f.Confidence >= 60 ? Theme.Bad : Theme.Warn, 116));
        }

        foreach (var r in d.Routes)
            stack.Controls.Add(Card(r.Title.ToUpperInvariant(), r.Instruction + "\n\n" + r.Why + "\n" + r.Reference, Theme.Accent2, 126));

        stack.Controls.Add(Card("MASTERY TARGET", d.MasteryTarget, Theme.Good, 92));
        stack.Controls.Add(Card("HOW TO READ THIS", "Evidence confidence combines effect strength, separation from your controlled range, and sample support. It is not a literal probability that one variable caused the error.", Theme.Muted, 84));
        ResizeCards();
        stack.ResumeLayout(true);
    }

    private Panel Card(string title, string body, Color accent, int height = 108)
    {
        var panel = new Panel { Height = height, BackColor = Theme.Panel, Margin = new Padding(0, 0, 0, 8), Padding = new Padding(13, 10, 13, 10) };
        panel.Paint += (_, e) =>
        {
            using var p = new Pen(Theme.Border, 1f);
            e.Graphics.DrawRectangle(p, 0, 0, Math.Max(0, panel.Width - 1), Math.Max(0, panel.Height - 1));
            using var a = new SolidBrush(accent);
            e.Graphics.FillRectangle(a, 0, 0, 3, panel.Height);
        };
        var head = new Label { Text = title, Dock = DockStyle.Top, Height = 24, ForeColor = accent, Font = new Font("Segoe UI Semibold", 9.2f), AutoEllipsis = true };
        var text = new Label { Text = body, Dock = DockStyle.Fill, ForeColor = Theme.Text, Font = new Font("Segoe UI", 9.1f), AutoEllipsis = false, Padding = new Padding(0, 3, 0, 0) };
        panel.Controls.Add(text); panel.Controls.Add(head);
        return panel;
    }

    private void ResizeCards()
    {
        int w = Math.Max(240, stack.ClientSize.Width - (stack.VerticalScroll.Visible ? 26 : 10));
        foreach (Control c in stack.Controls) c.Width = w;
    }
}
