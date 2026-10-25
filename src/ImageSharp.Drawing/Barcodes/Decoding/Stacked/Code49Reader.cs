// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Reads Code 49, AIM USS-49, the inverse of <see cref="Code49Encoder"/>. A row is a start pattern, four symbol characters of 16 modules and a stop bar, and
/// the parity of its four symbol characters, Table 2, gives the row number; the last row is all even. Every row read must hold its row check character. The
/// last row gives the row count and the starting mode, the symbol check characters check the symbol, and the code characters read through the shifts and the
/// numeric encodation method of section 2.2.
/// </summary>
internal sealed class Code49Reader : StackedReader
{
    private const int RowRuns = 35;
    private const int RowModules = 70;
    private const int CharacterRuns = 8;
    private const int CharacterModules = 16;
    private const int ModeCount = 7;
    private const int Fnc1 = 45;

    /// <summary>
    /// The key the votes keep the last row under, after the seven rows that Table 2 numbers by their parity.
    /// </summary>
    private const int LastRow = 7;

    /// <summary>
    /// The symbol character value of every pattern, keyed by the pattern bits, with bit 12 set for a pattern of odd parity.
    /// </summary>
    private static readonly Dictionary<int, int> Characters = CreateCharacters();

    /// <summary>
    /// The ASCII character of every code character value 0 to 48 after no shift, Shift 1 and Shift 2, by Table 7, or -1 for none.
    /// </summary>
    private static readonly int[] Ascii = CreateAscii();

    /// <inheritdoc/>
    public override BarcodeFamily Families => BarcodeFamily.Code49;

    /// <inheritdoc/>
    public override void Read(DecodeContext context, BitImage bits, bool turned)
    {
        int width = bits.Width;
        using PooledBuffer<int> runBuffer = new(width + 2);
        using PooledBuffer<int> startBuffer = new(width + 2);
        Span<int> runs = runBuffer.Span;
        Span<int> starts = startBuffer.Span;
        Span<int> codes = stackalloc int[Code49Encoder.CodeCharactersPerRow];
        CodewordVotes[] symbols = [new CodewordVotes(), new CodewordVotes()];
        int step = context.Options.TryHarder ? 1 : Math.Max(1, bits.Height / 128);
        for (int y = 0; y < bits.Height; y += step)
        {
            int count = LinearScanner.ToRuns(bits.Row(y), runs, starts);
            for (int direction = 0; direction < 2; direction++)
            {
                if (direction == 1)
                {
                    runs[..count].Reverse();
                }

                ReadOnlySpan<int> line = runs[..count];
                for (int i = 1; i + RowRuns <= line.Length; i += 2)
                {
                    if (TryReadRow(line, i, codes, out int row, out float module))
                    {
                        CodewordVotes symbol = symbols[direction];
                        for (int c = 0; c < codes.Length; c++)
                        {
                            symbol.AddCodeword(row, c, codes[c]);
                        }

                        float left = direction == 0 ? starts[i] : starts[count - (i + RowRuns)];
                        symbol.Extend(left, left + (module * RowModules), y);
                        i += RowRuns - 1;
                    }
                }
            }
        }

        for (int direction = 0; direction < 2; direction++)
        {
            CodewordVotes symbol = symbols[direction];
            if (TryDecode(symbol, out string? text, out string? identifier))
            {
                PointF topLeft = Map(symbol.Left, symbol.Top, bits, turned);
                PointF topRight = Map(symbol.Right, symbol.Top, bits, turned);
                PointF bottomRight = Map(symbol.Right, symbol.Bottom + 1, bits, turned);
                PointF bottomLeft = Map(symbol.Left, symbol.Bottom + 1, bits, turned);
                PointF[] corners = direction == 0 ? [topLeft, topRight, bottomRight, bottomLeft] : [bottomRight, bottomLeft, topLeft, topRight];
                context.Add(BarcodeFamily.Code49, text, identifier, corners);
            }
        }
    }

