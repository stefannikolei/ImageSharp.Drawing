// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Finds the finder patterns of QR Code, Micro QR Code and Rectangular Micro QR Code: square rings whose cross section is dark, light, dark, light and dark
/// in the ratio 1 : 1 : 3 : 1 : 1, section 5.3.2 of ISO/IEC 18004. Each image row reads the ratio, and the column, the row and the diagonal through its centre
/// confirm it.
/// </summary>
internal static class FinderPatternFinder
{
    /// <summary>
    /// Finds the finder patterns of an image.
    /// </summary>
    /// <param name="bits">The image.</param>
    /// <param name="step">The rows between two scanned rows.</param>
    /// <returns>The patterns, each with the number of rows that confirmed it.</returns>
    public static List<FinderPattern> Find(BitImage bits, int step)
    {
        List<FinderPattern> centers = [];
        Span<int> counts = stackalloc int[5];
        int width = bits.Width;
        for (int y = 0; y < bits.Height; y += step)
        {
            ReadOnlySpan<byte> row = bits.Row(y);
            counts.Clear();
            int state = 0;
            for (int x = 0; x < width; x++)
            {
                if (row[x] != 0)
                {
                    // A dark pixel ends a light run.
                    if ((state & 1) == 1)
                    {
                        state++;
                    }

                    counts[state]++;
                }
                else if ((state & 1) == 1)
                {
                    counts[state]++;
                }
                else if (state < 4)
                {
                    counts[++state]++;
                }
                else if (IsPattern(counts, 0.5F) && TryAdd(bits, counts, x, y, centers))
                {
                    counts.Clear();
                    state = 0;
                }
                else
                {
                    // The last three runs may start a pattern.
                    counts[0] = counts[2];
                    counts[1] = counts[3];
                    counts[2] = counts[4];
                    counts[3] = 1;
                    counts[4] = 0;
                    state = 3;
                }
            }

            if (state == 4 && IsPattern(counts, 0.5F))
            {
                TryAdd(bits, counts, width, y, centers);
            }
        }

        return centers;
    }

    /// <summary>
    /// Returns whether five runs are in the ratio 1 : 1 : 3 : 1 : 1.
    /// </summary>
    /// <param name="counts">The runs.</param>
    /// <param name="tolerance">The largest difference of a run from its share, in modules.</param>
    /// <returns><see langword="true"/> for the ratio.</returns>
    private static bool IsPattern(ReadOnlySpan<int> counts, float tolerance)
    {
        int total = 0;
        foreach (int count in counts)
        {
            if (count == 0)
            {
                return false;
            }

            total += count;
        }

        if (total < 7)
        {
            return false;
        }

        float module = total / 7F;
        float variance = module * tolerance;
        return MathF.Abs(module - counts[0]) < variance
            && MathF.Abs(module - counts[1]) < variance
            && MathF.Abs((3 * module) - counts[2]) < 3 * variance
            && MathF.Abs(module - counts[3]) < variance
            && MathF.Abs(module - counts[4]) < variance;
    }

    /// <summary>
    /// Returns the centre of the middle run of five runs that end at a position.
    /// </summary>
    /// <param name="counts">The runs.</param>
    /// <param name="end">The position after the last run.</param>
    /// <returns>The centre.</returns>
    private static float CentreFromEnd(ReadOnlySpan<int> counts, int end) => end - counts[4] - counts[3] - (counts[2] / 2F);

    /// <summary>
    /// Confirms a pattern a row reads in its column, row and diagonal, and adds it to the patterns or averages it into the pattern it is.
    /// </summary>
    /// <param name="bits">The image.</param>
    /// <param name="counts">The runs of the row.</param>
    /// <param name="end">The position after the last run.</param>
    /// <param name="y">The row.</param>
    /// <param name="centers">The patterns.</param>
    /// <returns><see langword="true"/> when the pattern is confirmed.</returns>
    private static bool TryAdd(BitImage bits, ReadOnlySpan<int> counts, int end, int y, List<FinderPattern> centers)
    {
        int total = counts[0] + counts[1] + counts[2] + counts[3] + counts[4];
        float centreX = CentreFromEnd(counts, end);
        float centreY = CrossCheck(bits, (int)centreX, y, false, counts[2], total);
        if (float.IsNaN(centreY))
        {
            return false;
        }

        centreX = CrossCheck(bits, (int)centreY, (int)centreX, true, counts[2], total);
        if (float.IsNaN(centreX) || !CrossCheckDiagonal(bits, (int)centreX, (int)centreY))
        {
            return false;
        }

        float module = total / 7F;
        foreach (FinderPattern center in centers)
        {
            if (center.AboutEquals(module, centreX, centreY))
            {
                center.Combine(centreX, centreY, module);
                return true;
            }
        }

        centers.Add(new FinderPattern(centreX, centreY, module));
        return true;
    }

