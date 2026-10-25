// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Reads Codablock F, the inverse of <see cref="CodablockFEncoder"/>. Every row is a Code 128 row that <see cref="Code128Reader.TryReadFrom"/> reads and
/// checks: Start A, the code set selector, the row indicator, the data columns, the row check character and the stop character. The row indicator of the
/// first row gives the row count and the indicator of every other row its index. The data columns read through the Code 128 code sets row by row, and the
/// modulo 86 check characters K1 and K2 at the end of the last row check the whole message.
/// </summary>
internal sealed class CodablockFReader : StackedReader
{
    private const int ColumnsKey = 0;
    private const int FunctionOne = 102;
    private const int FunctionTwo = 97;
    private const int FunctionThree = 96;

    /// <summary>
    /// The characters of a row before its data columns: the code set selector and the row indicator.
    /// </summary>
    private const int Header = 2;

    /// <inheritdoc/>
    public override BarcodeFamily Families => BarcodeFamily.CodablockF;

    /// <inheritdoc/>
    public override void Read(DecodeContext context, BitImage bits, bool turned)
    {
        int width = bits.Width;
        using PooledBuffer<int> runBuffer = new(width + 2);
        using PooledBuffer<int> startBuffer = new(width + 2);
        using PooledBuffer<int> valueBuffer = new((width / 6) + 2);
        Span<int> runs = runBuffer.Span;
        Span<int> starts = startBuffer.Span;
        Span<int> values = valueBuffer.Span;
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
                for (int i = 1; i + 6 < line.Length; i += 2)
                {
                    if (Code128Reader.Best(line.Slice(i, 6), CodablockFEncoder.StartA, CodablockFEncoder.StartA) < 0)
                    {
                        continue;
                    }

                    // A quiet zone of ten modules. Half of that tolerates a tight crop.
                    float module = LinearReader.Sum(line, i, 6) / 11F;
                    if (!LinearReader.IsQuiet(line, i - 1, module * 5)
                        || !Code128Reader.TryReadFrom(line, i, values, out int read, out int end)
                        || !TryAddRow(symbols[direction], values[1..read]))
                    {
                        continue;
                    }

                    float left = direction == 0 ? starts[i] : starts[count - end];
                    float right = direction == 0 ? (end < count ? starts[end] : width) : starts[count - i];
                    symbols[direction].Extend(left, right, y);
                    i = end;
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
                context.Add(BarcodeFamily.CodablockF, text, identifier, corners);
            }
        }
    }

    /// <summary>
    /// Adds the characters of a row to the votes under the row its indicator gives.
    /// </summary>
    /// <param name="symbol">The votes.</param>
    /// <param name="row">The characters after Start A: the code set selector, the row indicator and the data columns.</param>
    /// <returns><see langword="true"/> when the row is a Codablock F row.</returns>
    private static bool TryAddRow(CodewordVotes symbol, ReadOnlySpan<int> row)
    {
        int columns = row.Length - Header;
        if (columns is < CodablockFEncoder.MinimumColumns or > CodablockFEncoder.MaximumColumns
            || row[0] is not (CodablockFEncoder.Shift or CodablockFEncoder.LatchC or CodablockFEncoder.LatchB))
        {
            return false;
        }

        // The first row's indicator is the row count less two, and the indicator of every other row its index plus 42.
        int indicator = Unmap(row[1], row[0] != CodablockFEncoder.LatchC);
        int index = indicator < CodablockFEncoder.SecondRowIndicator ? 0 : indicator - CodablockFEncoder.SecondRowIndicator + 1;
        if (indicator < 0 || index >= CodablockFEncoder.MaximumRows)
        {
            return false;
        }

        for (int c = 0; c < row.Length; c++)
        {
            symbol.AddCodeword(index, c, row[c]);
        }

        symbol.AddValue(ColumnsKey, columns);
        return true;
    }

    /// <summary>
    /// Builds the message from the votes and checks it.
    /// </summary>
    /// <param name="symbol">The votes.</param>
    /// <param name="text">The data.</param>
    /// <param name="identifier">The symbology identifier.</param>
    /// <returns><see langword="true"/> when the symbol decodes and K1 and K2 hold.</returns>
    private static bool TryDecode(CodewordVotes symbol, out string text, out string identifier)
    {
        text = string.Empty;
        identifier = "]O4";
        int columns = symbol.Value(ColumnsKey);
        int selector = symbol.Codeword(0, 0);
        int indicator = symbol.Codeword(0, 1);
        if (columns < 0 || selector < 0 || indicator < 0)
        {
            return false;
        }

        int rows = Unmap(indicator, selector != CodablockFEncoder.LatchC) + 2;
        if (rows is < 2 or > CodablockFEncoder.MaximumRows)
        {
            return false;
        }

        ValueStringBuilder builder = new(stackalloc char[256]);
        try
        {
            bool gs1 = false;
            int k1 = -1;
            int k2 = -1;
            for (int row = 0; row < rows; row++)
            {
                // The code set selector after Start A: Shift selects code set A, Code C code set C and Code B code set B.
                int set = symbol.Codeword(row, 0) switch
                {
                    CodablockFEncoder.Shift => 0,
                    CodablockFEncoder.LatchB => 1,
                    CodablockFEncoder.LatchC => 2,
                    _ => -1,
                };

                if (set < 0)
                {
                    return false;
                }

                int dataColumns = row == rows - 1 ? columns - 2 : columns;
                bool shifted = false;
                bool functionFour = false;
                for (int c = 0; c < dataColumns; c++)
                {
                    int value = symbol.Codeword(row, Header + c);
                    if (value < 0)
                    {
                        return false;
                    }

                    int current = shifted ? 1 - set : set;
                    shifted = false;
                    if (value == FunctionOne)
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
                            case CodablockFEncoder.LatchB:
                                set = 1;
                                break;
                            case CodablockFEncoder.LatchA:
                                set = 0;
                                break;
                            default:
                                return false;
                        }

                        continue;
                    }

                    switch (value)
                    {
                        case < FunctionThree:
                            int ascii = current == 0 ? (value < 64 ? value + 32 : value - 64) : value + 32;
                            builder.Append((char)(functionFour ? ascii + 128 : ascii));
                            functionFour = false;
                            break;
                        case FunctionThree:
                        case FunctionTwo:
                            break;
                        case CodablockFEncoder.Shift:
                            shifted = true;
                            break;
                        case CodablockFEncoder.LatchC:
                            set = 2;
                            break;
                        case CodablockFEncoder.LatchB when current == 0:
                            set = 1;
                            break;
                        case CodablockFEncoder.LatchA when current == 1:
                            set = 0;
                            break;
                        case CodablockFEncoder.LatchB:
                        case CodablockFEncoder.LatchA:
                            // FNC4 adds 128 to the next data character.
                            functionFour = true;
                            break;
                        default:
                            return false;
                    }
                }

                if (row == rows - 1)
                {
                    // K1 and K2 take the code set the last row ends in.
                    k1 = Unmap(symbol.Codeword(row, Header + columns - 2), set != 2);
                    k2 = Unmap(symbol.Codeword(row, Header + columns - 1), set != 2);
                }
            }

            int sum1 = 0;
            int sum2 = 0;
            ReadOnlySpan<char> message = builder.AsSpan();
            for (int p = 0; p < message.Length; p++)
            {
                sum1 = (sum1 + ((p + 1) * message[p])) % CodablockFEncoder.CheckModulus;
                sum2 = (sum2 + (p * message[p])) % CodablockFEncoder.CheckModulus;
            }

            if (sum1 != k1 || sum2 != k2)
            {
                return false;
            }

            text = builder.ToString();
            identifier = gs1 ? "]O5" : "]O4";
            return text.Length > 0;
        }
        finally
        {
            builder.Dispose();
        }
    }

    /// <summary>
    /// Maps a character value back to a row indicator or check value of 0 to 85, the inverse of the mapping of <see cref="CodablockFEncoder"/>: in code sets A
    /// and B the characters 64 to 95 carry 0 to 31, 0 to 15 carry 32 to 47 and 26 to 63 carry 48 to 85, and in code set C a value carries itself.
    /// </summary>
    /// <param name="value">The character value.</param>
    /// <param name="alphabetic">Whether the row is in code set A or B rather than C.</param>
    /// <returns>The value, or -1 when the character carries none.</returns>
    private static int Unmap(int value, bool alphabetic)
    {
        if (value < 0)
        {
            return -1;
        }

        if (!alphabetic)
        {
            return value < CodablockFEncoder.CheckModulus ? value : -1;
        }

        return value switch
        {
            >= 64 and < 96 => value - 64,
            < 16 => value + 32,
            >= 26 and < 64 => value + 22,
            _ => -1,
        };
    }
}
