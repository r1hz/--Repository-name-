using System.Windows;

namespace ResetExplode.DiskAnalyzer;

/// <summary>Squarified treemap: agrupa proporções semelhantes para produzir retângulos próximos de quadrados.</summary>
public static class SquarifiedTreemapLayout
{
    private sealed record AreaItem(TreemapTile Tile, double Area);

    public static IReadOnlyList<TreemapLayoutItem> Arrange(IReadOnlyList<TreemapTile> tiles, Rect bounds)
    {
        if (tiles.Count == 0 || bounds.Width <= 0 || bounds.Height <= 0) return Array.Empty<TreemapLayoutItem>();

        double total = tiles.Where(tile => tile.SizeBytes > 0).Sum(tile => (double)tile.SizeBytes);
        if (total <= 0) return Array.Empty<TreemapLayoutItem>();

        double availableArea = bounds.Width * bounds.Height;
        var remainingItems = tiles.Where(tile => tile.SizeBytes > 0)
            .OrderByDescending(tile => tile.SizeBytes)
            .Select(tile => new AreaItem(tile, availableArea * tile.SizeBytes / total))
            .ToList();
        var arranged = new List<TreemapLayoutItem>(remainingItems.Count);
        Rect remaining = bounds;
        int next = 0;

        while (next < remainingItems.Count && remaining.Width > 0 && remaining.Height > 0)
        {
            double shortSide = Math.Min(remaining.Width, remaining.Height);
            var row = new List<AreaItem>();
            while (next < remainingItems.Count)
            {
                AreaItem candidate = remainingItems[next];
                if (row.Count == 0 || WorstAspect(row.Append(candidate), shortSide) <= WorstAspect(row, shortSide))
                {
                    row.Add(candidate);
                    next++;
                }
                else break;
            }

            LayoutRow(row, ref remaining, arranged);
        }

        return arranged;
    }

    private static double WorstAspect(IEnumerable<AreaItem> row, double shortSide)
    {
        double sum = 0, smallest = double.MaxValue, largest = 0;
        foreach (var item in row)
        {
            sum += item.Area;
            smallest = Math.Min(smallest, item.Area);
            largest = Math.Max(largest, item.Area);
        }
        if (sum <= 0 || smallest <= 0 || shortSide <= 0) return double.PositiveInfinity;
        double sideSquared = shortSide * shortSide;
        double sumSquared = sum * sum;
        return Math.Max(sideSquared * largest / sumSquared, sumSquared / (sideSquared * smallest));
    }

    private static void LayoutRow(List<AreaItem> row, ref Rect remaining, List<TreemapLayoutItem> output)
    {
        double rowArea = row.Sum(item => item.Area);
        if (remaining.Width >= remaining.Height)
        {
            double stripWidth = Math.Min(remaining.Width, rowArea / remaining.Height);
            double y = remaining.Y;
            for (int index = 0; index < row.Count; index++)
            {
                double height = index == row.Count - 1
                    ? remaining.Bottom - y
                    : Math.Min(remaining.Bottom - y, row[index].Area / stripWidth);
                output.Add(new TreemapLayoutItem(row[index].Tile, new Rect(remaining.X, y, stripWidth, Math.Max(0, height))));
                y += height;
            }
            remaining = new Rect(remaining.X + stripWidth, remaining.Y, Math.Max(0, remaining.Width - stripWidth), remaining.Height);
        }
        else
        {
            double stripHeight = Math.Min(remaining.Height, rowArea / remaining.Width);
            double x = remaining.X;
            for (int index = 0; index < row.Count; index++)
            {
                double width = index == row.Count - 1
                    ? remaining.Right - x
                    : Math.Min(remaining.Right - x, row[index].Area / stripHeight);
                output.Add(new TreemapLayoutItem(row[index].Tile, new Rect(x, remaining.Y, Math.Max(0, width), stripHeight)));
                x += width;
            }
            remaining = new Rect(remaining.X, remaining.Y + stripHeight, remaining.Width, Math.Max(0, remaining.Height - stripHeight));
        }
    }
}
