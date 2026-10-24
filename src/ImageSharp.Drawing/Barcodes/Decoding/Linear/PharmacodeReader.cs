// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Globalization;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Reads the one-track Pharmacode of the Laetus Pharmacode Guide: two to sixteen thin and thick bars with equal gaps, read from the right. A thin bar in
/// position n stands for 2^(n-1) and a thick bar for 2^n, and the number is their sum. Any bars read as a number, so the reader reads from the left of the
/// upright image only and the family is not one of the defaults.
/// </summary>
internal sealed class PharmacodeReader : LinearReader
{
    /// <summary>
    /// The largest number of bars of a symbol.
    /// </summary>
    private const int MaximumBars = 16;

    /// <inheritdoc/>
    public override BarcodeFamily Families => BarcodeFamily.Pharmacode;

    /// <inheritdoc/>
    public override bool Directional => true;

    /// <inheritdoc/>
    public override bool TryRead(ReadOnlySpan<int> runs, int start, DecodeContext context, out LinearMatch match)
    {
        Span<bool> thick = stackalloc bool[MaximumBars];
        for (int i = start; i + 2 < runs.Length; i += 2)
        {
            // A gap is two modules, and section 2.2.4 keeps 6 mm, twelve modules, clear on either side. Two gaps tolerate a tight crop.
            float gap = runs[i + 1];
            if (!IsQuiet(runs, i - 1, gap * 2))
            {
                continue;
            }

            int count = 0;
            int position = i;
            bool ended = false;
            while (position < runs.Length && count < MaximumBars)
            {
                // A thin bar is half a gap and a thick bar one and a half.
                float bar = runs[position] / gap;
                if (bar < 0.25F || bar > 2.2F)
                {
                    break;
                }

                thick[count++] = bar > 1F;
                float next = runs[position + 1] / gap;
                if (position + 1 == runs.Length - 1 || next >= 2F)
                {
                    ended = true;
                    break;
                }

                if (next < 0.6F || next > 1.5F)
                {
                    break;
                }

                position += 2;
            }

            if (!ended || count < 2 || IsSkewed(runs.Slice(i, (count * 2) - 1)))
            {
                continue;
            }

            int value = 0;
            for (int b = 0; b < count; b++)
            {
                // The rightmost bar is position 1.
                int weight = 1 << (count - 1 - b);
                value += thick[b] ? weight * 2 : weight;
            }

            match = new LinearMatch(BarcodeFamily.Pharmacode, value.ToString(CultureInfo.InvariantCulture), "]X0", i, position + 1, false);
            return true;
        }

        match = default;
        return false;
    }

    /// <summary>
    /// Returns a value indicating whether the bars are not of two clear widths: the thin bars must be at most two thirds of the thick bars.
    /// </summary>
    /// <param name="runs">The bars and the gaps between them.</param>
    /// <returns><see langword="true"/> when the bar widths overlap.</returns>
    private static bool IsSkewed(ReadOnlySpan<int> runs)
    {
        float gap = runs.Length > 1 ? runs[1] : runs[0];
        int maxThin = 0;
        int minThick = int.MaxValue;
        for (int i = 0; i < runs.Length; i += 2)
        {
            if (runs[i] > gap)
            {
                minThick = Math.Min(minThick, runs[i]);
            }
            else
            {
                maxThin = Math.Max(maxThin, runs[i]);
            }
        }

        return minThick != int.MaxValue && maxThin * 3 > minThick * 2;
    }
}
