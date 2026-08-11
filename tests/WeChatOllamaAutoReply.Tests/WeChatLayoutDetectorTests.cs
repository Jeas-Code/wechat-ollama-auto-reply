using System.Drawing;
using WeChatOllamaAutoReply;
using Xunit;

namespace WeChatOllamaAutoReply.Tests;

public sealed class WeChatLayoutDetectorTests
{
    [Theory]
    [InlineData(750, 651, 293)]
    [InlineData(1368, 1024, 411)]
    public void FindSessionRight_DetectsNeutralPanelBoundary(int width, int height, int expected)
    {
        using var bitmap = new Bitmap(width, height);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var panelBrush = new SolidBrush(Color.FromArgb(237, 238, 240)))
        {
            graphics.Clear(Color.White);
            graphics.FillRectangle(
                panelBrush,
                0,
                0,
                expected,
                height);
            graphics.DrawString("联系人", SystemFonts.DefaultFont, Brushes.Black, expected - 100, height / 3);
        }

        Assert.InRange(WeChatLayoutDetector.FindSessionRight(bitmap), expected - 3, expected + 3);
    }

    [Fact]
    public void FindSessionRight_FailsClosedWithoutReliableBoundary()
    {
        using var bitmap = new Bitmap(750, 651);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
        }

        Assert.Throws<InvalidOperationException>(() => WeChatLayoutDetector.FindSessionRight(bitmap));
    }
}
