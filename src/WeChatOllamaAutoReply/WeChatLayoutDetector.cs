using System.Drawing;

namespace WeChatOllamaAutoReply;

public static class WeChatLayoutDetector
{
    public static int FindSessionRight(Bitmap bitmap)
    {
        var minimumX = (int)(bitmap.Width * 0.20);
        var maximumX = (int)(bitmap.Width * 0.45);
        var minimumY = (int)(bitmap.Height * 0.08);
        var maximumY = (int)(bitmap.Height * 0.94);
        var stepY = Math.Max(3, bitmap.Height / 180);
        var bestX = -1;
        var bestScore = 0d;

        for (var x = minimumX; x <= maximumX; x++)
        {
            var score = 0d;
            var samples = 0;
            for (var y = minimumY; y <= maximumY; y += stepY)
            {
                var left = bitmap.GetPixel(x - 2, y);
                var right = bitmap.GetPixel(x + 2, y);
                if (!IsLightNeutral(left) || !IsLightNeutral(right))
                {
                    continue;
                }

                score += Math.Abs(Brightness(left) - Brightness(right));
                samples++;
            }

            if (samples < 20)
            {
                continue;
            }

            var average = score / samples;
            if (average > bestScore)
            {
                bestScore = average;
                bestX = x;
            }
        }

        if (bestX < 0 || bestScore < 3.0)
        {
            throw new InvalidOperationException("无法可靠识别微信会话列表边界，已禁止点击。");
        }

        return bestX;
    }

    private static bool IsLightNeutral(Color color)
    {
        var maximum = Math.Max(color.R, Math.Max(color.G, color.B));
        var minimum = Math.Min(color.R, Math.Min(color.G, color.B));
        return maximum - minimum <= 18 && Brightness(color) >= 215;
    }

    private static int Brightness(Color color) => (color.R + color.G + color.B) / 3;
}
