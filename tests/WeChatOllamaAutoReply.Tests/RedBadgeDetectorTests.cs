using System.Drawing;
using WeChatOllamaAutoReply;
using Xunit;

namespace WeChatOllamaAutoReply.Tests;

public sealed class RedBadgeDetectorTests
{
    [Fact]
    public void Find_DetectsRedCircularComponentInsideSearchArea()
    {
        using var bitmap = new Bitmap(100, 100);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
            graphics.FillEllipse(Brushes.Red, 20, 30, 10, 10);
        }

        var result = RedBadgeDetector.Find(bitmap, new Rectangle(0, 0, 60, 100));

        var point = Assert.Single(result);
        Assert.InRange(point.X, 23, 27);
        Assert.InRange(point.Y, 33, 37);
    }

    [Fact]
    public void Find_IgnoresRedOutsideSearchArea()
    {
        using var bitmap = new Bitmap(100, 100);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
            graphics.FillEllipse(Brushes.Red, 75, 30, 10, 10);
        }

        var result = RedBadgeDetector.Find(bitmap, new Rectangle(0, 0, 60, 100));

        Assert.Empty(result);
    }
}
