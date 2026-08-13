using System.Drawing;
using WeChatOllamaAutoReply;
using Xunit;

namespace WeChatOllamaAutoReply.Tests;

public sealed class RedBadgeDetectorTests
{
    [Theory]
    [InlineData(134, 301, true)]
    [InlineData(131, 301, false)]
    public void IsPlausibleBadgeCenterX_AllowsAvatarEdgeSlotWithScalingTolerance(
        int badgeCenterX,
        int sessionRight,
        bool expected)
    {
        Assert.Equal(expected, RedBadgeDetector.IsPlausibleBadgeCenterX(badgeCenterX, sessionRight));
    }

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
    public void Find_DetectsNumberedBadgeAt125PercentDpi()
    {
        using var bitmap = new Bitmap(200, 120);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var font = new Font(SystemFonts.DefaultFont.FontFamily, 11, FontStyle.Bold))
        {
            graphics.Clear(Color.White);
            graphics.FillEllipse(Brushes.Red, 90, 40, 22, 22);
            graphics.DrawString("3", font, Brushes.White, 96, 42);
        }

        var result = RedBadgeDetector.Find(bitmap, new Rectangle(80, 20, 50, 70));

        var point = Assert.Single(result);
        Assert.InRange(point.X, 98, 104);
        Assert.InRange(point.Y, 48, 54);
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