    /// <summary>
    /// Reads the five runs through a point along a column or a row, outwards from the dark centre, and returns the centre of the middle run.
    /// </summary>
    /// <param name="bits">The image.</param>
    /// <param name="fixedCoordinate">The column for a vertical check, or the row for a horizontal one.</param>
    /// <param name="start">The row for a vertical check, or the column for a horizontal one, inside the dark centre.</param>
    /// <param name="horizontal">Whether the check runs along a row.</param>
    /// <param name="maxCount">The longest an outer run may be.</param>
    /// <param name="originalTotal">The length of the five runs of the row that found the pattern.</param>
    /// <returns>The centre, or NaN when the runs are not the pattern.</returns>
    private static float CrossCheck(BitImage bits, int fixedCoordinate, int start, bool horizontal, int maxCount, int originalTotal)
    {
        int limit = horizontal ? bits.Width : bits.Height;
        Span<int> counts = stackalloc int[5];
        bool Dark(int p) => horizontal ? bits[p, fixedCoordinate] : bits[fixedCoordinate, p];

        int i = start;
        while (i >= 0 && Dark(i))
        {
            counts[2]++;
            i--;
        }

        if (i < 0)
        {
            return float.NaN;
        }

        while (i >= 0 && !Dark(i) && counts[1] <= maxCount)
        {
            counts[1]++;
            i--;
        }

        if (i < 0 || counts[1] > maxCount)
        {
            return float.NaN;
        }

        while (i >= 0 && Dark(i) && counts[0] <= maxCount)
        {
            counts[0]++;
            i--;
        }

        if (counts[0] > maxCount)
        {
            return float.NaN;
        }

        i = start + 1;
        while (i < limit && Dark(i))
        {
            counts[2]++;
            i++;
        }

        if (i == limit)
        {
            return float.NaN;
        }

        while (i < limit && !Dark(i) && counts[3] < maxCount)
        {
            counts[3]++;
            i++;
        }

        if (i == limit || counts[3] >= maxCount)
        {
            return float.NaN;
        }

        while (i < limit && Dark(i) && counts[4] < maxCount)
        {
            counts[4]++;
            i++;
        }

        if (counts[4] >= maxCount)
        {
            return float.NaN;
        }

        // The runs must be about as long as the runs of the row, or the pattern is not square.
        int total = counts[0] + counts[1] + counts[2] + counts[3] + counts[4];
        if (5 * Math.Abs(total - originalTotal) >= 2 * originalTotal)
        {
            return float.NaN;
        }

        return IsPattern(counts, 0.5F) ? CentreFromEnd(counts, i) : float.NaN;
    }

    /// <summary>
    /// Reads the five runs along the diagonal from the upper left to the lower right through a centre, which a square ring also crosses in the ratio of the
    /// pattern.
    /// </summary>
    /// <param name="bits">The image.</param>
    /// <param name="x">The column of the centre.</param>
    /// <param name="y">The row of the centre.</param>
    /// <returns><see langword="true"/> when the diagonal reads the pattern.</returns>
    private static bool CrossCheckDiagonal(BitImage bits, int x, int y)
    {
        Span<int> counts = stackalloc int[5];
        int i = 0;
        while (y >= i && x >= i && bits[x - i, y - i])
        {
            counts[2]++;
            i++;
        }

        while (y >= i && x >= i && !bits[x - i, y - i])
        {
            counts[1]++;
            i++;
        }

        while (y >= i && x >= i && bits[x - i, y - i])
        {
            counts[0]++;
            i++;
        }

        if (counts[2] == 0 || counts[1] == 0 || counts[0] == 0)
        {
            return false;
        }

        i = 1;
        while (y + i < bits.Height && x + i < bits.Width && bits[x + i, y + i])
        {
            counts[2]++;
            i++;
        }

        while (y + i < bits.Height && x + i < bits.Width && !bits[x + i, y + i])
        {
            counts[3]++;
            i++;
        }

        while (y + i < bits.Height && x + i < bits.Width && bits[x + i, y + i])
        {
            counts[4]++;
            i++;
        }

        return IsPattern(counts, 0.75F);
    }
}
