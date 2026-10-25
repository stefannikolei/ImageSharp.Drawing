// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Globalization;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Decodes the data codewords of MaxiCode, section 4 of ISO/IEC 16023: the characters of code sets A to E of Table 3 with their latches, shifts and locks,
/// the numeric shift of nine digits in five codewords, the ECI designators, and the structured carrier message of modes 2 and 3 in the primary message.
/// </summary>
internal static class MaxiCodeBitstream
{
    private const int SetCount = 5;
    private const int Eci = 27;
    private const int Lock = -2;
    private const int Pad = -3;
    private const int Special = -4;

    /// <summary>
    /// The byte of every value of every code set, the inverse of <see cref="MaxiCodeEncoder.SetValues"/>, with the special codewords as negative values.
    /// </summary>
    private static readonly int[] Characters = CreateCharacters();

    /// <summary>
    /// Decodes a symbol.
    /// </summary>
    /// <param name="data">The data codewords: the 10 of the primary message, then those of the secondary message.</param>
    /// <param name="mode">The mode, 2 to 6.</param>
    /// <param name="text">The data.</param>
    /// <param name="identifier">The symbology identifier.</param>
    /// <returns><see langword="true"/> when the codewords are valid.</returns>
    public static bool TryDecode(ReadOnlySpan<int> data, int mode, out string text, out string identifier)
    {
        text = string.Empty;
        identifier = mode is 2 or 3 ? "]U1" : "]U0";
        DecodedText decoded = new();
        bool eci;
        if (mode is 2 or 3)
        {
            // The primary data is one 60-bit number, least significant bits first: the mode in bits 0 to 3, the postal code in bits 4 to 39, the country code
            // in bits 40 to 49 and the class of service in bits 50 to 59.
            long primary = 0;
            for (int i = MaxiCodeEncoder.PrimaryDataCodewords - 1; i >= 0; i--)
            {
                primary = (primary << 6) | (uint)data[i];
            }

            string postalCode;
            if (mode == 2)
            {
                int length = (int)((primary >> 34) & 63);
                long value = (primary >> 4) & ((1L << 30) - 1);
                if (length is < 1 or > 9)
                {
                    return false;
                }

                postalCode = value.ToString(CultureInfo.InvariantCulture).PadLeft(length, '0');
            }
            else
            {
                // Six characters of code set A, the first in the highest bits, padded with spaces.
                Span<char> characters = stackalloc char[6];
                for (int i = 0; i < 6; i++)
                {
                    int character = Characters[(int)((primary >> (4 + (6 * (5 - i)))) & 63)];
                    if (character < 0)
                    {
                        return false;
                    }

                    characters[i] = (char)character;
                }

                postalCode = characters.TrimEnd(' ').ToString();
            }

            int country = (int)((primary >> 40) & 1023);
            int service = (int)((primary >> 50) & 1023);
            DecodedText secondary = new();
            if (!TryDecodeMessage(data[MaxiCodeEncoder.PrimaryDataCodewords..], secondary, out eci))
            {
                return false;
            }

            // The structured carrier fields come first, or after the header of a message in format 01.
            string message = secondary.ToString();
            string fields = $"{postalCode}\u001D{country:D3}\u001D{service:D3}\u001D";
            text = message.StartsWith("[)>\u001E01\u001D", StringComparison.Ordinal) && message.Length >= 9
                ? message.Insert(9, fields)
                : fields + message;
        }
        else
        {
            if (!TryDecodeMessage(data[1..], decoded, out eci))
            {
                return false;
            }

            text = decoded.ToString();
        }

        if (eci)
        {
            identifier = mode is 2 or 3 ? "]U3" : "]U2";
        }

        return text.Length > 0;
    }