    /// <summary>
    /// Reads a row from its start pattern: a bar and a space of one module, four symbol characters and a stop bar of four modules, 35 runs over 70 modules.
    /// </summary>
    /// <param name="runs">The runs.</param>
    /// <param name="i">The index of the start bar.</param>
    /// <param name="codes">The eight code characters to write.</param>
    /// <param name="row">The row number from zero, or <see cref="LastRow"/> for the last row.</param>
    /// <param name="module">The module width.</param>
    /// <returns><see langword="true"/> when the row reads and holds its row check character.</returns>
    private static bool TryReadRow(ReadOnlySpan<int> runs, int i, Span<int> codes, out int row, out float module)
    {
        row = -1;
        module = LinearReader.Sum(runs, i, RowRuns) / (float)RowModules;

        // Section 3.4: ten modules before the start pattern and one after the stop pattern. Half of the ten tolerates a tight crop.
        if (!LinearReader.IsQuiet(runs, i - 1, module * 5) || !LinearReader.IsQuiet(runs, i + RowRuns, module * 0.8F))
        {
            return false;
        }

        float startBar = runs[i] / module;
        float startSpace = runs[i + 1] / module;
        float stop = runs[i + RowRuns - 1] / module;
        if (startBar is < 0.5F or > 1.6F || startSpace is < 0.5F or > 1.6F || stop is < 3F or > 5F)
        {
            return false;
        }

        Span<int> widths = stackalloc int[CharacterRuns];
        int parity = 0;
        int sum = 0;
        for (int c = 0; c < Code49Encoder.SymbolCharactersPerRow; c++)
        {
            if (!DataBarCharacter.TryModules(runs.Slice(i + 2 + (c * CharacterRuns), CharacterRuns), CharacterModules, widths)
                || !Characters.TryGetValue(Pack(widths), out int entry))
            {
                return false;
            }

            // Formula 1: W = 49 C1 + C2.
            int value = entry & 0xFFF;
            codes[c * 2] = value / Code49Encoder.RowModulus;
            codes[(c * 2) + 1] = value % Code49Encoder.RowModulus;
            parity = (parity << 1) | (entry >> 12);
        }

        // Section 2.1.2: the last code character of the row is the modulo 49 sum of the other seven.
        for (int c = 0; c < 7; c++)
        {
            sum += codes[c];
        }

        if (sum % Code49Encoder.RowModulus != codes[7])
        {
            return false;
        }

        row = parity == 0 ? LastRow : Code49Encoder.RowParity.IndexOf((byte)parity);
        return row >= 0;
    }

    /// <summary>
    /// Builds the symbol from the votes, checks it and decodes it.
    /// </summary>
    /// <param name="symbol">The votes.</param>
    /// <param name="text">The data.</param>
    /// <param name="identifier">The symbology identifier.</param>
    /// <returns><see langword="true"/> when the symbol decodes.</returns>
    private static bool TryDecode(CodewordVotes symbol, out string text, out string identifier)
    {
        text = string.Empty;
        identifier = "]T0";
        int perRow = Code49Encoder.CodeCharactersPerRow;

        // Section 2.1.3: the seventh code character of the last row is 7 (r - 2) + M.
        int rowCount = symbol.Codeword(LastRow, 6);
        if (rowCount < 0)
        {
            return false;
        }

        int rows = (rowCount / ModeCount) + Code49Encoder.MinimumRows;
        int mode = rowCount % ModeCount;
        if (rows > Code49Encoder.MaximumRows)
        {
            return false;
        }

        Span<int> codes = stackalloc int[rows * perRow];
        for (int r = 0; r < rows; r++)
        {
            int key = r < rows - 1 ? r : LastRow;
            for (int c = 0; c < perRow; c++)
            {
                codes[(r * perRow) + c] = symbol.Codeword(key, c);
                if (codes[(r * perRow) + c] < 0)
                {
                    return false;
                }
            }
        }

        // The check characters the data positions give must be the ones the symbol holds.
        Span<int> expected = stackalloc int[codes.Length];
        codes.CopyTo(expected);
        Code49Encoder.WriteChecks(expected, mode);
        if (!codes.SequenceEqual(expected))
        {
            return false;
        }

        Span<int> data = stackalloc int[codes.Length];
        int count = 0;
        for (int r = 0; r < rows; r++)
        {
            int length = Code49Encoder.DataCharacters(r, rows);
            codes.Slice(r * perRow, length).CopyTo(data[count..]);
            count += length;
        }

        return TryInterpret(data[..count], mode, out text, out identifier);
    }

    /// <summary>
    /// Turns the data code characters into text through the shifts of Table 6 and the numeric encodation method of section 2.2.2.
    /// </summary>
    /// <param name="values">The data code characters.</param>
    /// <param name="mode">The starting mode of Table 8.</param>
    /// <param name="text">The data.</param>
    /// <param name="identifier">The symbology identifier.</param>
    /// <returns><see langword="true"/> when the values are valid.</returns>
    private static bool TryInterpret(ReadOnlySpan<int> values, int mode, out string text, out string identifier)
    {
        text = string.Empty;
        identifier = "]T0";

        // Table 8: the alphanumeric set, the numeric method, or a Shift 1 or Shift 2 before the first character.
        int shift = mode switch
        {
            4 => 1,
            5 => 2,
            _ => 0,
        };

        bool numeric = mode == 2;
        if (mode is not (0 or 2 or 4 or 5))
        {
            return false;
        }

        ValueStringBuilder builder = new(stackalloc char[128]);
        try
        {
            bool gs1 = false;
            int i = 0;
            while (i < values.Length)
            {
                int value = values[i];
                if (numeric)
                {
                    // A numeric run ends at the numeric shift, which also pads the symbol.
                    int end = values[i..].IndexOf(Code49Encoder.NumericShift);
                    end = end < 0 ? values.Length : i + end;
                    if (!TryAppendNumeric(values[i..end], ref builder))
                    {
                        return false;
                    }

                    numeric = false;
                    i = end + 1;
                    continue;
                }

                i++;
                switch (value)
                {
                    case Code49Encoder.NumericShift when shift == 0:
                        numeric = true;
                        continue;
                    case Code49Encoder.ShiftOne when shift == 0:
                        shift = 1;
                        continue;
                    case Code49Encoder.ShiftTwo when shift == 0:
                        shift = 2;
                        continue;
                    case Fnc1 when shift == 0:
                        // FNC1 in the first position marks GS1 data, and any later one is the group separator.
                        if (builder.Length == 0 && !gs1)
                        {
                            gs1 = true;
                        }
                        else
                        {
                            builder.Append('\u001D');
                        }

                        continue;
                    case > Fnc1 and < Code49Encoder.NumericShift when shift == 0:
                        // FNC2 and FNC3 carry no data.
                        continue;
                }

                int ascii = value < Code49Encoder.NumericShift ? Ascii[(shift * Code49Encoder.RowModulus) + value] : -1;
                if (ascii < 0)
                {
                    return false;
                }

                builder.Append((char)ascii);
                shift = 0;
            }

            text = builder.ToString();
            identifier = gs1 ? "]T1" : "]T0";
            return text.Length > 0;
        }
        finally
        {
            builder.Dispose();
        }
    }

