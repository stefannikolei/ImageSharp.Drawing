// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Reads Codabar, BS EN 798. Each character is four bars and three spaces, two or three of them wide, followed by a narrow gap. The symbol starts and stops
/// with one of the characters A to D, and the reader transmits them with the data, as a check character the symbol carries is, because the symbology defines
/// none of its own.
/// </summary>
internal sealed class CodabarReader : LinearReader
{
    /// <summary>
    /// The character value of each seven element pattern, the first element in the lowest bit and a set bit for a wide element, or -1.
    /// </summary>
    private static readonly sbyte[] Characters = CreateCharacters();

    /// <inheritdoc/>
    public override BarcodeFamily Families => BarcodeFamily.Codabar;

    /// <inheritdoc/>
    public override bool TryRead(ReadOnlySpan<int> runs, int start, DecodeContext context, out LinearMatch match)
    {
        Span<char> characters = stackalloc char[(runs.Length / 8) + 1];
        for (int i = start; i + 7 < runs.Length; i += 2)
        {
            int value = ReadCharacter(runs.Slice(i, 7));
            if (value < CodabarEncoder.FirstStartStop)
            {
                continue;
            }

            // Section 4.4.1 (d): a quiet zone of ten modules. Half a character tolerates a tight crop.
            int width = Sum(runs, i, 7);
            if (!IsQuiet(runs, i - 1, width / 2F))
            {
                continue;
            }

            characters[0] = CodabarEncoder.Character(value);
            int count = 1;
            int position = i + 8;
            bool stopped = false;
            while (position + 7 <= runs.Length && count < characters.Length)
            {
                // The gap before the character is narrow, and the character is about as wide as the start character.
                if (runs[position - 1] > width / 2)
                {
                    break;
                }

                value = ReadCharacter(runs.Slice(position, 7));
                int characterWidth = Sum(runs, position, 7);
                if (value < 0 || characterWidth * 2 < width || characterWidth > width * 2)
                {
                    break;
                }

                characters[count++] = CodabarEncoder.Character(value);
                if (value >= CodabarEncoder.FirstStartStop)
                {
                    stopped = IsQuiet(runs, position + 7, width / 2F);
                    position += 7;
                    break;
                }

                position += 8;
            }

            if (stopped && count >= 3)
            {
                match = new LinearMatch(BarcodeFamily.Codabar, characters[..count].ToString(), "]F0", i, position, false);
                return true;
            }
        }

        match = default;
        return false;
    }

    /// <summary>
    /// Reads one character from seven runs: two wide elements for a data character, three for a start and stop character.
    /// </summary>
    /// <param name="runs">The seven runs, bar first.</param>
    /// <returns>The character value, or -1.</returns>
    private static int ReadCharacter(ReadOnlySpan<int> runs)
    {
        int mask = WideElements(runs, 2);
        if (mask >= 0 && Characters[mask] is >= 0 and < CodabarEncoder.FirstStartStop)
        {
            return Characters[mask];
        }

        mask = WideElements(runs, 3);
        return mask >= 0 ? Characters[mask] : -1;
    }

    /// <summary>
    /// Inverts <see cref="CodabarEncoder.Patterns"/>, whose first element is the highest of seven bits.
    /// </summary>
    /// <returns>The character value of every mask.</returns>
    private static sbyte[] CreateCharacters()
    {
        sbyte[] characters = new sbyte[128];
        characters.AsSpan().Fill(-1);
        ReadOnlySpan<byte> patterns = CodabarEncoder.Patterns;
        for (int v = 0; v < patterns.Length; v++)
        {
            int mask = 0;
            for (int e = 0; e < 7; e++)
            {
                if (((patterns[v] >> (6 - e)) & 1) != 0)
                {
                    mask |= 1 << e;
                }
            }

            characters[mask] = (sbyte)v;
        }

        return characters;
    }
}
