using System.Drawing;
using WeChatOllamaAutoReply;
using Xunit;

namespace WeChatOllamaAutoReply.Tests;

public sealed class MutedSessionDetectorTests
{
    [Fact]
    public void IsMuted_DetectsGrayBellShapeInRowActionArea()
    {
        using var bitmap = NewBitmap();
        using (var graphics = Graphics.FromImage(bitmap))
        using (var pen = new Pen(Color.FromArgb(145, 145, 145), 2))
        {
            graphics.DrawEllipse(pen, 266, 226, 8, 8);
            graphics.DrawLine(pen, 264, 235, 276, 235);
            graphics.DrawLine(pen, 264, 224, 276, 238);
        }

        Assert.True(MutedSessionDetector.IsMuted(bitmap, sessionRight: 292, badgeY: 210));
    }

    [Fact]
    public void IsMuted_IgnoresEmptyRowActionArea()
    {
        using var bitmap = NewBitmap();

        Assert.False(MutedSessionDetector.IsMuted(bitmap, sessionRight: 292, badgeY: 210));
    }

    [Fact]
    public void IsMuted_IgnoresColoredAvatarPixelsOutsideActionArea()
    {
        using var bitmap = NewBitmap();
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.FillRectangle(Brushes.DarkRed, 90, 210, 45, 45);
        }

        Assert.False(MutedSessionDetector.IsMuted(bitmap, sessionRight: 292, badgeY: 210));
    }

    private static Bitmap NewBitmap()
    {
        var bitmap = new Bitmap(750, 651);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.FromArgb(247, 247, 247));
        return bitmap;
    }
}
