// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Decodes the bit stream of Aztec Code, section 7.3 of ISO/IEC 24778: the characters of the upper, lower, mixed, punctuation and digit code sets of Table 2,
/// their latches and shifts, the byte strings of a B/S, and the FLG(n) characters that carry FNC1 and the ECI designators.
/// </summary>
internal static class AztecBitstream
{
    private const int Upper = 0;
    private const int Lower = 1;
    private const int Mixed = 2;
    private const int Punct = 3;
    private const int Digit = 4;
    private const int Binary = 5;

    /// <summary>
    /// Gets the characters of the mixed code set, values 1 to 27 after the space.
    /// </summary>
    private static ReadOnlySpan<char> MixedCharacters
        => " \u0001\u0002\u0003\u0004\u0005\u0006\u0007\b\t\n\u000B\f\r\u001B\u001C\u001D\u001E\u001F@\\^_`|~\u007F";

    /// <summary>
    /// Gets the characters of the punctuation code set from value 6, after the carriage return and the pairs.
    /// </summary>
    private static ReadOnlySpan<char> PunctCharacters => "!\"#$%&'()*+,-./:;<=>?[]{}";

    /// <summary>
    /// Decodes the bits.
    /// </summary>
    /// <param name="bits">The bits after the stuffing is removed, one per byte.</param>
    /// <param name="text">The data.</param>
    /// <param name="identifier">The symbology identifier.</param>
    /// <returns><see langword="true"/> when the bits are valid.</returns>
    public static bool TryDecode(ReadOnlySpan<byte> bits, out string text, out string identifier)
    {
        text = string.Empty;
        identifier = "]z0";
        DecodedText decoded = new();
        int latched = Upper;
        int current = Upper;
        int index = 0;
        bool gs1 = false;
        bool eci = false;
        while (index < bits.Length)
        {
            if (current == Binary)
            {
                // A B/S is followed by the byte count, 1 to 31 in 5 bits, or 0 and the count less 31 in 11 bits.
                if (bits.Length - index < 5)
                {
                    break;
                }

                int length = Read(bits, ref index, 5);
                if (length == 0)
                {
                    if (bits.Length - index < 11)
                    {
                        break;
                    }

                    length = Read(bits, ref index, 11) + 31;
                }

                for (int k = 0; k < length && bits.Length - index >= 8; k++)
                {
                    decoded.AppendByte((byte)Read(bits, ref index, 8));
                }

                current = latched;
                continue;
            }

            int size = current == Digit ? 4 : 5;
            if (bits.Length - index < size)
            {
                break;
            }

            int code = Read(bits, ref index, size);
            int next = current;
            bool latch = false;
            switch (current)
            {
                case Upper:
                case Lower:
                    if (code == 0)
                    {
                        next = Punct;
                    }
                    else if (code == 1)
                    {
                        decoded.AppendByte((byte)' ');
                    }
                    else if (code <= 27)
                    {
                        decoded.AppendByte((byte)((current == Upper ? 'A' : 'a') + code - 2));
                    }
                    else
                    {
                        // 28 is L/L from upper and U/S from lower, 29 M/L, 30 D/L and 31 B/S.
                        (next, latch) = code switch
                        {
                            28 => current == Upper ? (Lower, true) : (Upper, false),
                            29 => (Mixed, true),
                            30 => (Digit, true),
                            _ => (Binary, false),
                        };
                    }

                    break;
                case Mixed:
                    if (code == 0)
                    {
                        next = Punct;
                    }
                    else if (code <= 27)
                    {
                        decoded.AppendByte((byte)MixedCharacters[code - 1]);
                    }
                    else
                    {
                        (next, latch) = code switch
                        {
                            28 => (Lower, true),
                            29 => (Upper, true),
                            30 => (Punct, true),
                            _ => (Binary, false),
                        };
                    }

                    break;
                case Punct:
                    switch (code)
                    {
                        case 0:
                            // FLG(n): n 0 is FNC1, n 1 to 6 an ECI of n digits, and 7 is reserved.
                            if (bits.Length - index < 3)
                            {
                                index = bits.Length;
                                break;
                            }

                            int n = Read(bits, ref index, 3);
                            if (n == 7)
                            {
                                return false;
                            }

                            if (n == 0)
                            {
                                if (decoded.IsEmpty && !gs1)
                                {
                                    gs1 = true;
                                }
                                else
                                {
                                    decoded.AppendChar(Gs1Data.Separator);
                                }

                                break;
                            }

                            int value = 0;
                            for (int k = 0; k < n; k++)
                            {
                                int digit = bits.Length - index >= 4 ? Read(bits, ref index, 4) : -1;
                                if (digit is < 2 or > 11)
                                {
                                    return false;
                                }

                                value = (value * 10) + digit - 2;
                            }

                            decoded.SetEci(value);
                            eci = true;
                            break;
                        case 1:
                            decoded.AppendByte((byte)'\r');
                            break;
                        case 2:
                            decoded.AppendByte((byte)'\r');
                            decoded.AppendByte((byte)'\n');
                            break;
                        case 3:
                        case 4:
                        case 5:
                            decoded.AppendByte((byte)(code == 3 ? '.' : code == 4 ? ',' : ':'));
                            decoded.AppendByte((byte)' ');
                            break;
                        case 31:
                            (next, latch) = (Upper, true);
                            break;
                        default:
                            decoded.AppendByte((byte)PunctCharacters[code - 6]);
                            break;
                    }

                    break;
                default:
                    if (code == 0)
                    {
                        next = Punct;
                    }
                    else if (code == 1)
                    {
                        decoded.AppendByte((byte)' ');
                    }
                    else if (code <= 11)
                    {
                        decoded.AppendByte((byte)('0' + code - 2));
                    }
                    else if (code <= 13)
                    {
                        decoded.AppendByte((byte)(code == 12 ? ',' : '.'));
                    }
                    else
                    {
                        // 14 U/L and 15 U/S.
                        (next, latch) = (Upper, code == 14);
                    }

                    break;
            }

            if (next == current)
            {
                // A character, after which a shift returns to the code set latched.
                current = latched;
            }
            else if (latch)
            {
                latched = next;
                current = next;
            }
            else
            {
                // A shift or a B/S reads the next character or string in its code set, then returns to the code set it came from, which section 7.3.2.2
                // makes the code set in force, also when that is a shift.
                latched = current;
                current = next;
            }
        }

        text = decoded.ToString();
        identifier = (gs1, eci) switch
        {
            (true, true) => "]z4",
            (true, false) => "]z1",
            (false, true) => "]z3",
            _ => "]z0",
        };

        return text.Length > 0;
    }

    /// <summary>
    /// Reads bits as a number, the most significant first.
    /// </summary>
    /// <param name="bits">The bits.</param>
    /// <param name="index">The index of the first bit, moved past them.</param>
    /// <param name="count">The number of bits.</param>
    /// <returns>The number.</returns>
    private static int Read(ReadOnlySpan<byte> bits, ref int index, int count)
    {
        int value = 0;
        for (int i = 0; i < count; i++)
        {
            value = (value << 1) | bits[index++];
        }

        return value;
    }
}
