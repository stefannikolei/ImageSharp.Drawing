// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Reads Code 16K, AIM USS-16K, the inverse of <see cref="Code16KEncoder"/>. A row is a start pattern, a guard bar, five symbol characters of the Code 128
/// patterns and a stop pattern, and the start and stop patterns of Table 6 give the row number. The first symbol character gives the row count and the
/// starting mode, the last two are the modulo 107 check characters, and the values between them read through the code sets, shifts and function characters
/// of Table 2.
/// </summary>
internal sealed class Code16KReader : StackedReader
{
    private const int CharactersPerRow = 5;
    private const int MinimumRows = 2;
    private const int MaximumRows = 16;
    private const float MaximumVariance = 0.25F;
    private const float MaximumIndividualVariance = 0.7F;
    private const int Pad = 103;

    /// <summary>
    /// The element widths of the symbol character values 0 to 106.
    /// </summary>
    private static readonly int[] Widths = CreateWidths();

    /// <summary>
    /// The element widths of the eight start patterns, bar first. The stop patterns have the same widths, space first.
    /// </summary>
    private static readonly int[] StartWidths = CreateStartWidths();

    /// <inheritdoc/>
    public override BarcodeFamily Families => BarcodeFamily.Code16K;

    /// <inheritdoc/>
    public override void Read(DecodeContext context, BitImage bits, bool turned)
    {
        int width = bits.Width;
        using PooledBuffer<int> runBuffer = new(width + 2);
        using PooledBuffer<int> startBuffer = new(width + 2);
        Span<int> runs = runBuffer.Span;
        Span<int> starts = startBuffer.Span;
        Span<int> values = stackalloc int[CharactersPerRow];
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
                for (int i = 1; i + 39 <= line.Length; i += 2)
                {
                    if (TryReadRow(line, i, values, out int row, out float module))
                    {
                        CodewordVotes symbol = symbols[direction];
                        for (int c = 0; c < CharactersPerRow; c++)
                        {
                            symbol.AddCodeword(row, c, values[c]);
                        }

                        float left = direction == 0 ? starts[i] : starts[count - (i + 39)];
                        float right = left + (module * 70);
                        symbol.Extend(left, right, y);
                        i += 38;
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
                context.Add(BarcodeFamily.Code16K, text, identifier, corners);
            }
        }
    }

