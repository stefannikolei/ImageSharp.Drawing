// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Reads Code 39, ISO/IEC 16388, and the symbols that carry their data in it: PZN and Code 32. Each symbol character is five bars and four spaces, three of
/// them wide, followed by an inter-character gap. The reader classifies the elements of each character as narrow or wide and looks the pattern up in
/// <see cref="Code39Encoder.Patterns"/>. The start and stop character is the same, so the reader needs a quiet zone on both sides.
/// </summary>
internal sealed class Code39Reader : LinearReader
{
    /// <summary>
    /// The pattern index of the start and stop character.
    /// </summary>
    private const int StartStop = 43;

    /// <summary>
    /// The character index of each nine element pattern, the first element in the lowest bit and a set bit for a wide element, or -1.
    /// </summary>
    private static readonly sbyte[] Characters = CreateCharacters();

    /// <summary>
    /// The ASCII character of each full ASCII pair of Table A.2, at index (prefix * 26) + letter, where the prefixes are $, %, / and +, or -1.
    /// </summary>
    private static readonly sbyte[] FullAscii = CreateFullAscii();

    /// <inheritdoc/>
    public override BarcodeFamily Families => BarcodeFamily.Code39 | BarcodeFamily.Pzn | BarcodeFamily.Code32;

    /// <inheritdoc/>
    public override bool TryRead(ReadOnlySpan<int> runs, int start, DecodeContext context, out LinearMatch match)
    {
        ValueStringBuilder data = new(stackalloc char[64]);
        try
        {
            for (int i = start; i + 9 < runs.Length; i += 2)
            {
                if (ReadCharacter(runs.Slice(i, 9)) != StartStop)
                {
                    continue;
                }

                // Section 4.4 d) asks for a quiet zone of ten modules. Half a character tolerates a tight crop.
                int width = Sum(runs, i, 9);
                if (!IsQuiet(runs, i - 1, width / 2F))
                {
                    continue;
                }

                data.Clear();
                if (TryReadFrom(runs, i + 10, width, ref data, out int end) && TryInterpret(data.AsSpan(), context, out match))
                {
                    match = new LinearMatch(match.Family, match.Text, match.Identifier, i, end, match.CheckedData);
                    return true;
                }
            }
        }
        finally
        {
            data.Dispose();
        }

        match = default;
        return false;
    }

    /// <summary>
    /// Reads one Code 39 character from nine runs.
    /// </summary>
    /// <param name="runs">The nine runs, bar first.</param>
    /// <returns>The character index of <see cref="Code39Encoder.Patterns"/>, or -1.</returns>
    public static int ReadCharacter(ReadOnlySpan<int> runs)
    {
        int mask = WideElements(runs, 3);
        return mask < 0 ? -1 : Characters[mask];
    }

    /// <summary>
    /// Reads the data characters after a start character up to the stop character.
    /// </summary>
    /// <param name="runs">The runs.</param>
    /// <param name="position">The index of the first bar after the start character and its gap.</param>
    /// <param name="width">The width of the start character in pixels.</param>
    /// <param name="data">The characters read.</param>
    /// <param name="end">The index of the light run after the stop character.</param>
    /// <returns><see langword="true"/> when a stop character with its quiet zone ends the data.</returns>
    private static bool TryReadFrom(ReadOnlySpan<int> runs, int position, int width, ref ValueStringBuilder data, out int end)
    {
        end = 0;

        // The gap after the start character is narrow: section 4.4 c) gives it at most 5.3 X at an X of under 0.287 mm, and at most three narrow
        // elements otherwise. Half a character covers both.
        if (runs[position - 1] > width / 2)
        {
            return false;
        }

        while (position + 9 < runs.Length)
        {
            int value = ReadCharacter(runs.Slice(position, 9));
            if (value < 0)
            {
                return false;
            }

            // Every character of a symbol is about as wide as the start character.
            int characterWidth = Sum(runs, position, 9);
            if (characterWidth * 4 < width * 3 || characterWidth * 3 > width * 4)
            {
                return false;
            }

            if (value == StartStop)
            {
                if (data.Length == 0 || !IsQuiet(runs, position + 9, width / 2F))
                {
                    return false;
                }

                end = position + 9;
                return true;
            }

            data.Append(AlphanumericCharacterSet.Character(value));
            if (runs[position + 9] > width / 2)
            {
                return false;
            }

            position += 10;
        }

        return false;
    }

