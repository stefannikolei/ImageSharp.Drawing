// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Globalization;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Decodes the data bit stream of QR Code, Micro QR Code and Rectangular Micro QR Code, section 7.4 of ISO/IEC 18004 and ISO/IEC 23941: mode segments of a
/// mode indicator, a character count indicator and the data, in Numeric, Alphanumeric, Byte and Kanji mode, with the ECI, FNC1 and Structured Append headers,
/// up to the terminator. The lengths of the indicators come from the version group, <see cref="QrEncoder.ModeBits"/> and <see cref="QrEncoder.CountBits"/>.
/// </summary>
internal static class QrBitstream
{
    /// <summary>
    /// The modes, numbered as the Micro QR Code mode indicator counts them, Table 2.
    /// </summary>
    private enum Mode
    {
        /// <summary>
        /// Three digits in 10 bits.
        /// </summary>
        Numeric,

        /// <summary>
        /// Two characters of Table 5 in 11 bits.
        /// </summary>
        Alphanumeric,

        /// <summary>
        /// One byte in 8 bits.
        /// </summary>
        Byte,

        /// <summary>
        /// One Shift JIS double byte character in 13 bits.
        /// </summary>
        Kanji,

        /// <summary>
        /// An Extended Channel Interpretation designator.
        /// </summary>
        Eci,

        /// <summary>
        /// FNC1 in the first position: GS1 data.
        /// </summary>
        Fnc1First,

        /// <summary>
        /// FNC1 in the second position, after an application indicator.
        /// </summary>
        Fnc1Second,

        /// <summary>
        /// The header of a symbol in a Structured Append sequence.
        /// </summary>
        StructuredAppend,

        /// <summary>
        /// The end of the data.
        /// </summary>
        Terminator,
    }

    /// <summary>
    /// Decodes the data codewords.
    /// </summary>
    /// <param name="data">The data codewords.</param>
    /// <param name="bitCount">The number of data bits, which excludes the low four bits of the short last codeword of M1 and M3.</param>
    /// <param name="size">The symbol size.</param>
    /// <param name="text">The data.</param>
    /// <param name="identifier">The symbology identifier.</param>
    /// <returns><see langword="true"/> when the bit stream is valid.</returns>
    public static bool TryDecode(ReadOnlySpan<byte> data, int bitCount, QrEncoder.SymbolSize size, out string text, out string identifier)
    {
        text = string.Empty;
        identifier = "]Q1";
        BitStreamReader bits = new(data, bitCount);
        DecodedText decoded = new();
        int group = size.Group;
        int modeBits = QrEncoder.ModeBits[group];
        bool gs1 = false;
        bool fnc1Second = false;
        bool eci = false;
        while (true)
        {
            Mode mode = ReadMode(ref bits, size, modeBits);
            if (mode == Mode.Terminator)
            {
                break;
            }

            switch (mode)
            {
                case Mode.Fnc1First:
                    gs1 = true;
                    continue;
                case Mode.Fnc1Second:
                    // Section 7.4.8.3: an application indicator of two digits, or a letter whose value is its code plus 100.
                    int indicator = bits.Read(8);
                    fnc1Second = true;
                    decoded.AppendText(indicator < 100 ? indicator.ToString("D2", CultureInfo.InvariantCulture) : ((char)(indicator - 100)).ToString());
                    continue;
                case Mode.StructuredAppend:
                    // The position and total of the sequence and the parity of its data carry nothing of this symbol's data.
                    bits.Read(16);
                    continue;
                case Mode.Eci:
                    int designator = ReadEci(ref bits);
                    if (designator < 0)
                    {
                        return false;
                    }

                    decoded.SetEci(designator);
                    eci = true;
                    continue;
            }

            int countBits = CountBits(size, mode);
            if (countBits <= 0)
            {
                return false;
            }

            int count = bits.Read(countBits);
            bool valid = mode switch
            {
                Mode.Numeric => TryReadNumeric(ref bits, count, decoded),
                Mode.Alphanumeric => TryReadAlphanumeric(ref bits, count, gs1 || fnc1Second, decoded),
                Mode.Byte => TryReadBytes(ref bits, count, decoded),
                _ => TryReadKanji(ref bits, count, decoded),
            };

            if (!valid || bits.Failed)
            {
                return false;
            }
        }

        if (bits.Failed)
        {
            return false;
        }

        text = decoded.ToString();
        identifier = (gs1, fnc1Second, eci) switch
        {
            (true, _, false) => "]Q3",
            (true, _, true) => "]Q4",
            (_, true, false) => "]Q5",
            (_, true, true) => "]Q6",
            (_, _, true) => "]Q2",
            _ => "]Q1",
        };

        return text.Length > 0;
    }

