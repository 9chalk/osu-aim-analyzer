using OsuAimAnalyzer;

namespace OsuAimAnalyzer.Tests;

public class InspectorReadoutTests
{
    [Fact]
    public void Readout_ResizesWithoutInnerScrollingAndSupportsCollapse()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var host = new Form { ClientSize = new Size(680, 760), ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-30000, -30000) };
                var summary = new TextBox { Multiline = true, ReadOnly = true, Text = "A · 97.24% · 2 misses\r\nProficiency: 751 · aim performance: 218\r\n\r\nDEMAND\r\n5.20★ · AR 9.2 · 180 BPM\r\n\r\nBest mechanic: arrival timing.\r\nWeakest mechanic: landing stability." };
                var training = new TextBox { Multiline = true, ReadOnly = true, Text = "Good overload map. Errors are appearing, but movement quality is still intact enough for productive practice.\r\n\r\nUse moderate volume and prioritize clean repetitions. Aim tension is elevated." };
                var comparison = new TextBox { Multiline = true, ReadOnly = true, Text = "Versus the immediately previous attempt: proficiency +24 pts, aim performance +12, accuracy +0.8%.\r\n\r\nVersus all 6 earlier runs: proficiency +18 pts. Your previous best remains a useful reference." };
                using var page = new InspectorReadoutPage("Run insights", "How this run went, what to train, and how it compares.", ("At a glance", summary), ("Training focus", training), ("Compared with earlier attempts", comparison));
                host.Controls.Add(page); host.Show();
                foreach (int width in new[] { 680, 430, 900 })
                {
                    host.ClientSize = new Size(width, 760); host.PerformLayout(); page.PerformLayout();
                    Assert.False(page.HorizontalScroll.Visible);
                    foreach (var body in new[] { summary, training, comparison })
                    {
                        Assert.Equal(ScrollBars.None, body.ScrollBars);
                        Assert.True(body.Width >= width - 90);
                        Assert.True(body.GetPositionFromCharIndex(body.TextLength - 1).Y + body.Font.Height <= body.ClientSize.Height);
                    }
                }
                var card = training.Parent!;
                var toggle = card.Controls.OfType<Button>().Single();
                int expanded = card.Height;
                toggle.PerformClick(); Assert.False(training.Visible); Assert.True(card.Height < expanded);
                Assert.Contains("collapsed", toggle.AccessibleName);
                toggle.PerformClick(); Assert.True(training.Visible); Assert.Equal(expanded, card.Height);
                training.Text = string.Join("\r\n\r\n", Enumerable.Repeat("Longer replacement content must remain readable across selection changes.", 20));
                Assert.True(card.Height > expanded);
                Assert.True(page.VerticalScroll.Visible);
                comparison.Focus(); page.ScrollControlIntoView(comparison.Parent); page.PerformLayout();
                Assert.True(comparison.Parent!.Bottom <= page.ClientSize.Height, $"bottom {comparison.Parent!.Bottom}, viewport {page.ClientSize.Height}, scroll {page.AutoScrollPosition}, extent {page.AutoScrollMinSize}");
                training.Text = "Good overload map. Keep repetitions controlled and compare your next attempt.\r\n\r\nUse Practice for a gentler version of this map.";
                host.ClientSize = new Size(680, 760); page.PerformLayout();
                if (Environment.GetEnvironmentVariable("AIM_UI_REVIEW") is { Length: > 0 } image)
                {
                    using var bitmap = new Bitmap(host.ClientSize.Width, host.ClientSize.Height);
                    page.DrawToBitmap(bitmap, page.ClientRectangle); bitmap.Save(image);
                }
                host.Close();
            }
            catch (Exception e) { failure = e; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)));
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
