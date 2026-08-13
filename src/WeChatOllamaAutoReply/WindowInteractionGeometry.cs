using System.Drawing;

namespace WeChatOllamaAutoReply;

public static class WindowInteractionGeometry
{
    public static int GetSessionSearchTop(int windowHeight) =>
        Math.Clamp((int)(windowHeight * 0.08), 56, Math.Max(56, windowHeight - 120));

    public static Point GetSessionClickPoint(
        Rectangle captureBounds,
        int sessionLeft,
        int sessionRight,
        int rowY)
    {
        if (sessionRight <= sessionLeft || rowY < 0 || rowY >= captureBounds.Height)
        {
            throw new ArgumentOutOfRangeException(nameof(rowY), "会话栏布局或行坐标无效。");
        }

        var relativeX = sessionLeft + (int)((sessionRight - sessionLeft) * 0.60);
        var relativeY = Math.Clamp(rowY + 24, 0, captureBounds.Height - 1);
        return new Point(captureBounds.Left + relativeX, captureBounds.Top + relativeY);
    }

    public static Point GetMessageInputPoint(
        Rectangle captureBounds,
        int sessionRight)
    {
        if (sessionRight <= 0 || sessionRight >= captureBounds.Width)
        {
            throw new ArgumentOutOfRangeException(nameof(sessionRight), "会话栏右边界无效。");
        }

        var conversationWidth = captureBounds.Width - sessionRight;
        var captureX = sessionRight + (int)(conversationWidth * 0.50);
        var captureY = Math.Clamp((int)(captureBounds.Height * 0.80), 120, captureBounds.Height - 50);
        return new Point(captureBounds.Left + captureX, captureBounds.Top + captureY);
    }

    public static Point NormalizeVirtualDesktopPoint(Point physicalPoint, Rectangle virtualScreen)
    {
        if (virtualScreen.Width < 2 || virtualScreen.Height < 2 ||
            !virtualScreen.Contains(physicalPoint))
        {
            throw new ArgumentOutOfRangeException(
                nameof(physicalPoint),
                "点击点不在当前虚拟桌面范围内。");
        }

        return new Point(
            (int)Math.Round((physicalPoint.X - virtualScreen.Left) * 65535d / (virtualScreen.Width - 1)),
            (int)Math.Round((physicalPoint.Y - virtualScreen.Top) * 65535d / (virtualScreen.Height - 1)));
    }
}