    /// <summary>
    /// Reads a mode indicator. A QR Code indicator sets one bit per mode, a Micro QR Code one counts the modes from 0 and an all zero mode and count is its
    /// terminator, and a Rectangular Micro QR Code one counts them from 1.
    /// </summary>
    /// <param name="bits">The bit stream.</param>
    /// <param name="size">The symbol size.</param>
    /// <param name="modeBits">The length of the mode indicator.</param>
    /// <returns>The mode.</returns>
    private static Mode ReadMode(ref BitStreamReader bits, QrEncoder.SymbolSize size, int modeBits)
    {
        if (size.Format == QrFormat.Micro)
        {
            // Table 2: the terminator is all zero, the mode and character count indicators of an empty Numeric segment. A stream that ends before it needs none.
            int terminator = QrEncoder.TerminatorBits[size.Group];
            if (bits.Remaining < terminator || bits.Peek(terminator) == 0)
            {
                return Mode.Terminator;
            }

            return (Mode)bits.Read(modeBits);
        }

        if (bits.Remaining < modeBits)
        {
            return Mode.Terminator;
        }

        int value = bits.Read(modeBits);
        if (size.Format == QrFormat.Rectangular)
        {
            // ISO/IEC 23941 Table 2: 000 terminator, 001 Numeric, 010 Alphanumeric, 011 Byte, 100 Kanji, 101 FNC1 first, 110 FNC1 second and 111 ECI.
            return value switch
            {
                0 => Mode.Terminator,
                1 => Mode.Numeric,
                2 => Mode.Alphanumeric,
                3 => Mode.Byte,
                4 => Mode.Kanji,
                5 => Mode.Fnc1First,
                6 => Mode.Fnc1Second,
                _ => Mode.Eci,
            };
        }

        // Table 2 of ISO/IEC 18004.
        return value switch
        {
            0b0001 => Mode.Numeric,
            0b0010 => Mode.Alphanumeric,
            0b0100 => Mode.Byte,
            0b1000 => Mode.Kanji,
            0b0111 => Mode.Eci,
            0b0101 => Mode.Fnc1First,
            0b1001 => Mode.Fnc1Second,
            0b0011 => Mode.StructuredAppend,
            _ => Mode.Terminator,
        };
    }

    /// <summary>
    /// Returns the length of the character count indicator of a mode, Table 3, or 0 where the version does not have the mode.
    /// </summary>
    /// <param name="size">The symbol size.</param>
    /// <param name="mode">The mode.</param>
    /// <returns>The length in bits.</returns>
    private static int CountBits(QrEncoder.SymbolSize size, Mode mode)
    {
        int group = size.Group;
        if (mode != Mode.Kanji)
        {
            return QrEncoder.CountBits[(group * 3) + (int)mode];
        }

        // Table 3: Kanji mode counts in 8, 10 or 12 bits in QR Code, and in 3 or 4 bits in M3 and M4. Rectangular Micro QR Code is not supported here.
        return size.Format switch
        {
            QrFormat.Full => 8 + (2 * group),
            QrFormat.Micro when size.Version >= 3 => size.Version,
            _ => 0,
        };
    }

    /// <summary>
    /// Reads an ECI designator, section 7.4.2.2: one, two or three codewords whose leading bits give their number.
    /// </summary>
    /// <param name="bits">The bit stream.</param>
    /// <returns>The ECI number, or -1.</returns>
    private static int ReadEci(ref BitStreamReader bits)
    {
        int first = bits.Read(8);
        if ((first & 0x80) == 0)
        {
            return first & 0x7F;
        }

        if ((first & 0xC0) == 0x80)
        {
            return ((first & 0x3F) << 8) | bits.Read(8);
        }

        if ((first & 0xE0) == 0xC0)
        {
            return ((first & 0x1F) << 16) | bits.Read(16);
        }

        return -1;
    }

