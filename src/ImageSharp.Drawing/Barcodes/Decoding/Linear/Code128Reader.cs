// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Reads Code 128, ISO/IEC 15417. Each symbol character is three bars and three spaces over eleven modules. The reader matches each character against the
/// patterns of <see cref="Code128Encoder.Patterns"/>, checks the modulo 103 check character and applies the code sets, the shifts and the function characters
/// of section 4.3.
/// </summary>
internal sealed class Code128Reader : LinearReader
{
    private const int StartA = 103;
    private const int StartB = 104;
    private const int StartC = 105;
    private const int Stop = 106;
    private const int CodeC = 99;
    private const int CodeBInA = 100;
    private const int CodeAInB = 101;
    private const int FunctionOne = 102;
    private const int FunctionTwo = 97;
    private const int FunctionThree = 96;
    private const int Shift = 98;

    /// <summary>
    /// The largest mean difference of a character from its pattern, in modules per module.
    /// </summary>
    private const float MaximumVariance = 0.25F;

    /// <summary>
    /// The largest difference of one element from its pattern, in modules.
    /// </summary>
    private const float MaximumIndividualVariance = 0.7F;

    /// <summary>
    /// The six element widths of each symbol character value, and the first six of the stop character, from <see cref="Code128Encoder.Patterns"/>.
    /// </summary>
    private static readonly int[] Widths = CreateWidths();

    /// <inheritdoc/>
    public override BarcodeFamily Families => BarcodeFamily.Code128;

