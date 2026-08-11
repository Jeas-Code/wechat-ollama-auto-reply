using System.Drawing;

namespace WeChatOllamaAutoReply;

public static class MutedSessionDetector
{
    public static bool IsMuted(Bitmap bitmap, int sessionRight, int badgeY)
    {
        var left = sessionRight - Math.Max(24, (int)(sessionRight * 0.11));
        var right = sessionRight - Math.Max(10, (int)(sessionRight * 0.045));
        var top = badgeY + Math.Max(11, (int)(bitmap.Height * 0.030));
        var bottom = badgeY + Math.Max(24, (int)(bitmap.Height * 0.065));
        var area = Rectangle.Intersect(
            new Rectangle(Point.Empty, bitmap.Size),
            Rectangle.FromLTRB(left, top, right, bottom));
        if (area.Width < 5 || area.Height < 5)
        {
            return false;
        }

        var pixels = new List<Point>();
        for (var y = area.Top; y < area.Bottom; y++)
        {
            for (var x = area.Left; x < area.Right; x++)
            {
                var color = bitmap.GetPixel(x, y);
                var maximum = Math.Max(color.R, Math.Max(color.G, color.B));
                var minimum = Math.Min(color.R, Math.Min(color.G, color.B));
                var brightness = (color.R + color.G + color.B) / 3;
                if (maximum - minimum <= 22 && brightness is >= 70 and <= 205)
                {
                    pixels.Add(new Point(x, y));
                }
            }
        }

        if (pixels.Count < 8)
        {
            return false;
        }

        var width = pixels.Max(point => point.X) - pixels.Min(point => point.X) + 1;
        var height = pixels.Max(point => point.Y) - pixels.Min(point => point.Y) + 1;
        return width >= 5 && height >= 5;
    }
}
