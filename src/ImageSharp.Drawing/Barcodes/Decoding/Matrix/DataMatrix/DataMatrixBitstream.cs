// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Decodes the data codewords of Data Matrix ECC 200, section 5.2 of ISO/IEC 16022, the inverse of <see cref="DataMatrixCodewords"/>: ASCII encodation with
/// its digit pairs, Upper Shift and function characters, the C40, Text and X12 encodations of three values in two codewords, EDIFACT encodation of four six
/// bit values in three codewords, and Base 256 encodation with its length field and the 255-state randomising.
/// </summary>
internal static class DataMatrixBitstream
{
    private const int Pad = 129;
    private const int LatchC40 = 230;
    private const int LatchBase256 = 231;
    private const int Fnc1 = 232;
    private const int StructuredAppend = 233;
    private const int ReaderProgramming = 234;
    private const int UpperShift = 235;
    private const int Macro05 = 236;
    private const int Macro06 = 237;
    private const int LatchX12 = 238;
    private const int LatchText = 239;
    private const int LatchEdifact = 240;
    private const int Eci = 241;
    private const int Unlatch = 254;

    /// <summary>
    /// The encodations of three values in two codewords, which Code One takes from Data Matrix.
    /// </summary>
    public enum Triplets
    {
        /// <summary>
        /// C40: the capital letters in the basic set.
        /// </summary>
        C40,

        /// <summary>
        /// Text: the small letters in the basic set.
        /// </summary>
        Text,

        /// <summary>
        /// X12: one set of 40 values and no shifts.
        /// </summary>
        X12,
    }

    /// <summary>
    /// The encodations of Table 2.
    /// </summary>
    private enum Mode
    {
        /// <summary>
        /// ASCII encodation.
        /// </summary>
        Ascii,

        /// <summary>
        /// C40 encodation.
        /// </summary>
        C40,

        /// <summary>
        /// Text encodation.
        /// </summary>
        Text,

        /// <summary>
        /// X12 encodation.
        /// </summary>
        X12,

        /// <summary>
        /// EDIFACT encodation.
        /// </summary>
        Edifact,

        /// <summary>
        /// Base 256 encodation.
        /// </summary>
        Base256,
    }

    /// <summary>
    /// Gets the characters of the Shift 2 set of C40 and Text encodation, values 0 to 26.
    /// </summary>
    private static ReadOnlySpan<char> Shift2Characters => "!\"#$%&'()*+,-./:;<=>?@[\\]^_";

    /// <summary>
    /// Decodes the data codewords.
    /// </summary>
    /// <param name="codewords">The data codewords.</param>
    /// <param name="text">The data.</param>
    /// <param name="identifier">The symbology identifier.</param>
    /// <returns><see langword="true"/> when the codewords are valid.</returns>
    public static bool TryDecode(ReadOnlySpan<int> codewords, out string text, out string identifier)
    {
        text = string.Empty;
        identifier = "]d1";
        DecodedText decoded = new();
        Mode mode = Mode.Ascii;
        bool gs1 = false;
        bool eci = false;
        bool upperShift = false;
        string? trailer = null;
        int i = 0;
        while (i < codewords.Length)
        {
            switch (mode)
            {
                case Mode.Ascii:
                    int codeword = codewords[i++];
                    if (codeword == Pad)
                    {
                        i = codewords.Length;
                        break;
                    }

                    if (codeword <= 128)
                    {
                        decoded.AppendByte((byte)(codeword - 1 + (upperShift ? 128 : 0)));
                        upperShift = false;
                        break;
                    }

                    if (codeword <= 229)
                    {
                        int pair = codeword - 130;
                        decoded.AppendByte((byte)('0' + (pair / 10)));
                        decoded.AppendByte((byte)('0' + (pair % 10)));
                        break;
                    }

                    switch (codeword)
                    {
                        case LatchC40:
                            mode = Mode.C40;
                            break;
                        case LatchBase256:
                            mode = Mode.Base256;
                            break;
                        case Fnc1:
                            // FNC1 in the first position marks GS1 data, and any later one is the group separator.
                            if (i == 1)
                            {
                                gs1 = true;
                            }
                            else
                            {
                                decoded.AppendChar(Gs1Data.Separator);
                            }

                            break;
                        case StructuredAppend:
                            // The position in the sequence and the two codewords of the file identification carry no data of this symbol.
                            i += 3;
                            break;
                        case ReaderProgramming:
                            break;
                        case UpperShift:
                            upperShift = true;
                            break;
                        case Macro05:
                        case Macro06:
                            decoded.AppendText(codeword == Macro05 ? "[)>\u001E05\u001D" : "[)>\u001E06\u001D");
                            trailer = "\u001E\u0004";
                            break;
                        case LatchX12:
                            mode = Mode.X12;
                            break;
                        case LatchText:
                            mode = Mode.Text;
                            break;
                        case LatchEdifact:
                            mode = Mode.Edifact;
                            break;
                        case Eci:
                            int designator = ReadEci(codewords, ref i);
                            if (designator < 0)
                            {
                                return false;
                            }

                            decoded.SetEci(designator);
                            eci = true;
                            break;
                        default:
                            return false;
                    }

                    break;
                case Mode.Base256:
                    if (!TryDecodeBase256(codewords, ref i, decoded))
                    {
                        return false;
                    }

                    mode = Mode.Ascii;
                    break;
                case Mode.Edifact:
                    DecodeEdifact(codewords, ref i, decoded);
                    mode = Mode.Ascii;
                    break;
                default:
                    Triplets triplets = mode switch
                    {
                        Mode.C40 => Triplets.C40,
                        Mode.Text => Triplets.Text,
                        _ => Triplets.X12,
                    };

                    if (!TryDecodeTriplets(codewords, ref i, triplets, Unlatch, decoded, ref gs1))
                    {
                        return false;
                    }

                    mode = Mode.Ascii;
                    break;
            }
        }

        if (trailer is not null)
        {
            decoded.AppendText(trailer);
        }

        text = decoded.ToString();
        identifier = (gs1, eci) switch
        {
            (true, true) => "]d5",
            (true, false) => "]d2",
            (false, true) => "]d4",
            _ => "]d1",
        };

        return text.Length > 0;
    }