    /// <inheritdoc/>
    public override bool TryRead(ReadOnlySpan<int> runs, int start, DecodeContext context, out LinearMatch match)
    {
        match = default;
        Span<int> values = stackalloc int[(runs.Length / 6) + 1];
        for (int i = start; i + 6 < runs.Length; i += 2)
        {
            int startValue = Best(runs.Slice(i, 6), StartA, Stop);
            if (startValue is not (StartA or StartB or StartC))
            {
                continue;
            }

            // Section 4.2: a quiet zone of ten modules. Half of that tolerates a tight crop.
            float module = Sum(runs, i, 6) / 11F;
            if (!IsQuiet(runs, i - 1, module * 5))
            {
                continue;
            }

            if (TryReadFrom(runs, i, values, out int count, out int end) && TryInterpret(values[..count], out string? text, out string? identifier))
            {
                match = new LinearMatch(BarcodeFamily.Code128, text, identifier, i, end, true);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Reads the symbol characters from a start character to the stop character and checks the check character.
    /// </summary>
    /// <param name="runs">The runs.</param>
    /// <param name="start">The index of the first bar of the start character.</param>
    /// <param name="values">The buffer for the character values.</param>
    /// <param name="count">The number of values written: the start character and the data characters, without the check character.</param>
    /// <param name="end">The index of the light run after the stop character.</param>
    /// <returns><see langword="true"/> when the characters read and check.</returns>
    private static bool TryReadFrom(ReadOnlySpan<int> runs, int start, Span<int> values, out int count, out int end)
    {
        count = 0;
        end = 0;
        int position = start;
        while (position + 6 < runs.Length && count < values.Length)
        {
            int value = Best(runs.Slice(position, 6), 0, Stop);
            if (value < 0)
            {
                return false;
            }

            if (value == Stop)
            {
                // The stop character ends with a two module bar, and a quiet zone follows it.
                int stopModules = Sum(runs, position, 6);
                float module = stopModules / 11F;
                float lastBar = runs[position + 6] / module;
                if (lastBar < 1.3F || lastBar > 2.7F || !IsQuiet(runs, position + 7, module * 5))
                {
                    return false;
                }

                end = position + 7;
                break;
            }

            if (count > 0 && value >= StartA)
            {
                return false;
            }

            values[count++] = value;
            position += 6;
        }

        // At least the start character, one data character and the check character.
        if (end == 0 || count < 3)
        {
            return false;
        }

        // Section 4.3.4: the start character weighs one and each data character its position. The sum modulo 103 is the check character.
        int sum = values[0];
        for (int i = 1; i < count - 1; i++)
        {
            sum += i * values[i];
        }

        if (sum % 103 != values[count - 1])
        {
            return false;
        }

        count--;
        return true;
    }

    /// <summary>
    /// Turns the character values into the transmitted data: the code sets, the shift, the function characters and the extended ASCII of FNC4.
    /// </summary>
    /// <param name="values">The start character and the data characters.</param>
    /// <param name="text">The data.</param>
    /// <param name="identifier">The symbology identifier.</param>
    /// <returns><see langword="true"/> when the values are a valid message.</returns>
    private static bool TryInterpret(ReadOnlySpan<int> values, out string text, out string identifier)
    {
        ValueStringBuilder builder = new(stackalloc char[128]);
        try
        {
            int set = values[0] - StartA;
            char modifier = '0';
            bool extended = false;
            bool shiftNext = false;
            bool fnc4Next = false;
            int lastFunctionFour = -2;

            for (int i = 1; i < values.Length; i++)
            {
                int value = values[i];
                int currentSet = shiftNext ? 1 - set : set;
                shiftNext = false;

                if (value == FunctionOne)
                {
                    // Section 4.3.7: FNC1 in the first position marks GS1 data. FNC1 after one letter or two digits marks AIM data. Any later FNC1 is the
                    // group separator.
                    if (builder.Length == 0)
                    {
                        modifier = '1';
                    }
                    else if (modifier == '0' && IsApplicationIndicator(builder.AsSpan()))
                    {
                        modifier = '2';
                    }
                    else
                    {
                        builder.Append('\u001D');
                    }

                    continue;
                }

                if (currentSet == 2)
                {
                    if (value < 100)
                    {
                        builder.Append((char)('0' + (value / 10)));
                        builder.Append((char)('0' + (value % 10)));
                    }
                    else
                    {
                        set = value == CodeBInA ? 1 : 0;
                    }

                    continue;
                }

                if (value < 96)
                {
                    int ascii = currentSet == 0 ? (value < 64 ? value + 32 : value - 64) : value + 32;

                    // Section 4.3.6: FNC4 adds 128 to the next character. Two FNC4 in a row latch the extended range until the next pair.
                    bool high = extended ^ fnc4Next;
                    fnc4Next = false;
                    builder.Append((char)(high ? ascii + 128 : ascii));
                    continue;
                }

                switch (value)
                {
                    case FunctionThree:
                    case FunctionTwo:
                        // Reader initialization and message append carry no data.
                        break;
                    case Shift:
                        shiftNext = true;
                        break;
                    case CodeC:
                        set = 2;
                        break;
                    case CodeBInA when currentSet == 0:
                        set = 1;
                        break;
                    case CodeAInB when currentSet == 1:
                        set = 0;
                        break;
                    default:
                        // FNC4: 101 in code set A, 100 in code set B.
                        if (lastFunctionFour == i - 1 && fnc4Next)
                        {
                            extended = !extended;
                            fnc4Next = false;
                        }
                        else
                        {
                            fnc4Next = true;
                        }

                        lastFunctionFour = i;
                        break;
                }
            }

            text = builder.ToString();
            identifier = "]C" + modifier;
            return true;
        }
        finally
        {
            builder.Dispose();
        }
    }

    /// <summary>
    /// Returns a value indicating whether the data before an FNC1 in the second position is the application indicator of section 4.3.7: one letter or two
    /// digits.
    /// </summary>
    /// <param name="data">The data so far.</param>
    /// <returns><see langword="true"/> for an application indicator.</returns>
    private static bool IsApplicationIndicator(ReadOnlySpan<char> data)
        => (data.Length == 1 && char.IsAsciiLetter(data[0])) || (data.Length == 2 && char.IsAsciiDigit(data[0]) && char.IsAsciiDigit(data[1]));

    /// <summary>
    /// Returns the character value whose pattern best matches six runs.
    /// </summary>
    /// <param name="runs">The six runs, bar first.</param>
    /// <param name="first">The lowest value to try.</param>
    /// <param name="last">The highest value to try.</param>
    /// <returns>The value, or -1 when no pattern matches.</returns>
    private static int Best(ReadOnlySpan<int> runs, int first, int last)
    {
        float best = MaximumVariance;
        int value = -1;
        ReadOnlySpan<int> widths = Widths;
        for (int v = first; v <= last; v++)
        {
            float variance = Variance(runs, widths.Slice(v * 6, 6), MaximumIndividualVariance);
            if (variance < best)
            {
                best = variance;
                value = v;
            }
        }

        return value;
    }

    /// <summary>
    /// Unpacks the patterns: six widths of four bits each, the first element in the highest bits. The stop character has seven elements, and its first six are
    /// kept.
    /// </summary>
    /// <returns>The widths.</returns>
    private static int[] CreateWidths()
    {
        ReadOnlySpan<uint> patterns = Code128Encoder.Patterns;
        int[] widths = new int[patterns.Length * 6];
        for (int v = 0; v < patterns.Length; v++)
        {
            uint pattern = patterns[v];
            int elements = v == Stop ? 7 : 6;
            for (int e = 0; e < 6; e++)
            {
                widths[(v * 6) + e] = (int)((pattern >> ((elements - 1 - e) * 4)) & 0xF);
            }
        }

        return widths;
    }
}