    /// <summary>
    /// Turns the characters into the symbol a scanner transmits: a PZN, a Code 32 or a Code 39 symbol in its standard or its full ASCII mode.
    /// </summary>
    /// <param name="data">The data characters.</param>
    /// <param name="context">The decode.</param>
    /// <param name="match">The match, without its run indices.</param>
    /// <returns><see langword="true"/> when the options name a family the data reads as.</returns>
    private static bool TryInterpret(ReadOnlySpan<char> data, DecodeContext context, out LinearMatch match)
    {
        // A PZN is the identifier of ISO/IEC 15418, a minus sign, and seven or eight digits whose last digit checks the others.
        if (context.Wants(BarcodeFamily.Pzn)
            && data.Length is 8 or 9
            && data[0] == '-'
            && !data[1..].ContainsAnyExceptInRange('0', '9')
            && PznData.CheckDigit(data[1..^1]) == data[^1] - '0')
        {
            match = new LinearMatch(BarcodeFamily.Pzn, data.ToString(), "]A0", 0, 0, true);
            return true;
        }

        // A Code 32 symbol is six base 32 characters that stand for nine digits, the first a zero and the last a check digit.
        if (context.Wants(BarcodeFamily.Code32) && data.Length == Code32Symbology.Base32Length && TryReadCode32(data, out string? digits))
        {
            match = new LinearMatch(BarcodeFamily.Code32, digits, "]A0", 0, 0, true);
            return true;
        }

        if (!context.Wants(BarcodeFamily.Code39))
        {
            match = default;
            return false;
        }

        if (!context.Options.Code39FullAscii)
        {
            match = new LinearMatch(BarcodeFamily.Code39, data.ToString(), "]A0", 0, 0, false);
            return true;
        }

        // Annex A.3: a prefix of $, %, / or + and a letter stand for one ASCII character. A pair that Table A.2 does not list reads as the two characters.
        Span<char> text = stackalloc char[data.Length];
        int count = 0;
        for (int i = 0; i < data.Length; i++)
        {
            int prefix = "$%/+".IndexOf(data[i]);
            if (prefix >= 0 && i + 1 < data.Length && char.IsAsciiLetterUpper(data[i + 1]))
            {
                int ascii = FullAscii[(prefix * 26) + data[i + 1] - 'A'];
                if (ascii >= 0)
                {
                    text[count++] = (char)ascii;
                    i++;
                    continue;
                }
            }

            text[count++] = data[i];
        }

        match = new LinearMatch(BarcodeFamily.Code39, text[..count].ToString(), "]A4", 0, 0, false);
        return true;
    }

    /// <summary>
    /// Reads the nine digits of a Code 32 symbol from its six base 32 characters.
    /// </summary>
    /// <param name="data">The six characters, the most significant first.</param>
    /// <param name="digits">The nine digits.</param>
    /// <returns><see langword="true"/> when the characters are base 32 and the digits an AIC code with a correct check digit.</returns>
    private static bool TryReadCode32(ReadOnlySpan<char> data, out string digits)
    {
        digits = string.Empty;
        long number = 0;
        foreach (char character in data)
        {
            int value = Code32Symbology.Base32Characters.IndexOf(character);
            if (value < 0)
            {
                return false;
            }

            number = (number * Code32Symbology.Base32Characters.Length) + value;
        }

        if (number >= 1_000_000_000)
        {
            return false;
        }

        Span<char> code = stackalloc char[Code32Symbology.Digits + 1];
        for (int i = code.Length - 1; i >= 0; i--)
        {
            code[i] = (char)('0' + (number % 10));
            number /= 10;
        }

        if (code[0] != '0' || Code32Symbology.CheckDigit(code[..Code32Symbology.Digits]) != code[^1] - '0')
        {
            return false;
        }

        digits = code.ToString();
        return true;
    }

    /// <summary>
    /// Inverts <see cref="Code39Encoder.Patterns"/>: the nine element bits of each pattern, the first element in the highest bit, become a mask with the first
    /// element in the lowest bit.
    /// </summary>
    /// <returns>The character index of every mask.</returns>
    private static sbyte[] CreateCharacters()
    {
        sbyte[] characters = new sbyte[512];
        characters.AsSpan().Fill(-1);
        ReadOnlySpan<ushort> patterns = Code39Encoder.Patterns;
        for (int v = 0; v < patterns.Length; v++)
        {
            int elements = patterns[v] >> 1;
            int mask = 0;
            for (int e = 0; e < 9; e++)
            {
                if (((elements >> (8 - e)) & 1) != 0)
                {
                    mask |= 1 << e;
                }
            }

            characters[mask] = (sbyte)v;
        }

        return characters;
    }

    /// <summary>
    /// Inverts the full ASCII substitutions of <see cref="Code39ExtendedSymbology.Substitutions"/>.
    /// </summary>
    /// <returns>The ASCII character of every pair.</returns>
    private static sbyte[] CreateFullAscii()
    {
        sbyte[] map = new sbyte[4 * 26];
        map.AsSpan().Fill(-1);
        string substitutions = Code39ExtendedSymbology.Substitutions;
        for (int ascii = 0; ascii < 128; ascii++)
        {
            char prefix = substitutions[ascii * 2];
            int index = "$%/+".IndexOf(prefix);
            if (index >= 0)
            {
                map[(index * 26) + substitutions[(ascii * 2) + 1] - 'A'] = (sbyte)ascii;
            }
        }

        return map;
    }
}