    /// <summary>
    /// Reads an ECI designator, section 5.4.1: one codeword below 128, two below 192 and three otherwise.
    /// </summary>
    /// <param name="codewords">The codewords.</param>
    /// <param name="i">The index of the first codeword of the designator, moved past it.</param>
    /// <returns>The ECI number, or -1.</returns>
    private static int ReadEci(ReadOnlySpan<int> codewords, ref int i)
    {
        if (i >= codewords.Length)
        {
            return -1;
        }

        int first = codewords[i++];
        if (first <= 127)
        {
            return first - 1;
        }

        if (first <= 191)
        {
            return i < codewords.Length ? ((first - 128) * 254) + (codewords[i++] - 1) + 127 : -1;
        }

        if (i + 1 >= codewords.Length)
        {
            return -1;
        }

        int value = ((first - 192) * 64516) + ((codewords[i] - 1) * 254) + (codewords[i + 1] - 1) + 16383;
        i += 2;
        return value;
    }

    /// <summary>
    /// Decodes C40, Text or X12 encodation: two codewords give 1600 C1 + 40 C2 + C3 + 1, until the unlatch 254 or until fewer than two codewords are left, which
    /// read in ASCII encodation.
    /// </summary>
    /// <param name="codewords">The codewords.</param>
    /// <param name="i">The index of the first codeword, moved past the encodation.</param>
    /// <param name="mode">C40, Text or X12.</param>
    /// <param name="unlatch">The codeword that unlatches: 254 in Data Matrix and 255 in Code One.</param>
    /// <param name="decoded">The data.</param>
    /// <param name="gs1">Whether the data is GS1 data, set by a Function 1 Symbol Character in the first position.</param>
    /// <returns><see langword="true"/> when the values are valid.</returns>
    public static bool TryDecodeTriplets(ReadOnlySpan<int> codewords, ref int i, Triplets mode, int unlatch, DecodedText decoded, ref bool gs1)
    {
        Span<int> values = stackalloc int[3];
        int shift = 0;
        bool upperShift = false;
        while (i + 1 < codewords.Length && codewords[i] != unlatch)
        {
            int value = (codewords[i] * 256) + codewords[i + 1] - 1;
            i += 2;
            values[0] = value / 1600;
            values[1] = (value / 40) % 40;
            values[2] = value % 40;
            foreach (int v in values)
            {
                if (mode == Triplets.X12)
                {
                    // X12: carriage return 0, asterisk 1, greater than 2, space 3, the digits 4 to 13 and the capital letters 14 to 39.
                    decoded.AppendByte(v switch
                    {
                        0 => (byte)'\r',
                        1 => (byte)'*',
                        2 => (byte)'>',
                        3 => (byte)' ',
                        < 14 => (byte)(v + 44),
                        _ => (byte)(v + 51),
                    });

                    continue;
                }

                int character = -1;
                switch (shift)
                {
                    case 0:
                        // The basic set: the three shifts, space and the digits, then the capital letters in C40 and the small letters in Text.
                        if (v < 3)
                        {
                            shift = v + 1;
                            continue;
                        }

                        character = v switch
                        {
                            3 => ' ',
                            < 14 => v + 44,
                            _ => v + (mode == Triplets.C40 ? 51 : 83),
                        };

                        break;
                    case 1:
                        // Shift 1: the control characters.
                        character = v;
                        break;
                    case 2:
                        // Shift 2: the punctuation, the Function 1 Symbol Character at 27 and the Upper Shift at 30.
                        if (v < 27)
                        {
                            character = Shift2Characters[v];
                        }
                        else if (v == 27)
                        {
                            if (decoded.IsEmpty)
                            {
                                gs1 = true;
                            }
                            else
                            {
                                decoded.AppendChar(Gs1Data.Separator);
                            }
                        }
                        else if (v == 30)
                        {
                            upperShift = true;
                        }
                        else
                        {
                            return false;
                        }

                        break;
                    default:
                        // Shift 3: the characters 96 to 127, the small letters in C40 and the capital letters in Text.
                        character = mode == Triplets.C40 || v == 0 || v > 26 ? v + 96 : v + 64;
                        break;
                }

                shift = 0;
                if (character >= 0)
                {
                    decoded.AppendByte((byte)(character + (upperShift ? 128 : 0)));
                    upperShift = false;
                }
            }
        }

        // The unlatch returns to ASCII encodation, and so does the end of the codewords.
        if (i < codewords.Length && codewords[i] == unlatch)
        {
            i++;
        }

        return true;
    }

