// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Decodes the general purpose field that GS1 DataBar Expanded, section 7.2.5.5 of ISO/IEC 24724, and the GS1 Composite 2D components, section 5.4 of
/// ISO/IEC 24723, share: numeric, alphanumeric and ISO/IEC 646 modes that latch to each other, and the alpha mode of section 5.3.3 that only the 2D
/// components start in. The separator reads as a Function 1 Symbol Character, <see cref="Gs1Data.Separator"/>.
/// </summary>
internal static class Gs1GeneralField
{
    /// <summary>
    /// The modes of the general purpose field.
    /// </summary>
    public enum Mode
    {
        /// <summary>
        /// Two digits or a digit and the separator in seven bits.
        /// </summary>
        Numeric,

        /// <summary>
        /// Digits, capital letters and five punctuation characters.
        /// </summary>
        Alphanumeric,

        /// <summary>
        /// The ISO/IEC 646 characters of GS1 element strings.
        /// </summary>
        Iso646,

        /// <summary>
        /// Capital letters in five bits and digits in six, which the separator ends with a latch to numeric mode.
        /// </summary>
        Alpha,
    }

    /// <summary>
    /// Reads the general purpose field to the end of the bits. The modes latch and the padding, latches with no characters, ends the field.
    /// </summary>
    /// <param name="bits">The bit stream.</param>
    /// <param name="builder">The text.</param>
    /// <param name="mode">The mode the field starts in.</param>
    /// <returns><see langword="true"/> when the field is valid.</returns>
    public static bool TryRead(ref BitStreamReader bits, ref ValueStringBuilder builder, Mode mode)
    {
        while (true)
        {
            int remaining = bits.Remaining;
            switch (mode)
            {
                case Mode.Numeric:
                    if (remaining < 4)
                    {
                        return true;
                    }

                    // Four zero bits latch to alphanumeric mode, also as the start of the padding.
                    if (bits.Peek(4) == 0)
                    {
                        bits.Read(4);
                        mode = Mode.Alphanumeric;
                        break;
                    }

                    if (remaining < 7)
                    {
                        // A last digit in four bits, its value plus one.
                        int single = bits.Read(4);
                        if (single > 10)
                        {
                            return false;
                        }

                        builder.Append((char)('0' + single - 1));
                        return true;
                    }

                    int pair = bits.Read(7) - 8;
                    if (pair < 0 || pair > 120)
                    {
                        return false;
                    }

                    AppendNumeric(ref builder, pair / 11);
                    AppendNumeric(ref builder, pair % 11);
                    break;

                case Mode.Alpha:
                    // Table 4 of ISO/IEC 24723: a capital letter is its code less 65 in five bits, a digit its code plus 4 in six bits, and the separator 31 in
                    // five bits, which latches to numeric mode.
                    if (remaining < 5)
                    {
                        return true;
                    }

                    int letter = bits.Peek(5);
                    if (letter < 26)
                    {
                        bits.Read(5);
                        builder.Append((char)('A' + letter));
                    }
                    else if (letter == 31)
                    {
                        bits.Read(5);
                        builder.Append(Gs1Data.Separator);
                        mode = Mode.Numeric;
                    }
                    else
                    {
                        if (remaining < 6)
                        {
                            return true;
                        }

                        builder.Append((char)(bits.Read(6) - 4));
                    }

                    break;

                default:
                    if (remaining < 3)
                    {
                        return true;
                    }

                    if (bits.Peek(3) == 0)
                    {
                        bits.Read(3);
                        mode = Mode.Numeric;
                        break;
                    }

                    if (remaining < 5)
                    {
                        return true;
                    }

                    int five = bits.Peek(5);
                    if (five == 0b01111)
                    {
                        // A Function 1 Symbol Character, which latches to numeric mode.
                        bits.Read(5);
                        builder.Append(Gs1Data.Separator);
                        mode = Mode.Numeric;
                        break;
                    }

                    if (five == 0b00100)
                    {
                        bits.Read(5);
                        mode = mode == Mode.Alphanumeric ? Mode.Iso646 : Mode.Alphanumeric;
                        break;
                    }

                    if (five is >= 5 and <= 14)
                    {
                        bits.Read(5);
                        builder.Append((char)('0' + five - 5));
                        break;
                    }

                    if (mode == Mode.Alphanumeric)
                    {
                        if (remaining < 6)
                        {
                            return true;
                        }

                        int six = bits.Read(6);
                        if (six is >= 32 and <= 57)
                        {
                            builder.Append((char)(six + 33));
                        }
                        else if (six is >= 58 and <= 62)
                        {
                            builder.Append("*,-./"[six - 58]);
                        }
                        else
                        {
                            return false;
                        }

                        break;
                    }

                    if (remaining < 7)
                    {
                        return true;
                    }

                    int seven = bits.Peek(7);
                    if (seven is >= 64 and <= 89)
                    {
                        bits.Read(7);
                        builder.Append((char)(seven + 1));
                        break;
                    }

                    if (seven is >= 90 and <= 115)
                    {
                        bits.Read(7);
                        builder.Append((char)(seven + 7));
                        break;
                    }

                    if (remaining < 8)
                    {
                        return true;
                    }

                    int eight = bits.Read(8);
                    ReadOnlySpan<char> punctuation = DataBarExpandedEncoder.Iso646Punctuation;
                    if (eight < 232 || eight - 232 >= punctuation.Length)
                    {
                        return false;
                    }

                    builder.Append(punctuation[eight - 232]);
                    break;
            }
        }
    }

    /// <summary>
    /// Removes the separators at the end of decoded element strings: a Function 1 Symbol Character at the end of the data separates nothing.
    /// </summary>
    /// <param name="text">The element strings.</param>
    /// <returns>The element strings without separators at the end.</returns>
    public static ReadOnlySpan<char> TrimSeparators(ReadOnlySpan<char> text)
    {
        while (text.Length > 0 && text[^1] == Gs1Data.Separator)
        {
            text = text[..^1];
        }

        return text;
    }

    /// <summary>
    /// Appends a numeric mode value: a digit, or the group separator for 10.
    /// </summary>
    /// <param name="builder">The text.</param>
    /// <param name="value">The value, 0 to 10.</param>
    private static void AppendNumeric(ref ValueStringBuilder builder, int value)
        => builder.Append(value == 10 ? Gs1Data.Separator : (char)('0' + value));
}
