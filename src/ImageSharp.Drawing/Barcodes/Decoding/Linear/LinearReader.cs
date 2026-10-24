// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Reads one or more linear families from the runs of one scan line. The runs alternate light and dark, starting with a light run, so the bars are at odd
/// indices. A reader searches the runs from a start index for the start pattern of its symbology and decodes the characters that follow it.
/// </summary>
internal abstract class LinearReader : IFamilyReader
{
    /// <summary>
    /// Gets the families the reader returns.
    /// </summary>
    public abstract BarcodeFamily Families { get; }

    /// <summary>
    /// Gets a value indicating whether the symbology reads in one direction only, from the left of the upright image. A symbol whose characters read the
    /// same from either end, such as the bars of a Pharmacode, would give a second value read from the right.
    /// </summary>
    public virtual bool Directional => false;

    /// <summary>
    /// Searches the runs for a symbol, from the bar at the start index on.
    /// </summary>
    /// <param name="runs">The runs of the scan line in pixels, light first. The last run is light.</param>
    /// <param name="start">The index of the first bar to try, an odd number.</param>
    /// <param name="context">The decode.</param>
    /// <param name="match">The symbol found.</param>
    /// <returns><see langword="true"/> when the reader found a symbol.</returns>
    public abstract bool TryRead(ReadOnlySpan<int> runs, int start, DecodeContext context, out LinearMatch match);

    /// <summary>
    /// Returns a value indicating whether the light run at an index is at least as wide as the quiet zone needs. The first and the last run reach the ends of
    /// the scan line, and the image ends on a light area, so they count as wide enough.
    /// </summary>
    /// <param name="runs">The runs.</param>
    /// <param name="index">The index of the light run.</param>
    /// <param name="minimum">The smallest width in pixels.</param>
    /// <returns><see langword="true"/> when the quiet zone is clear.</returns>
    protected static bool IsQuiet(ReadOnlySpan<int> runs, int index, float minimum)
        => index <= 0 || index >= runs.Length - 1 || runs[index] >= minimum;

    /// <summary>
    /// Returns the sum of a range of runs.
    /// </summary>
    /// <param name="runs">The runs.</param>
    /// <param name="start">The index of the first run.</param>
    /// <param name="count">The number of runs.</param>
    /// <returns>The sum in pixels.</returns>
    protected static int Sum(ReadOnlySpan<int> runs, int start, int count)
    {
        int sum = 0;
        foreach (int run in runs.Slice(start, count))
        {
            sum += run;
        }

        return sum;
    }

    /// <summary>
    /// Returns how far the runs are from a pattern of module widths, the runs scaled so that their total equals the total of the pattern. The result is the
    /// sum of the differences over the total, in modules per module, or <see cref="float.MaxValue"/> when one run differs by more than the limit.
    /// </summary>
    /// <param name="runs">The runs in pixels.</param>
    /// <param name="pattern">The module widths.</param>
    /// <param name="maximumIndividual">The largest difference of one run, in modules.</param>
    /// <returns>The variance.</returns>
    protected static float Variance(ReadOnlySpan<int> runs, ReadOnlySpan<byte> pattern, float maximumIndividual)
    {
        Span<int> widths = stackalloc int[pattern.Length];
        for (int i = 0; i < pattern.Length; i++)
        {
            widths[i] = pattern[i];
        }

        return Variance(runs, widths, maximumIndividual);
    }

    /// <summary>
    /// Returns how far the runs are from a pattern of module widths, the runs scaled so that their total equals the total of the pattern. The result is the
    /// sum of the differences over the total, in modules per module, or <see cref="float.MaxValue"/> when one run differs by more than the limit.
    /// </summary>
    /// <param name="runs">The runs in pixels.</param>
    /// <param name="pattern">The module widths.</param>
    /// <param name="maximumIndividual">The largest difference of one run, in modules.</param>
    /// <returns>The variance.</returns>
    protected static float Variance(ReadOnlySpan<int> runs, ReadOnlySpan<int> pattern, float maximumIndividual)
    {
        int total = 0;
        int patternTotal = 0;
        for (int i = 0; i < pattern.Length; i++)
        {
            total += runs[i];
            patternTotal += pattern[i];
        }

        if (total < patternTotal)
        {
            // Fewer pixels than modules cannot hold the pattern.
            return float.MaxValue;
        }

        float unit = (float)total / patternTotal;
        float sum = 0;
        for (int i = 0; i < pattern.Length; i++)
        {
            float difference = MathF.Abs(runs[i] - (pattern[i] * unit));
            if (difference > maximumIndividual * unit)
            {
                return float.MaxValue;
            }

            sum += difference;
        }

        return sum / total;
    }

    /// <summary>
    /// Classifies each run of a character as narrow or wide, for a symbology of two element widths. The threshold lies halfway between the narrowest and the
    /// widest run, so it works at any ratio of wide to narrow.
    /// </summary>
    /// <param name="runs">The runs of the character.</param>
    /// <returns>A bit mask with bit i set when run i is wide, or -1 when the runs are all one width.</returns>
    protected static int WideMask(ReadOnlySpan<int> runs)
    {
        int min = int.MaxValue;
        int max = 0;
        foreach (int run in runs)
        {
            min = Math.Min(min, run);
            max = Math.Max(max, run);
        }

        // A ratio below 1.5 is too small to tell the widths apart.
        if (max * 2 < min * 3)
        {
            return -1;
        }

        int threshold2 = min + max;
        int mask = 0;
        for (int i = 0; i < runs.Length; i++)
        {
            if (runs[i] * 2 > threshold2)
            {
                mask |= 1 << i;
            }
        }

        return mask;
    }

    /// <summary>
    /// Classifies the runs of a character with a known number of wide elements: the widest that many are wide. The narrowest wide element must be clearly
    /// wider than the widest narrow element, a ratio of more than 1.5.
    /// </summary>
    /// <param name="runs">The runs, at most 16.</param>
    /// <param name="wideCount">The number of wide elements.</param>
    /// <returns>A mask with bit i set when run i is wide, or -1.</returns>
    protected static int WideElements(ReadOnlySpan<int> runs, int wideCount)
    {
        Span<int> sorted = stackalloc int[runs.Length];
        runs.CopyTo(sorted);
        sorted.Sort();
        int maxNarrow = sorted[runs.Length - wideCount - 1];
        int minWide = sorted[runs.Length - wideCount];
        if (minWide * 2 <= maxNarrow * 3)
        {
            return -1;
        }

        int mask = 0;
        for (int i = 0; i < runs.Length; i++)
        {
            if (runs[i] >= minWide)
            {
                mask |= 1 << i;
            }
        }

        return mask;
    }
}
