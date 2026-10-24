// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Reads Telepen. Each character is eight bits, an ASCII value and an even parity bit, least significant first, in sixteen modules of bars and spaces of one
/// or three modules. The pairs of a bar and a space stand for 1, 00, 010, or 01 and a later 10, as <see cref="TelepenEncoder"/> writes them. The symbol is
/// the start code _, the data, the modulo 127 check character and the stop code z.
/// </summary>
internal sealed class TelepenReader : LinearReader
{
    private const float MaximumVariance = 0.3F;
    private const float MaximumIndividualVariance = 0.8F;
    private const int StartCode = '_';
    private const int StopCode = 'z';

    /// <inheritdoc/>
    public override BarcodeFamily Families => BarcodeFamily.Telepen;

    /// <summary>
    /// Gets the twelve runs of the start code _, the bits 1111 1010 from the least significant.
    /// </summary>
    private static ReadOnlySpan<int> Start => [1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 3, 3];

    /// <inheritdoc/>
    public override bool TryRead(ReadOnlySpan<int> runs, int start, DecodeContext context, out LinearMatch match)
    {
        Span<int> values = stackalloc int[(runs.Length / 4) + 2];
        for (int i = start; i + 12 < runs.Length; i += 2)
        {
            if (Variance(runs.Slice(i, 12), Start, MaximumIndividualVariance) > MaximumVariance)
            {
                continue;
            }

            float module = Sum(runs, i, 12) / 16F;
            if (!IsQuiet(runs, i - 1, module * 5)
                || !TryReadCharacters(runs, i, module, values, out int count, out int end)
                || count < 3
                || values[0] != StartCode)
            {
                continue;
            }

            // The values between the start code and the check character are the data.
            ReadOnlySpan<int> data = values[1..(count - 1)];
            if (TelepenEncoder.CheckCharacter(data) != values[count - 1] || !TryInterpret(data, context.Options.TelepenNumeric, out string? text))
            {
                continue;
            }

            match = new LinearMatch(BarcodeFamily.Telepen, text, context.Options.TelepenNumeric ? "]B1" : "]B0", i, end, true);
            return true;
        }

        match = default;
        return false;
    }

    /// <summary>
    /// Reads characters from the start code to the stop code. A run is narrow below two modules and wide above. The space of the last bar of the stop code is
    /// the quiet zone, which counts as narrow.
    /// </summary>
    /// <param name="runs">The runs.</param>
    /// <param name="position">The index of the first bar of the start code.</param>
    /// <param name="module">The module width.</param>
    /// <param name="values">The character values to write, the start code first, without the stop code.</param>
    /// <param name="count">The number of values written.</param>
    /// <param name="end">The index of the light run after the stop code.</param>
    /// <returns><see langword="true"/> when the characters read up to the stop code.</returns>
    private static bool TryReadCharacters(ReadOnlySpan<int> runs, int position, float module, Span<int> values, out int count, out int end)
    {
        count = 0;
        end = 0;
        int bits = 0;
        int bitCount = 0;
        bool open = false;
        float wide = module * 2;
        while (position + 1 < runs.Length)
        {
            bool last = position + 1 == runs.Length - 1 || runs[position + 1] > module * 5;
            bool wideBar = runs[position] > wide;
            bool wideSpace = !last && runs[position + 1] > wide;
            if (runs[position] > module * 4 || (!last && runs[position + 1] > module * 4))
            {
                return false;
            }

            // The pair stands for one to three bits, least significant first.
            int pair;
            int pairBits;
            if (open)
            {
                if (wideBar)
                {
                    return false;
                }

                (pair, pairBits, open) = wideSpace ? (0b01, 2, false) : (0b1, 1, true);
            }
            else
            {
                (pair, pairBits, open) = (wideBar, wideSpace) switch
                {
                    (false, false) => (0b1, 1, false),
                    (true, false) => (0b00, 2, false),
                    (true, true) => (0b010, 3, false),
                    _ => (0b10, 2, true),
                };
            }

            bits |= pair << bitCount;
            bitCount += pairBits;
            position += 2;
            if (bitCount < 8)
            {
                continue;
            }

            if (bitCount > 8 || open || (BitOperations.PopCount((uint)bits) & 1) != 0 || count >= values.Length)
            {
                return false;
            }

            int value = bits & 0x7F;
            bits = 0;
            bitCount = 0;
            if (value == StopCode && count > 0)
            {
                end = position - 1;
                return last;
            }

            if (last)
            {
                return false;
            }

            values[count++] = value;
        }

        return false;
    }

    /// <summary>
    /// Turns the data values into the text: ASCII characters, or in the numeric mode two digits for each value from 27 and a digit and X for each value from
    /// 17 to 26.
    /// </summary>
    /// <param name="data">The data values.</param>
    /// <param name="numeric">Whether the symbol reads in the numeric mode.</param>
    /// <param name="text">The text.</param>
    /// <returns><see langword="true"/> when every value is valid in the mode.</returns>
    private static bool TryInterpret(ReadOnlySpan<int> data, bool numeric, out string text)
    {
        text = string.Empty;
        if (data.IsEmpty)
        {
            return false;
        }

        Span<char> characters = stackalloc char[data.Length * 2];
        int count = 0;
        foreach (int value in data)
        {
            if (!numeric)
            {
                characters[count++] = (char)value;
            }
            else if (value >= TelepenEncoder.NumericPairOffset)
            {
                int pair = value - TelepenEncoder.NumericPairOffset;
                characters[count++] = (char)('0' + (pair / 10));
                characters[count++] = (char)('0' + (pair % 10));
            }
            else if (value >= TelepenEncoder.NumericSingleOffset)
            {
                characters[count++] = (char)('0' + value - TelepenEncoder.NumericSingleOffset);
                characters[count++] = 'X';
            }
            else
            {
                return false;
            }
        }

        text = characters[..count].ToString();
        return true;
    }
}
