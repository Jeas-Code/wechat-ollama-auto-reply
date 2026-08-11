using System.Drawing;

namespace WeChatOllamaAutoReply;

public static class RedBadgeDetector
{
    public static IReadOnlyList<Point> Find(Bitmap bitmap, Rectangle searchArea)
    {
        var area = Rectangle.Intersect(new Rectangle(Point.Empty, bitmap.Size), searchArea);
        var visited = new HashSet<int>();
        var results = new List<Point>();

        for (var y = area.Top; y < area.Bottom; y++)
        {
            for (var x = area.Left; x < area.Right; x++)
            {
                var key = y * bitmap.Width + x;
                if (visited.Contains(key) || !IsBadgeRed(bitmap.GetPixel(x, y)))
                {
                    continue;
                }

                var component = FloodFill(bitmap, area, new Point(x, y), visited);
                if (component.Count is < 8 or > 500)
                {
                    continue;
                }

                var minX = component.Min(point => point.X);
                var maxX = component.Max(point => point.X);
                var minY = component.Min(point => point.Y);
                var maxY = component.Max(point => point.Y);
                var width = maxX - minX + 1;
                var height = maxY - minY + 1;
                var ratio = width / (double)height;
                if (width is >= 3 and <= 32 && height is >= 3 and <= 32 && ratio is >= 0.45 and <= 2.2)
                {
                    results.Add(new Point((minX + maxX) / 2, (minY + maxY) / 2));
                }
            }
        }

        var ordered = results.OrderBy(point => point.Y).ToArray();
        var distinctRows = new List<Point>();
        foreach (var point in ordered)
        {
            if (distinctRows.Count == 0 || Math.Abs(point.Y - distinctRows[^1].Y) > 8)
            {
                distinctRows.Add(point);
            }
        }

        return distinctRows;
    }

    private static List<Point> FloodFill(Bitmap bitmap, Rectangle area, Point start, HashSet<int> visited)
    {
        var queue = new Queue<Point>();
        var component = new List<Point>();
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            var point = queue.Dequeue();
            if (!area.Contains(point))
            {
                continue;
            }

            var key = point.Y * bitmap.Width + point.X;
            if (!visited.Add(key) || !IsBadgeRed(bitmap.GetPixel(point.X, point.Y)))
            {
                continue;
            }

            component.Add(point);
            queue.Enqueue(new Point(point.X - 1, point.Y));
            queue.Enqueue(new Point(point.X + 1, point.Y));
            queue.Enqueue(new Point(point.X, point.Y - 1));
            queue.Enqueue(new Point(point.X, point.Y + 1));
        }

        return component;
    }

    private static bool IsBadgeRed(Color color) =>
        color.R >= 190 && color.G <= 125 && color.B <= 125 &&
        color.R - color.G >= 75 && color.R - color.B >= 75;
}
