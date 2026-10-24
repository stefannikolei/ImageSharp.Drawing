// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Reads Plessey Code. Each bit is a bar and a space of one pitch: a wide bar for a 1 and a narrow bar for a 0. The bits of each hexadecimal character come
/// least significant first. The start code is 1101, and a full pitch termination bar and the reversed start code end the symbol. The last two characters
/// hold the eight bits of the cyclic redundancy check of <see cref="PlesseyEncoder.CheckCharacters"/>, which the reader checks and does not transmit.
/// </summary>
internal sealed class PlesseyReader : LinearReader
{
    /// <inheritdoc/>
    public override BarcodeFamily Families => BarcodeFamily.Plessey;

    /// <summary>
    /// Gets the bits of the start code in the order they stand: 1, 1, 0, 1.
    /// </summary>
    private static ReadOnlySpan<int> StartBits => [1, 1, 0, 1];

    /// <inheritdoc/>
    public override bool TryRead(ReadOnlySpan<int> runs, int start, DecodeContext context, out LinearMatch match)
    {
        Span<int> values = stackalloc int[(runs.Length / 8) + 1];
        Span<char> characters = stackalloc char[values.Length];
        Span<char> checks = stackalloc char[PlesseyEncoder.CheckCharacterCount];
        for (int i = start; i + 8 < runs.Length; i += 2)
        {
            float pitch = Sum(runs, i, 8) / 4F;
            if (!IsQuiet(runs, i - 1, pitch * 2) || !MatchesStart(runs.Slice(i, 8), pitch))
            {
                continue;
            }

            int count = 0;
            int value = 0;
            int bitCount = 0;
            int position = i + 8;
            bool stopped = false;
            while (position + 1 < runs.Length && count < values.Length)
            {
                int bar = runs[position];

                // The termination bar fills a whole pitch, with no space of its own: the space after it belongs to the reversed start code.
                if (bitCount == 0 && bar > pitch * 0.85F)
                {
                    stopped = position + 9 <= runs.Length && IsQuiet(runs, position + 9, pitch * 2);
                    position += 9;
                    break;
                }

                int pair = bar + runs[position + 1];
                if (pair < pitch * 0.7F || pair > pitch * 1.3F)
                {
                    break;
                }

                if (bar * 2 > pair)
                {
                    value |= 1 << bitCount;
                }

                position += 2;
                if (++bitCount == 4)
                {
                    values[count++] = value;
                    value = 0;
                    bitCount = 0;
                }
            }

            if (!stopped || count <= PlesseyEncoder.CheckCharacterCount)
            {
                continue;
            }

            int dataCount = count - PlesseyEncoder.CheckCharacterCount;
            for (int c = 0; c < count; c++)
            {
                characters[c] = PlesseyEncoder.Characters[values[c]];
            }

            PlesseyEncoder.CheckCharacters(characters[..dataCount], checks);
            if (checks[0] == characters[dataCount] && checks[1] == characters[dataCount + 1])
            {
                match = new LinearMatch(BarcodeFamily.Plessey, characters[..dataCount].ToString(), "]P0", i, position, true);
                return true;
            }
        }

        match = default;
        return false;
    }

    /// <summary>
    /// Returns a value indicating whether eight runs are the start code at the pitch.
    /// </summary>
    /// <param name="runs">The eight runs.</param>
    /// <param name="pitch">The width of one bit.</param>
    /// <returns><see langword="true"/> for the start code.</returns>
    private static bool MatchesStart(ReadOnlySpan<int> runs, float pitch)
    {
        ReadOnlySpan<int> bits = StartBits;
        for (int b = 0; b < 4; b++)
        {
            int bar = runs[b * 2];
            int pair = bar + runs[(b * 2) + 1];
            if (pair < pitch * 0.7F || pair > pitch * 1.3F || (bar * 2 > pair ? 1 : 0) != bits[b])
            {
                return false;
            }
        }

        return true;
    }
}
