// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Reads MSI. Each digit is four bits, most significant first, and each bit is a bar and a space: a wide bar and a narrow space for a 1, a narrow bar and a
/// wide space for a 0. The start character is a 1 bit and the stop character a 0 bit and a narrow bar. The check digits are optional and of several kinds, so
/// the reader transmits every digit.
/// </summary>
internal sealed class MsiReader : LinearReader
{
    /// <inheritdoc/>
    public override BarcodeFamily Families => BarcodeFamily.Msi;

    /// <inheritdoc/>
    public override bool TryRead(ReadOnlySpan<int> runs, int start, DecodeContext context, out LinearMatch match)
    {
        Span<char> digits = stackalloc char[(runs.Length / 8) + 1];
        for (int i = start; i + 4 < runs.Length; i += 2)
        {
            // The start character: a wide bar and a narrow space, three modules.
            float module = (runs[i] + runs[i + 1]) / 3F;
            if (!IsOne(runs[i], runs[i + 1]) || !IsQuiet(runs, i - 1, module * 5))
            {
                continue;
            }

            int count = 0;
            int bits = 0;
            int bitCount = 0;
            int position = i + 2;
            bool stopped = false;
            while (position + 2 < runs.Length && count < digits.Length)
            {
                int bar = runs[position];
                int space = runs[position + 1];

                // The stop character: a 0 bit, then a narrow bar and the quiet zone.
                if (bitCount == 0 && IsZero(bar, space) && runs[position + 2] * 2 < (bar + space) && IsQuiet(runs, position + 3, module * 5))
                {
                    stopped = count > 0;
                    position += 3;
                    break;
                }

                // Every bit is one bar and one space of three modules together.
                float pitch = (bar + space) / 3F;
                if (pitch < module * 0.6F || pitch > module * 1.6F)
                {
                    break;
                }

                if (IsOne(bar, space))
                {
                    bits = (bits << 1) | 1;
                }
                else if (IsZero(bar, space))
                {
                    bits <<= 1;
                }
                else
                {
                    break;
                }

                position += 2;
                if (++bitCount == 4)
                {
                    if (bits > 9)
                    {
                        break;
                    }

                    digits[count++] = (char)('0' + bits);
                    bits = 0;
                    bitCount = 0;
                }
            }

            if (stopped)
            {
                match = new LinearMatch(BarcodeFamily.Msi, digits[..count].ToString(), "]M0", i, position, false);
                return true;
            }
        }

        match = default;
        return false;
    }

    /// <summary>
    /// Returns a value indicating whether a bar and a space are a 1 bit: the bar clearly wider than the space.
    /// </summary>
    /// <param name="bar">The bar.</param>
    /// <param name="space">The space.</param>
    /// <returns><see langword="true"/> for a 1 bit.</returns>
    private static bool IsOne(int bar, int space) => bar * 2 > space * 3;

    /// <summary>
    /// Returns a value indicating whether a bar and a space are a 0 bit: the space clearly wider than the bar.
    /// </summary>
    /// <param name="bar">The bar.</param>
    /// <param name="space">The space.</param>
    /// <returns><see langword="true"/> for a 0 bit.</returns>
    private static bool IsZero(int bar, int space) => space * 2 > bar * 3;
}
