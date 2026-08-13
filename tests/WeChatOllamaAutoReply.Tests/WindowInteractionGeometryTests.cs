using System.Drawing;
using WeChatOllamaAutoReply;
using Xunit;

namespace WeChatOllamaAutoReply.Tests;

public sealed class WindowInteractionGeometryTests
{
    [Theory]
    [InlineData(816, 65)]
    [InlineData(1024, 81)]
    public void SessionSearchTop_IncludesFirstConversationRow(int windowHeight, int expected)
    {
        Assert.Equal(expected, WindowInteractionGeometry.GetSessionSearchTop(windowHeight));
    }

    [Fact]
    public void SessionClickPoint_FollowsMovedWindowAndDetectedSessionPanel()
    {
        var first = WindowInteractionGeometry.GetSessionClickPoint(
            new Rectangle(10, 20, 758, 816),
            sessionLeft: 78,
            sessionRight: 301,
            rowY: 441);
        var moved = WindowInteractionGeometry.GetSessionClickPoint(
            new Rectangle(410, 220, 758, 816),
            sessionLeft: 78,
            sessionRight: 301,
            rowY: 441);

        Assert.Equal(new Point(first.X + 400, first.Y + 200), moved);
    }

    [Fact]
    public void InteractionPoints_AdaptToResizedWindowAndNewSessionBoundary()
    {
        var sessionPoint = WindowInteractionGeometry.GetSessionClickPoint(
            new Rectangle(100, 50, 1200, 900),
            sessionLeft: 110,
            sessionRight: 410,
            rowY: 520);
        var inputPoint = WindowInteractionGeometry.GetMessageInputPoint(
            new Rectangle(100, 50, 1200, 900),
            sessionRight: 410);

        Assert.Equal(new Point(390, 594), sessionPoint);
        Assert.Equal(new Point(905, 770), inputPoint);
    }

    [Fact]
    public void SessionClickPoint_UsesPhysicalCaptureCoordinatesAt125PercentDpi()
    {
        var point = WindowInteractionGeometry.GetSessionClickPoint(
            new Rectangle(-3, 631, 758, 816),
            sessionLeft: 78,
            sessionRight: 301,
            rowY: 441);

        Assert.Equal(new Point(208, 1096), point);
    }

    [Fact]
    public void NormalizeVirtualDesktopPoint_SupportsOffsetMultiMonitorDesktop()
    {
        var point = WindowInteractionGeometry.NormalizeVirtualDesktopPoint(
            new Point(208, 1177),
            new Rectangle(-1920, 0, 4480, 1440));

        Assert.Equal(new Point(31136, 53603), point);
    }

    [Fact]
    public void NormalizeVirtualDesktopPoint_RejectsPointOutsideDesktop()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WindowInteractionGeometry.NormalizeVirtualDesktopPoint(
                new Point(2500, 1177),
                new Rectangle(-1920, 0, 3840, 1440)));
    }
}