    /// <summary>
    /// Appends the digits of a numeric run, section 2.2.2: five digits for every three code characters, or four with 100,000 added, three digits for a last
    /// two code characters and one digit for a last one.
    /// </summary>
    /// <param name="values">The code characters of the run, each 0 to 47.</param>
    /// <param name="builder">The text.</param>
    /// <returns><see langword="true"/> when the run is valid.</returns>
    private static bool TryAppendNumeric(ReadOnlySpan<int> values, ref ValueStringBuilder builder)
    {
        int i = 0;
        while (i < values.Length)
        {
            int length = Math.Min(3, values.Length - i);
            int number = 0;
            for (int k = 0; k < length; k++)
            {
                number = (number * Code49Encoder.NumericBase) + values[i + k];
            }

            i += length;
            int digits = length switch
            {
                1 => 1,
                2 => 3,
                _ => number >= Code49Encoder.FourDigitOffset ? 4 : 5,
            };

            if (digits == 4)
            {
                number -= Code49Encoder.FourDigitOffset;
            }

            if (number >= Pow10(digits))
            {
                return false;
            }

            for (int divisor = Pow10(digits - 1); divisor > 0; divisor /= 10)
            {
                builder.Append((char)('0' + ((number / divisor) % 10)));
            }
        }

        return true;
    }

    /// <summary>
    /// Returns ten to a power.
    /// </summary>
    /// <param name="power">The power, 0 to 5.</param>
    /// <returns>The value.</returns>
    private static int Pow10(int power)
    {
        int value = 1;
        for (int k = 0; k < power; k++)
        {
            value *= 10;
        }

        return value;
    }

    /// <summary>
    /// Packs the module widths of a symbol character into its pattern bits, a set bit for a bar module, from the most significant bit.
    /// </summary>
    /// <param name="widths">The eight widths, bar first.</param>
    /// <returns>The pattern bits.</returns>
    private static int Pack(ReadOnlySpan<int> widths)
    {
        int bits = 0;
        for (int e = 0; e < widths.Length; e++)
        {
            for (int m = 0; m < widths[e]; m++)
            {
                bits = (bits << 1) | ((e & 1) == 0 ? 1 : 0);
            }
        }

        return bits;
    }

    /// <summary>
    /// Collects the symbol character value of every pattern of Appendix F.
    /// </summary>
    /// <returns>The values, keyed by the pattern bits, with bit 12 set for odd parity.</returns>
    private static Dictionary<int, int> CreateCharacters()
    {
        ReadOnlySpan<ushort> even = Code49Patterns.Even;
        ReadOnlySpan<ushort> odd = Code49Patterns.Odd;
        Dictionary<int, int> characters = new(even.Length + odd.Length);
        for (int value = 0; value < even.Length; value++)
        {
            characters[even[value]] = value;
            characters[odd[value]] = value | (1 << 12);
        }

        return characters;
    }

    /// <summary>
    /// Inverts Table 7: the ASCII character of every shift and code character value.
    /// </summary>
    /// <returns>The characters, 49 for each shift, or -1 for none.</returns>
    private static int[] CreateAscii()
    {
        ReadOnlySpan<byte> values = Code49Encoder.AsciiValues;
        ReadOnlySpan<byte> shifts = Code49Encoder.AsciiShifts;
        int[] ascii = new int[3 * Code49Encoder.RowModulus];
        ascii.AsSpan().Fill(-1);
        for (int c = 0; c < values.Length; c++)
        {
            int index = (shifts[c] * Code49Encoder.RowModulus) + values[c];
            if (ascii[index] < 0)
            {
                ascii[index] = c;
            }
        }

        return ascii;
    }
}
