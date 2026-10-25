// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Finds an alignment pattern of QR Code in a region of the image: a dark centre module in a light ring, whose cross section is light, dark and light in the
/// ratio 1 : 1 : 1, section 5.3.5 of ISO/IEC 18004. The rows from the middle of the region outwards read the ratio, and the column through its centre
/// confirms it.
/// </summary>
internal static class AlignmentPatternFinder
{
    /// <summary>
    /// Finds the alignment pattern nearest an estimated centre.
    /// </summary>
    /// <param name="bits">The image.</param>
    /// <param name="moduleSize">The module size in pixels.</param>
    /// <param name="x">The estimated x coordinate of the centre.</param>
    /// <param name="y">The estimated y coordinate of the centre.</param>
    /// <param name="allowance">The half width of the region in modules.</param>
    /// <returns>The pattern, or <see langword="null"/>.</returns>
    public static FinderPattern? Find(BitImage bits, float moduleSize, float x, float y, int allowance)
    {
        int reach = (int)(allowance * moduleSize);
        int left = Math.Max(0, (int)x - reach);
        int right = Math.Min(bits.Width - 1, (int)x + reach);
        int top = Math.Max(0, (int)y - reach);
        int bottom = Math.Min(bits.Height - 1, (int)y + reach);
        if (right - left < moduleSize * 3 || bottom - top < moduleSize * 3)
        {
            return null;
        }

        List<FinderPattern> candidates = [];
        Span<int> counts = stackalloc int[3];
        int middle = (top + bottom) / 2;
        int height = bottom - top;
        for (int k = 0; k < height; k++)
        {
            // The rows from the middle outwards.
            int row = middle + ((k & 1) == 0 ? (k + 1) / 2 : -((k + 1) / 2));
            if (row < top || row > bottom)
            {
                continue;
            }

            counts.Clear();
            int column = left;

            // A light run cut by the left edge of the region has no length.
            while (column <= right && !bits[column, row])
            {
                column++;
            }

            int state = 0;
            for (; column <= right; column++)
            {
                if (bits[column, row])
                {
                    if (state == 1)
                    {
                        counts[1]++;
                    }
                    else if (state == 2)
                    {
                        FinderPattern? confirmed = TryAdd(bits, counts, column, row, moduleSize, candidates);
                        if (confirmed is not null)
                        {
                            return confirmed;
                        }

                        counts[0] = counts[2];
                        counts[1] = 1;
                        counts[2] = 0;
                        state = 1;
                    }
                    else
                    {
                        counts[++state]++;
                    }
                }
                else
                {
                    if (state == 1)
                    {
                        state++;
                    }

                    counts[state]++;
                }
            }

            if (state == 2)
            {
                FinderPattern? confirmed = TryAdd(bits, counts, right + 1, row, moduleSize, candidates);
                if (confirmed is not null)
                {
                    return confirmed;
                }
            }
        }

        // Nothing read twice: the first guess is better than none.
        return candidates.Count > 0 ? candidates[0] : null;
    }

    /// <summary>
    /// Returns whether three runs are each about one module.
    /// </summary>
    /// <param name="counts">The runs.</param>
    /// <param name="moduleSize">The module size in pixels.</param>
    /// <returns><see langword="true"/> for the ratio.</returns>
    private static bool IsPattern(ReadOnlySpan<int> counts, float moduleSize)
    {
        float variance = moduleSize / 2F;
        foreach (int count in counts)
        {
            if (MathF.Abs(moduleSize - count) >= variance)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Confirms a pattern in the column through its centre, and returns it once two rows have read it.
    /// </summary>
    /// <param name="bits">The image.</param>
    /// <param name="counts">The runs of the row.</param>
    /// <param name="end">The position after the last run.</param>
    /// <param name="row">The row.</param>
    /// <param name="moduleSize">The module size in pixels.</param>
    /// <param name="candidates">The patterns read once.</param>
    /// <returns>The pattern when a second row confirms it, or <see langword="null"/>.</returns>
    private static FinderPattern? TryAdd(BitImage bits, ReadOnlySpan<int> counts, int end, int row, float moduleSize, List<FinderPattern> candidates)
    {
        if (!IsPattern(counts, moduleSize))
        {
            return null;
        }

        int total = counts[0] + counts[1] + counts[2];
        float centreX = end - counts[2] - (counts[1] / 2F);
        float centreY = CrossCheckVertical(bits, row, (int)centreX, 2 * counts[1], total, moduleSize);
        if (float.IsNaN(centreY))
        {
            return null;
        }

        float module = total / 3F;
        foreach (FinderPattern candidate in candidates)
        {
            if (candidate.AboutEquals(module, centreX, centreY))
            {
                candidate.Combine(centreX, centreY, module);
                return candidate;
            }
        }

        candidates.Add(new FinderPattern(centreX, centreY, module));
        return null;
    }

    /// <summary>
    /// Reads the light, dark and light runs through a point along its column and returns the centre of the dark run.
    /// </summary>
    /// <param name="bits">The image.</param>
    /// <param name="start">The row inside the dark centre.</param>
    /// <param name="column">The column.</param>
    /// <param name="maxCount">The longest a run may be.</param>
    /// <param name="originalTotal">The length of the runs of the row.</param>
    /// <param name="moduleSize">The module size in pixels.</param>
    /// <returns>The centre, or NaN.</returns>
    private static float CrossCheckVertical(BitImage bits, int start, int column, int maxCount, int originalTotal, float moduleSize)
    {
        Span<int> counts = stackalloc int[3];
        int i = start;
        while (i >= 0 && bits[column, i] && counts[1] <= maxCount)
        {
            counts[1]++;
            i--;
        }

        if (i < 0 || counts[1] > maxCount)
        {
            return float.NaN;
        }

        while (i >= 0 && !bits[column, i] && counts[0] <= maxCount)
        {
            counts[0]++;
            i--;
        }

        if (counts[0] > maxCount)
        {
            return float.NaN;
        }

        i = start + 1;
        while (i < bits.Height && bits[column, i] && counts[1] <= maxCount)
        {
            counts[1]++;
            i++;
        }

        if (i == bits.Height || counts[1] > maxCount)
        {
            return float.NaN;
        }

        while (i < bits.Height && !bits[column, i] && counts[2] <= maxCount)
        {
            counts[2]++;
            i++;
        }

        if (counts[2] > maxCount)
        {
            return float.NaN;
        }

        int total = counts[0] + counts[1] + counts[2];
        if (5 * Math.Abs(total - originalTotal) >= 2 * originalTotal || !IsPattern(counts, moduleSize))
        {
            return float.NaN;
        }

        return i - counts[2] - (counts[1] / 2F);
    }
}
