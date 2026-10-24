// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Reads Interleaved 2 of 5, ISO/IEC 16390, with ITF-14, Leitcode and Identcode. Each pair of digits is five bars and five spaces: the bars carry the first
/// digit and the spaces the second, two of each five wide. The start pattern is four narrow elements and the stop pattern a wide bar and two narrow elements.
/// The symbology defines no check digit, so the reader transmits every digit and needs a quiet zone of ten narrow elements on both sides.
/// </summary>
internal sealed class Interleaved2Of5Reader : LinearReader
{
    /// <summary>
    /// The digit of each five element pattern, the first element in the lowest bit, or -1.
    /// </summary>
    private static readonly sbyte[] Digits = CreateDigits(Interleaved2Of5Encoder.Patterns);

    /// <inheritdoc/>
    public override BarcodeFamily Families => BarcodeFamily.Interleaved2Of5;

    /// <inheritdoc/>
    public override bool TryRead(ReadOnlySpan<int> runs, int start, DecodeContext context, out LinearMatch match)
    {
        Span<char> digits = stackalloc char[((runs.Length / 10) * 2) + 2];
        Span<int> elements = stackalloc int[5];
        for (int i = start; i + 4 < runs.Length; i += 2)
        {
            // The start pattern: four narrow elements of about one width.
            float narrow = Sum(runs, i, 4) / 4F;
            if (!AllNarrow(runs.Slice(i, 4), narrow))
            {
                continue;
            }

            // Section 3.2: a quiet zone of ten modules. Six of them tolerate a tight crop, and the quiet zone keeps the reader from starting inside a symbol.
            if (!IsQuiet(runs, i - 1, narrow * 6))
            {
                continue;
            }

            int count = 0;
            int position = i + 4;
            bool stopped = false;
            while (position + 3 <= runs.Length && count + 2 <= digits.Length)
            {
                // The stop pattern: a wide bar, a narrow space and a narrow bar, then the quiet zone.
                if (runs[position] >= narrow * 1.8F
                    && AllNarrow(runs.Slice(position + 1, 2), narrow)
                    && IsQuiet(runs, position + 3, narrow * 6))
                {
                    stopped = true;
                    position += 3;
                    break;
                }

                if (position + 10 > runs.Length)
                {
                    break;
                }

                int first = ReadDigit(runs.Slice(position, 10), 0, elements);
                int second = ReadDigit(runs.Slice(position, 10), 1, elements);
                if (first < 0 || second < 0)
                {
                    break;
                }

                digits[count++] = (char)('0' + first);
                digits[count++] = (char)('0' + second);
                position += 10;
            }

            if (stopped && count >= 2)
            {
                match = new LinearMatch(BarcodeFamily.Interleaved2Of5, digits[..count].ToString(), "]I0", i, position, false);
                return true;
            }
        }

        match = default;
        return false;
    }

    /// <summary>
    /// Inverts a table of five element digit patterns whose first element is the highest bit.
    /// </summary>
    /// <param name="patterns">The patterns, indexed by digit.</param>
    /// <returns>The digit of every mask.</returns>
    public static sbyte[] CreateDigits(ReadOnlySpan<byte> patterns)
    {
        sbyte[] digits = new sbyte[32];
        digits.AsSpan().Fill(-1);
        for (int d = 0; d < patterns.Length; d++)
        {
            int mask = 0;
            for (int e = 0; e < 5; e++)
            {
                if (((patterns[d] >> (4 - e)) & 1) != 0)
                {
                    mask |= 1 << e;
                }
            }

            digits[mask] = (sbyte)d;
        }

        return digits;
    }

    /// <summary>
    /// Returns a value indicating whether every run is about one narrow width.
    /// </summary>
    /// <param name="runs">The runs.</param>
    /// <param name="narrow">The narrow width.</param>
    /// <returns><see langword="true"/> when every run is narrow.</returns>
    private static bool AllNarrow(ReadOnlySpan<int> runs, float narrow)
    {
        foreach (int run in runs)
        {
            if (run < narrow * 0.5F || run > narrow * 1.5F)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Reads the digit of the bars or the spaces of a pair.
    /// </summary>
    /// <param name="runs">The ten runs of the pair.</param>
    /// <param name="offset">0 for the bars, 1 for the spaces.</param>
    /// <param name="elements">Scratch for five runs.</param>
    /// <returns>The digit, or -1.</returns>
    private static int ReadDigit(ReadOnlySpan<int> runs, int offset, Span<int> elements)
    {
        for (int e = 0; e < 5; e++)
        {
            elements[e] = runs[(e * 2) + offset];
        }

        int mask = WideElements(elements, 2);
        return mask < 0 ? -1 : Digits[mask];
    }
}