    /// <summary>
    /// Reads a Numeric mode segment: three digits in 10 bits, and a last two in 7 bits or one in 4.
    /// </summary>
    /// <param name="bits">The bit stream.</param>
    /// <param name="count">The number of digits.</param>
    /// <param name="decoded">The data.</param>
    /// <returns><see langword="true"/> when every group is a number of its digits.</returns>
    private static bool TryReadNumeric(ref BitStreamReader bits, int count, DecodedText decoded)
    {
        Span<char> digits = stackalloc char[3];
        while (count > 0)
        {
            int length = Math.Min(3, count);
            int value = bits.Read(length switch { 3 => 10, 2 => 7, _ => 4 });
            int limit = length switch { 3 => 1000, 2 => 100, _ => 10 };
            if (value >= limit)
            {
                return false;
            }

            for (int i = length - 1; i >= 0; i--)
            {
                digits[i] = (char)('0' + (value % 10));
                value /= 10;
            }

            decoded.AppendText(digits[..length]);
            count -= length;
        }

        return true;
    }

    /// <summary>
    /// Reads an Alphanumeric mode segment: two characters in 11 bits, and a last one in 6. In FNC1 mode, section 7.4.8.1 and clause 13.4, a % is the FNC1
    /// character, which transmits as the group separator, and %% is a % of the data.
    /// </summary>
    /// <param name="bits">The bit stream.</param>
    /// <param name="count">The number of characters.</param>
    /// <param name="fnc1">Whether the symbol is in an FNC1 mode.</param>
    /// <param name="decoded">The data.</param>
    /// <returns><see langword="true"/> when every value is a character of Table 5.</returns>
    private static bool TryReadAlphanumeric(ref BitStreamReader bits, int count, bool fnc1, DecodedText decoded)
    {
        ReadOnlySpan<char> characters = QrEncoder.AlphanumericCharacters;
        Span<char> segment = count <= 256 ? stackalloc char[count] : new char[count];
        int length = 0;
        while (length < count)
        {
            if (count - length >= 2)
            {
                int value = bits.Read(11);
                if (value >= 45 * 45)
                {
                    return false;
                }

                segment[length++] = characters[value / 45];
                segment[length++] = characters[value % 45];
            }
            else
            {
                int value = bits.Read(6);
                if (value >= 45)
                {
                    return false;
                }

                segment[length++] = characters[value];
            }
        }

        if (!fnc1)
        {
            decoded.AppendText(segment);
            return true;
        }

        for (int i = 0; i < segment.Length; i++)
        {
            if (segment[i] != '%')
            {
                decoded.AppendChar(segment[i]);
            }
            else if (i + 1 < segment.Length && segment[i + 1] == '%')
            {
                decoded.AppendChar('%');
                i++;
            }
            else
            {
                decoded.AppendChar(Gs1Data.Separator);
            }
        }

        return true;
    }

    /// <summary>
    /// Reads a Byte mode segment.
    /// </summary>
    /// <param name="bits">The bit stream.</param>
    /// <param name="count">The number of bytes.</param>
    /// <param name="decoded">The data.</param>
    /// <returns><see langword="true"/> when the bytes are there.</returns>
    private static bool TryReadBytes(ref BitStreamReader bits, int count, DecodedText decoded)
    {
        if (count * 8 > bits.Remaining)
        {
            return false;
        }

        for (int i = 0; i < count; i++)
        {
            decoded.AppendByte((byte)bits.Read(8));
        }

        return true;
    }

    /// <summary>
    /// Reads a Kanji mode segment, section 7.4.6: each 13 bit value is a Shift JIS character less 8140 or C140 hexadecimal, its high byte times C0 plus its
    /// low byte.
    /// </summary>
    /// <param name="bits">The bit stream.</param>
    /// <param name="count">The number of characters.</param>
    /// <param name="decoded">The data.</param>
    /// <returns><see langword="true"/> when the characters are there.</returns>
    private static bool TryReadKanji(ref BitStreamReader bits, int count, DecodedText decoded)
    {
        if (count * 13 > bits.Remaining)
        {
            return false;
        }

        byte[] bytes = new byte[count * 2];
        for (int i = 0; i < count; i++)
        {
            int value = bits.Read(13);
            int code = ((value / 0xC0) << 8) | (value % 0xC0);
            code += code < 0x1F00 ? 0x8140 : 0xC140;
            bytes[i * 2] = (byte)(code >> 8);
            bytes[(i * 2) + 1] = (byte)code;
        }

        // ECI 20 is Shift JIS.
        decoded.AppendText(ByteText.Decode(bytes, 20));
        return true;
    }
}