    /// <summary>
    /// Reads a row from its start pattern: the start pattern, the guard bar, five symbol characters and the stop pattern, 39 runs over 70 modules.
    /// </summary>
    /// <param name="runs">The runs.</param>
    /// <param name="i">The index of the first bar of the start pattern.</param>
    /// <param name="values">The five values to write.</param>
    /// <param name="row">The row number.</param>
    /// <param name="module">The module width.</param>
    /// <returns><see langword="true"/> when the row reads.</returns>
    private static bool TryReadRow(ReadOnlySpan<int> runs, int i, Span<int> values, out int row, out float module)
    {
        row = -1;
        module = LinearReader.Sum(runs, i, 39) / 70F;

        // Section 3.4: ten modules before the start pattern. Half of that tolerates a tight crop.
        if (!LinearReader.IsQuiet(runs, i - 1, module * 5) || !LinearReader.IsQuiet(runs, i + 39, module * 0.8F))
        {
            return false;
        }

        int start = Best(runs.Slice(i, 4), StartWidths, 4, 8);
        float guard = runs[i + 4] / module;
        int stop = Best(runs.Slice(i + 35, 4), StartWidths, 4, 8);
        if (start < 0 || stop < 0 || guard < 0.5F || guard > 1.6F)
        {
            return false;
        }

        for (int c = 0; c < CharactersPerRow; c++)
        {
            values[c] = Best(runs.Slice(i + 5 + (c * 6), 6), Widths, 6, Widths.Length / 6);
            if (values[c] < 0)
            {
                return false;
            }
        }

        // Table 6: the start pattern is the row number modulo 8, and the stop pattern tells the first eight rows from the second.
        ReadOnlySpan<byte> stops = Code16KEncoder.RowStopValues;
        for (int r = start; r < MaximumRows; r += 8)
        {
            if (stops[r] == stop)
            {
                row = r;
                return true;
            }
        }

        return false;
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
        identifier = "]K0";
        int first = symbol.Codeword(0, 0);
        if (first < 0)
        {
            return false;
        }

        // Section 2.3.1: the starting symbol character is 7 (r - 2) + m.
        int rows = (first / 7) + MinimumRows;
        int mode = first % 7;
        if (rows > MaximumRows)
        {
            return false;
        }

        int total = rows * CharactersPerRow;
        Span<int> values = stackalloc int[total];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < CharactersPerRow; c++)
            {
                values[(r * CharactersPerRow) + c] = symbol.Codeword(r, c);
                if (values[(r * CharactersPerRow) + c] < 0)
                {
                    return false;
                }
            }
        }

        // Section 2.6: C1 weights the symbol characters from two upward, C2 from one upward and takes C1 as its last term.
        int c1 = 0;
        int c2 = 0;
        for (int j = 0; j < total - 2; j++)
        {
            c1 += (j + 2) * values[j];
            c2 += (j + 1) * values[j];
        }

        c1 %= Code16KEncoder.Modulus;
        c2 = (c2 + ((total - 1) * c1)) % Code16KEncoder.Modulus;
        if (values[total - 2] != c1 || values[total - 1] != c2)
        {
            return false;
        }

        return TryInterpret(values[1..(total - 2)], mode, out text, out identifier);
    }

    /// <summary>
    /// Turns the data values into text through the code sets of Table 2.
    /// </summary>
    /// <param name="values">The data values.</param>
    /// <param name="mode">The starting mode of Table 3.</param>
    /// <param name="text">The data.</param>
    /// <param name="identifier">The symbology identifier.</param>
    /// <returns><see langword="true"/> when the values are valid.</returns>
    private static bool TryInterpret(ReadOnlySpan<int> values, int mode, out string text, out string identifier)
    {
        ValueStringBuilder builder = new(stackalloc char[128]);
        try
        {
            // Table 3: code set A, B or C, code set B or C with an implied FNC1, and code set C with an implied Shift B or Double Shift B.
            int set = mode switch
            {
                0 => 0,
                1 or 3 => 1,
                _ => 2,
            };

            bool gs1 = mode is 3 or 4;
            int shifted = mode switch
            {
                5 => 1,
                6 => 2,
                _ => 0,
            };

            int shiftSet = 1;
            bool extended = false;
            bool fnc4Next = false;
            for (int i = 0; i < values.Length; i++)
            {
                int value = values[i];
                int current = set;
                if (shifted > 0)
                {
                    current = shiftSet;
                    shifted--;
                }

                if (value == Pad)
                {
                    continue;
                }

                if (value == 102)
                {
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
                }

                if (current == 2)
                {
                    switch (value)
                    {
                        case < 100:
                            builder.Append((char)('0' + (value / 10)));
                            builder.Append((char)('0' + (value % 10)));
                            break;
                        case 100:
                            set = 1;
                            break;
                        case 101:
                            set = 0;
                            break;
                        default:
                            // Shift B, Double Shift B and Triple Shift B: one, two or three characters in code set B.
                            shifted = value - 103;
                            shiftSet = 1;
                            break;
                    }

                    continue;
                }

                if (value < 96)
                {
                    int ascii = current == 0 ? (value < 64 ? value + 32 : value - 64) : value + 32;
                    builder.Append((char)(extended ^ fnc4Next ? ascii + 128 : ascii));
                    fnc4Next = false;
                    continue;
                }

                switch (value)
                {
                    case 96:
                    case 97:
                        break;
                    case 98:
                        shifted = 1;
                        shiftSet = 1 - current;
                        break;
                    case 99:
                        set = 2;
                        break;
                    case 100 when current == 0:
                        set = 1;
                        break;
                    case 101 when current == 1:
                        set = 0;
                        break;
                    case 100:
                    case 101:
                        // FNC4: once for the next character, twice in a row for the characters until the next pair.
                        if (fnc4Next)
                        {
                            extended = !extended;
                            fnc4Next = false;
                        }
                        else
                        {
                            fnc4Next = true;
                        }

                        break;
                    case 104:
                        shifted = 2;
                        shiftSet = 1 - current;
                        break;
                    default:
                        // Double Shift C and Triple Shift C: two or three digit pairs in code set C.
                        shifted = value - 103;
                        shiftSet = 2;
                        break;
                }
            }

            text = builder.ToString();
            identifier = gs1 ? "]K1" : "]K0";
            return text.Length > 0;
        }
        finally
        {
            builder.Dispose();
        }
    }

    /// <summary>
    /// Returns the pattern of a table that best matches runs.
    /// </summary>
    /// <param name="runs">The runs.</param>
    /// <param name="widths">The patterns, each its element widths.</param>
    /// <param name="elements">The elements of a pattern.</param>
    /// <param name="count">The number of patterns.</param>
    /// <returns>The index of the pattern, or -1.</returns>
    private static int Best(ReadOnlySpan<int> runs, ReadOnlySpan<int> widths, int elements, int count)
    {
        float best = MaximumVariance;
        int index = -1;
        for (int v = 0; v < count; v++)
        {
            float variance = LinearReader.Variance(runs, widths.Slice(v * elements, elements), MaximumIndividualVariance);
            if (variance < best)
            {
                best = variance;
                index = v;
            }
        }

        return index;
    }

    /// <summary>
    /// Unpacks the symbol character patterns: six widths of four bits each, the first element in the highest bits.
    /// </summary>
    /// <returns>The widths of the values 0 to 106.</returns>
    private static int[] CreateWidths()
    {
        ReadOnlySpan<uint> patterns = Code16KEncoder.Patterns;
        int[] widths = new int[(patterns.Length + 1) * 6];
        for (int v = 0; v <= patterns.Length; v++)
        {
            uint pattern = v < patterns.Length ? patterns[v] : Code16KEncoder.TripleShiftPattern;
            for (int e = 0; e < 6; e++)
            {
                widths[(v * 6) + e] = (int)((pattern >> ((5 - e) * 4)) & 0xF);
            }
        }

        return widths;
    }

    /// <summary>
    /// Unpacks the start patterns: four widths of four bits each.
    /// </summary>
    /// <returns>The widths of the eight patterns.</returns>
    private static int[] CreateStartWidths()
    {
        ReadOnlySpan<ushort> patterns = Code16KEncoder.StartPatterns;
        int[] widths = new int[patterns.Length * 4];
        for (int v = 0; v < patterns.Length; v++)
        {
            for (int e = 0; e < 4; e++)
            {
                widths[(v * 4) + e] = (patterns[v] >> ((3 - e) * 4)) & 0xF;
            }
        }

        return widths;
    }
}