    /// <summary>
    /// Decodes a message through the code sets, from code set A.
    /// </summary>
    /// <param name="codewords">The codewords.</param>
    /// <param name="decoded">The data.</param>
    /// <param name="eci">Whether the message carries an ECI.</param>
    /// <returns><see langword="true"/> when the codewords are valid.</returns>
    private static bool TryDecodeMessage(ReadOnlySpan<int> codewords, DecodedText decoded, out bool eci)
    {
        eci = false;
        int set = 0;
        int previousSet = 0;
        int shift = -1;
        for (int i = 0; i < codewords.Length; i++)
        {
            int codeword = codewords[i];
            int character = Characters[(set * 64) + codeword];
            if (character == Special)
            {
                switch (codeword)
                {
                    case MaxiCodeEncoder.NumericShiftCodeword:
                        // Nine digits as a 30-bit number in the next five codewords.
                        if (i + 5 >= codewords.Length)
                        {
                            return false;
                        }

                        int value = 0;
                        for (int k = 1; k <= 5; k++)
                        {
                            value = (value << 6) | codewords[i + k];
                        }

                        i += 5;
                        if (value > 999_999_999)
                        {
                            return false;
                        }

                        foreach (char digit in value.ToString("D9", CultureInfo.InvariantCulture))
                        {
                            decoded.AppendByte((byte)digit);
                        }

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
                    case MaxiCodeEncoder.TwoShiftACodeword:
                    case MaxiCodeEncoder.ThreeShiftACodeword:
                        // 2 SHIFT A and 3 SHIFT A in code set B.
                        previousSet = set;
                        set = 0;
                        shift = codeword == MaxiCodeEncoder.TwoShiftACodeword ? 2 : 3;
                        continue;
                    case MaxiCodeEncoder.LatchA:
                        set = 0;
                        shift = -1;
                        continue;
                    case MaxiCodeEncoder.LatchBCodeword:
                        // The latch to code set B, which in code set B itself is the latch to code set A.
                        set = set == 1 ? 0 : 1;
                        shift = -1;
                        continue;
                    default:
                        // SHIFT B in code set A and SHIFT A in code set B, and the shifts into code sets C, D and E, for one character.
                        previousSet = set;
                        set = codeword switch
                        {
                            MaxiCodeEncoder.ShiftCodeword => set == 0 ? 1 : 0,
                            MaxiCodeEncoder.ShiftCCodeword => 2,
                            MaxiCodeEncoder.ShiftDCodeword => 3,
                            _ => 4,
                        };

                        shift = 1;
                        continue;
                }
            }
            else if (character == Lock)
            {
                // A shift into the code set in force locks it.
                shift = -1;
                continue;
            }
            else if (character != Pad)
            {
                decoded.AppendByte((byte)character);
            }

            if (shift > 0 && --shift == 0)
            {
                set = previousSet;
                shift = -1;
            }
        }

        return true;
    }

    /// <summary>
    /// Reads an ECI designator after the ECI codeword: one to four codewords whose leading bits give their number, for 5, 10, 15 or 20 bits.
    /// </summary>
    /// <param name="codewords">The codewords.</param>
    /// <param name="i">The index of the ECI codeword, moved to the last codeword of the designator.</param>
    /// <returns>The ECI number, or -1.</returns>
    private static int ReadEci(ReadOnlySpan<int> codewords, ref int i)
    {
        if (i + 1 >= codewords.Length)
        {
            return -1;
        }

        int first = codewords[++i];
        int extra = (first & 0x20) == 0 ? 0 : (first & 0x10) == 0 ? 1 : (first & 0x08) == 0 ? 2 : 3;
        int value = first & (0x1F >> extra);
        for (int k = 0; k < extra; k++)
        {
            if (i + 1 >= codewords.Length)
            {
                return -1;
            }

            value = (value << 6) | codewords[++i];
        }

        return value;
    }

    /// <summary>
    /// Inverts the code set table of the encoder and marks the special codewords of Table 3. In every code set 27 is the ECI and 63 a latch, to code set A in
    /// code set B and to code set B elsewhere. Code sets A and B have the numeric shift at 31, the pad at 33 and the shifts at 59 to 62, code set B more pads
    /// at 55 and 58 and the two and three shifts into code set A at 56 and 57. Code sets C, D and E have the latch to code set A at 58 and the shifts at 60 to
    /// 62, of which the shift into the code set itself locks it, and code set E has the numeric shift at 31 and pads at 28 and 29.
    /// </summary>
    /// <returns>The byte of every value, 64 per code set, or a negative marker.</returns>
    private static int[] CreateCharacters()
    {
        int[] characters = new int[SetCount * 64];
        characters.AsSpan().Fill(Special);
        ReadOnlySpan<byte> values = MaxiCodeEncoder.SetValues;
        for (int set = 0; set < SetCount; set++)
        {
            for (int b = 0; b < 256; b++)
            {
                int value = values[(set * 256) + b];
                if (value != 0xFF)
                {
                    characters[(set * 64) + value] = b;
                }
            }

            Span<int> row = characters.AsSpan(set * 64, 64);
            row[Eci] = Special;
            row[MaxiCodeEncoder.LatchBCodeword] = Special;
            if (set < 2)
            {
                row[MaxiCodeEncoder.NumericShiftCodeword] = Special;
                row[33] = Pad;
                for (int codeword = MaxiCodeEncoder.ShiftCodeword; codeword <= MaxiCodeEncoder.ShiftECodeword; codeword++)
                {
                    row[codeword] = Special;
                }

                continue;
            }

            row[MaxiCodeEncoder.LatchA] = Special;
            for (int codeword = MaxiCodeEncoder.ShiftCCodeword; codeword <= MaxiCodeEncoder.ShiftECodeword; codeword++)
            {
                row[codeword] = codeword - MaxiCodeEncoder.ShiftCCodeword + 2 == set ? Lock : Special;
            }
        }

        characters[64 + 55] = Pad;
        characters[64 + 58] = Pad;
        characters[64 + MaxiCodeEncoder.TwoShiftACodeword] = Special;
        characters[64 + MaxiCodeEncoder.ThreeShiftACodeword] = Special;
        characters[(4 * 64) + MaxiCodeEncoder.NumericShiftCodeword] = Special;
        characters[(4 * 64) + 28] = Pad;
        characters[(4 * 64) + 29] = Pad;
        return characters;
    }
}
