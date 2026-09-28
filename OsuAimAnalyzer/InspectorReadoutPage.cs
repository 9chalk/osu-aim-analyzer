namespace OsuAimAnalyzer;

/// <summary>One scrolling reading surface with full-width, collapsible sections.</summary>
public sealed class InspectorReadoutPage : UserControl
{
    private readonly Label heading;
    private readonly Label subtitle;
    private readonly List<(Panel Card, Button Toggle, TextBox Body, string Title)> sections = new();
    private bool arranging;

    public InspectorReadoutPage(string title, string description, params (string Title, TextBox Body)[] content)
    {
        Dock = DockStyle.Fill; AutoScroll = true; BackColor = Theme.Background;
        heading = new Label { Text = title, AutoSize = false, ForeColor = Theme.Text, Font = new Font("Segoe UI Semibold", 17f) };
        subtitle = new Label { Text = description, AutoSize = false, ForeColor = Theme.Muted, Font = new Font("Segoe UI", 9f) };
        Controls.Add(heading); Controls.Add(subtitle);
        foreach (var (label, body) in content)
        {
            var card = new Panel { BackColor = Theme.Panel };
            var toggle = new Button
            {
                Text = "−  " + label, Tag = true, FlatStyle = FlatStyle.Flat, BackColor = Theme.Panel,
                ForeColor = Theme.Accent2, TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI Semibold", 11f), Cursor = Cursors.Hand,
                AccessibleName = label + ", expanded", TabStop = true
            };
            toggle.FlatAppearance.BorderSize = 0;
            toggle.FlatAppearance.MouseOverBackColor = Theme.Panel2;
            body.Dock = DockStyle.None; body.BorderStyle = BorderStyle.None; body.ScrollBars = ScrollBars.None;
            body.Font = new Font("Segoe UI", 10f); body.BackColor = Theme.Panel; body.ForeColor = Theme.Text;
            body.AccessibleName = label; body.WordWrap = true;
            card.Controls.Add(toggle); card.Controls.Add(body); Controls.Add(card);
            sections.Add((card, toggle, body, label));
            toggle.Click += (_, _) =>
            {
                bool expanded = !(bool)toggle.Tag!;
                toggle.Tag = expanded; body.Visible = expanded;
                toggle.Text = (expanded ? "−  " : "+  ") + label;
                toggle.AccessibleName = label + (expanded ? ", expanded" : ", collapsed");
                PerformLayout();
            };
            body.TextChanged += (_, _) => { AutoScrollPosition = Point.Empty; PerformLayout(); };
        }
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (arranging || heading is null) return;
        arranging = true;
        try
        {
            int S(int value) => (int)Math.Round(value * DeviceDpi / 96.0);
            int width = Math.Max(S(120), ClientSize.Width - SystemInformation.VerticalScrollBarWidth - S(24));
            int offset = AutoScrollPosition.Y;
            heading.SetBounds(S(12), S(12) + offset, width, S(36));
            int descriptionHeight = TextRenderer.MeasureText(subtitle.Text, subtitle.Font, new Size(width, int.MaxValue), TextFormatFlags.WordBreak).Height;
            subtitle.SetBounds(S(12), S(52) + offset, width, descriptionHeight + S(4));
            int y = S(68) + descriptionHeight;
            foreach (var (card, toggle, body, _) in sections)
            {
                int innerWidth = Math.Max(1, width - S(32));
                int height = TextRenderer.MeasureText(body.Text.Length == 0 ? " " : body.Text, body.Font,
                    new Size(Math.Max(1, innerWidth - S(12)), int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix).Height + S(18);
                bool expanded = (bool)toggle.Tag!;
                int cardHeight = expanded ? S(52) + Math.Max(S(48), height) : S(44);
                card.SetBounds(S(12), y + offset, width, cardHeight);
                toggle.SetBounds(S(12), S(5), width - S(24), S(36));
                body.SetBounds(S(16), S(48), innerWidth, Math.Max(S(48), height));
                y += cardHeight + S(12);
            }
            AutoScrollMinSize = new Size(0, y);
            AdjustFormScrollbars(true);
        }
        finally { arranging = false; }
    }
}
