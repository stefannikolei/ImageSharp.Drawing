// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Reads Code 93, ANSI/AIM BC5-1995. Each symbol character is three bars and three spaces over nine modules. The reader matches the characters against
/// <see cref="Code93Encoder.Patterns"/>, checks both modulo 47 check characters and reverses the full ASCII substitutions of the four shift characters.
/// </summary>
internal sealed class Code93Reader : LinearReader
{
    private const float MaximumVariance = 0.25F;
    private const float MaximumIndividualVariance = 0.7F;

    /// <summary>
    /// The number of symbol character values: the 43 data characters and the four shift characters.
    /// </summary>
    private const int ValueCount = 47;

    /// <summary>
    /// The ASCII character of each shift pair of <see cref="Code93ExtendedSymbology.Substitutions"/>, at index (shift * 26) + letter, or -1.
    /// </summary>
    private static readonly sbyte[] FullAscii = CreateFullAscii();

    /// <inheritdoc/>
    public override BarcodeFamily Families => BarcodeFamily.Code93;

    /// <summary>
    /// Gets the six elements of the start character. The stop character adds a termination bar of one module.
    /// </summary>
    private static ReadOnlySpan<int> StartStop => [1, 1, 1, 1, 4, 1];

    /// <inheritdoc/>
    public override bool TryRead(ReadOnlySpan<int> runs, int start, DecodeContext context, out LinearMatch match)
    {
        Span<char> characters = stackalloc char[(runs.Length / 6) + 1];
        for (int i = start; i + 6 < runs.Length; i += 2)
        {
            if (Variance(runs.Slice(i, 6), StartStop, MaximumIndividualVariance) > MaximumVariance)
            {
                continue;
            }

            // Section 2.6: a quiet zone of ten modules. Half of that tolerates a tight crop.
            float module = Sum(runs, i, 6) / 9F;
            if (!IsQuiet(runs, i - 1, module * 5))
            {
                continue;
            }

            if (TryReadFrom(runs, i + 6, characters, out int count, out int end) && TryInterpret(characters[..count], out string? text))
            {
                match = new LinearMatch(BarcodeFamily.Code93, text, "]G0", i, end, true);
                return true;
            }
        }

        match = default;
        return false;
    }

    /// <summary>
    /// Reads the characters after a start character up to the stop character and checks both check characters.
    /// </summary>
    /// <param name="runs">The runs.</param>
    /// <param name="position">The index of the first bar after the start character.</param>
    /// <param name="characters">The buffer for the symbol characters, the shift characters as a to d.</param>
    /// <param name="count">The number of data characters, without the check characters.</param>
    /// <param name="end">The index of the light run after the termination bar.</param>
    /// <returns><see langword="true"/> when the characters read and check.</returns>
    private static bool TryReadFrom(ReadOnlySpan<int> runs, int position, Span<char> characters, out int count, out int end)
    {
        count = 0;
        end = 0;
        while (position + 7 < runs.Length && count < characters.Length)
        {
            ReadOnlySpan<int> character = runs.Slice(position, 6);
            float module = Sum(runs, position, 6) / 9F;
            if (Variance(character, StartStop, MaximumIndividualVariance) <= MaximumVariance)
            {
                float termination = runs[position + 6] / module;
                if (termination < 0.5F || termination > 1.7F || !IsQuiet(runs, position + 7, module * 5))
                {
                    return false;
                }

                end = position + 7;
                break;
            }

            float best = MaximumVariance;
            int value = -1;
            for (int v = 0; v < ValueCount; v++)
            {
                float variance = Variance(character, Code93Encoder.Patterns.Slice(v * 6, 6), MaximumIndividualVariance);
                if (variance < best)
                {
                    best = variance;
                    value = v;
                }
            }

            if (value < 0)
            {
                return false;
            }

            characters[count++] = value < 43 ? AlphanumericCharacterSet.Character(value) : (char)('a' + value - 43);
            position += 6;
        }

        if (end == 0 || count < 3)
        {
            return false;
        }

        // The two check characters follow the data, and their values come from the same table.
        count -= 2;
        (int first, int second) = Code93Encoder.CheckValues(characters[..count]);
        return ValueOf(characters[count]) == first && ValueOf(characters[count + 1]) == second;
    }

    /// <summary>
    /// Reverses the full ASCII substitutions: a shift character and a letter stand for one ASCII character.
    /// </summary>
    /// <param name="characters">The data characters, the shifts as a to d.</param>
    /// <param name="text">The transmitted data.</param>
    /// <returns><see langword="true"/> when every shift character starts a valid pair.</returns>
    private static bool TryInterpret(ReadOnlySpan<char> characters, out string text)
    {
        Span<char> data = stackalloc char[characters.Length];
        int count = 0;
        text = string.Empty;
        for (int i = 0; i < characters.Length; i++)
        {
            char character = characters[i];
            if (character is >= 'a' and <= 'd')
            {
                if (i + 1 >= characters.Length || !char.IsAsciiLetterUpper(characters[i + 1]))
                {
                    return false;
                }

                int ascii = FullAscii[((character - 'a') * 26) + characters[++i] - 'A'];
                if (ascii < 0)
                {
                    return false;
                }

                data[count++] = (char)ascii;
                continue;
            }

            data[count++] = character;
        }

        text = data[..count].ToString();
        return true;
    }

    /// <summary>
    /// Returns the value of a symbol character.
    /// </summary>
    /// <param name="character">The character, a shift as a to d.</param>
    /// <returns>The value.</returns>
    private static int ValueOf(char character)
        => character is >= 'a' and <= 'd' ? character - 'a' + 43 : AlphanumericCharacterSet.Value(character);

    /// <summary>
    /// Inverts the shift pairs of <see cref="Code93ExtendedSymbology.Substitutions"/>.
    /// </summary>
    /// <returns>The ASCII character of every pair.</returns>
    private static sbyte[] CreateFullAscii()
    {
        sbyte[] map = new sbyte[4 * 26];
        map.AsSpan().Fill(-1);
        string substitutions = Code93ExtendedSymbology.Substitutions;
        for (int ascii = 0; ascii < 128; ascii++)
        {
            char shift = substitutions[ascii * 2];
            if (shift is >= 'a' and <= 'd')
            {
                map[((shift - 'a') * 26) + substitutions[(ascii * 2) + 1] - 'A'] = (sbyte)ascii;
            }
        }

        return map;
    }
}