    /// <summary>
    /// Decodes EDIFACT encodation: three codewords give four six bit values, until the value 31 unlatches or until two or fewer codewords are left, which read
    /// in ASCII encodation. A value without its sixth bit set stands for the character 64 above it.
    /// </summary>
    /// <param name="codewords">The codewords.</param>
    /// <param name="i">The index of the first codeword, moved past the encodation.</param>
    /// <param name="decoded">The data.</param>
    private static void DecodeEdifact(ReadOnlySpan<int> codewords, ref int i, DecodedText decoded)
    {
        int bit = i * 8;
        int end = codewords.Length * 8;
        while (end - bit > 16)
        {
            for (int k = 0; k < 4; k++)
            {
                int value = 0;
                for (int b = 0; b < 6; b++, bit++)
                {
                    value = (value << 1) | ((codewords[bit >> 3] >> (7 - (bit & 7))) & 1);
                }

                if (value == 31)
                {
                    // The unlatch ends the codeword it is in; the rest of its bits fill it.
                    i = (bit + 7) / 8;
                    return;
                }

                decoded.AppendByte((byte)((value & 0x20) == 0 ? value | 0x40 : value));
            }
        }

        i = bit / 8;
    }

    /// <summary>
    /// Decodes Base 256 encodation: a length field of one codeword below 250, two from 250, or 0 for the rest of the symbol, then the bytes. Every codeword of
    /// the field and the bytes takes the 255-state randomising of its position.
    /// </summary>
    /// <param name="codewords">The codewords.</param>
    /// <param name="i">The index of the first codeword of the length field, moved past the bytes.</param>
    /// <param name="decoded">The data.</param>
    /// <returns><see langword="true"/> when the bytes are there.</returns>
    private static bool TryDecodeBase256(ReadOnlySpan<int> codewords, ref int i, DecodedText decoded)
    {
        if (i >= codewords.Length)
        {
            return false;
        }

        int first = Unrandomise(codewords[i], i + 1);
        i++;
        int length;
        if (first == 0)
        {
            length = codewords.Length - i;
        }
        else if (first < 250)
        {
            length = first;
        }
        else
        {
            if (i >= codewords.Length)
            {
                return false;
            }

            length = (250 * (first - 249)) + Unrandomise(codewords[i], i + 1);
            i++;
        }

        if (length > codewords.Length - i)
        {
            return false;
        }

        for (int k = 0; k < length; k++, i++)
        {
            decoded.AppendByte((byte)Unrandomise(codewords[i], i + 1));
        }

        return true;
    }

    /// <summary>
    /// Removes the 255-state randomising of a codeword: ((149 x position) mod 255) + 1, wrapping below zero.
    /// </summary>
    /// <param name="codeword">The codeword.</param>
    /// <param name="position">The position of the codeword in the symbol, from 1.</param>
    /// <returns>The value.</returns>
    private static int Unrandomise(int codeword, int position)
    {
        int value = codeword - (((149 * position) % 255) + 1);
        return value >= 0 ? value : value + 256;
    }
}
