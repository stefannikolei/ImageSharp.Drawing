// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Decodes the data codewords of DotCode, the inverse of the codeword writer of <see cref="DotCodeEncoder"/>: code sets A, B and C with their latches and
/// shifts, the upper shifts, binary mode with its shifts and terminators, the macro codewords, and the FNC1 characters of GS1 data.
/// </summary>
internal static class DotCodeBitstream
{
    /// <summary>
    /// The FNC2 codeword, which carries an ECI or the message append. It has this value in code sets A, B and C.
    /// </summary>
    private const int Fnc2 = 108;

    /// <summary>
    /// The FNC3 codeword, which marks reader programming. It has this value in code sets A, B and C.
    /// </summary>
    private const int Fnc3 = 109;

    /// <summary>
    /// The code sets of the data.
    /// </summary>
    private enum CodeSet
    {
        /// <summary>
        /// Code set A: space to underscore and the control characters.
        /// </summary>
        A,

        /// <summary>
        /// Code set B: space to DEL, HT, FS, GS, RS and the CR LF pair.
        /// </summary>
        B,

        /// <summary>
        /// Code set C: digit pairs.
        /// </summary>
        C,

        /// <summary>
        /// Binary mode: five bytes in six codewords.
        /// </summary>
        Binary,
    }

    /// <summary>
    /// Decodes the data codewords of a symbol, the pads included.
    /// </summary>
    /// <param name="codewords">The data codewords, unmasked.</param>
    /// <param name="text">The data.</param>
    /// <param name="identifier">The symbology identifier.</param>
    /// <returns><see langword="true"/> when the codewords are valid.</returns>
    public static bool TryDecode(ReadOnlySpan<int> codewords, out string text, out string identifier)
    {
        text = string.Empty;
        identifier = "]J0";
        if (codewords.IsEmpty)
        {
            return false;
        }

        // A digit pair or the 17xxxxxx10 macro in the first codeword marks GS1 data. Other data that starts with two digits leads with an FNC1, and GS1 data
        // that does not start with them leads with its own FNC1. Neither FNC1 carries a character.
        DecodedText decoded = new();
        bool gs1 = codewords[0] <= DotCodeEncoder.CSeventeenTen;
        int i = 0;
        if (codewords[0] == DotCodeEncoder.Fnc1Codeword)
        {
            gs1 = codewords.Length < 2 || codewords[1] > DotCodeEncoder.CSeventeenTen;
            i = 1;
        }

        // A latch to code set B and a macro codeword stand for the header [)>RS and two digits, and imply the trailer.
        CodeSet set = CodeSet.C;
        string trailer = string.Empty;
        if (codewords.Length > 1 && codewords[0] == DotCodeEncoder.CLatchB && codewords[1] is >= DotCodeEncoder.Macro05 and <= DotCodeEncoder.MacroAny)
        {
            decoded.AppendText("[)>\u001E");
            decoded.AppendText(codewords[1] switch
            {
                DotCodeEncoder.Macro05 => "05\u001D",
                DotCodeEncoder.Macro06 => "06\u001D",
                DotCodeEncoder.Macro12 => "12\u001D",
                _ => string.Empty,
            });

            trailer = codewords[1] == DotCodeEncoder.MacroAny ? "\u0004" : "\u001E\u0004";
            set = CodeSet.B;
            i = 2;
        }

        while (i < codewords.Length)
        {
            int codeword = codewords[i++];
            if (set == CodeSet.Binary)
            {
                if (!TryDecodeBinaryCodeword(codewords, ref i, codeword, ref set, decoded))
                {
                    return false;
                }

                continue;
            }

            // The codewords from FNC1 on are the same in code sets A, B and C.
            switch (codeword)
            {
                case DotCodeEncoder.Fnc1Codeword:
                    decoded.AppendChar(Gs1Data.Separator);
                    continue;
                case Fnc2:
                case Fnc3:
                    return false;
                case DotCodeEncoder.UpperShiftA:
                case DotCodeEncoder.UpperShiftB:
                    // The next codeword is a character of code set A or B, 128 higher.
                    int shifted = i < codewords.Length && codewords[i] < 96
                        ? codeword == DotCodeEncoder.UpperShiftA ? AChar(codewords[i]) : codewords[i] + 32
                        : -1;

                    if (shifted < 0)
                    {
                        return false;
                    }

                    decoded.AppendByte((byte)(shifted + 128));
                    i++;
                    continue;
                case DotCodeEncoder.BinaryLatch:
                    set = CodeSet.Binary;
                    continue;
            }

            bool valid = set switch
            {
                CodeSet.A => TryDecodeA(codewords, ref i, codeword, ref set, decoded),
                CodeSet.B => TryDecodeB(codewords, ref i, codeword, ref set, decoded),
                _ => TryDecodeC(codewords, ref i, codeword, ref set, decoded),
            };

            if (!valid)
            {
                return false;
            }
        }

        decoded.AppendText(trailer);
        text = decoded.ToString();
        identifier = gs1 ? "]J1" : "]J0";
        return text.Length > 0;
    }

    /// <summary>
    /// Decodes a codeword of code set C: a digit pair, the 17xxxxxx10 macro, a latch, or a shift into code set B.
    /// </summary>
    /// <param name="codewords">The data codewords.</param>
    /// <param name="i">The index after the codeword, moved past what it reads.</param>
    /// <param name="codeword">The codeword.</param>
    /// <param name="set">The code set, changed by a latch.</param>
    /// <param name="decoded">The data.</param>
    /// <returns><see langword="true"/> when the codeword is valid.</returns>
    private static bool TryDecodeC(ReadOnlySpan<int> codewords, ref int i, int codeword, ref CodeSet set, DecodedText decoded)
    {
        if (codeword < 100)
        {
            AppendPair(decoded, codeword);
            return true;
        }

        switch (codeword)
        {
            case DotCodeEncoder.CSeventeenTen:
                // The (17) element string and its date in three pairs, then the (10) that starts a batch number.
                if (codewords.Length - i < 3)
                {
                    return false;
                }

                decoded.AppendText("17");
                for (int k = 0; k < 3; k++)
                {
                    if (codewords[i] >= 100)
                    {
                        return false;
                    }

                    AppendPair(decoded, codewords[i++]);
                }

                decoded.AppendText("10");
                return true;
            case DotCodeEncoder.CLatchA:
                set = CodeSet.A;
                return true;
            case DotCodeEncoder.CLatchB:
                set = CodeSet.B;
                return true;
            case >= DotCodeEncoder.CShiftB1 and <= DotCodeEncoder.CShiftB1 + 3:
                return TryDecodeShiftB(codewords, ref i, codeword - DotCodeEncoder.CShiftB1 + 1, decoded);
            default:
                return false;
        }
    }

    /// <summary>
    /// Decodes a codeword of code set A: a character, a latch, or a shift into code set B or C.
    /// </summary>
    /// <param name="codewords">The data codewords.</param>
    /// <param name="i">The index after the codeword, moved past what it reads.</param>
    /// <param name="codeword">The codeword.</param>
    /// <param name="set">The code set, changed by a latch.</param>
    /// <param name="decoded">The data.</param>
    /// <returns><see langword="true"/> when the codeword is valid.</returns>
    private static bool TryDecodeA(ReadOnlySpan<int> codewords, ref int i, int codeword, ref CodeSet set, DecodedText decoded)
    {
        switch (codeword)
        {
            case < 96:
                decoded.AppendByte((byte)AChar(codeword));
                return true;
            case <= DotCodeEncoder.AShiftB1 + 5:
                return TryDecodeShiftB(codewords, ref i, codeword - DotCodeEncoder.AShiftB1 + 1, decoded);
            case DotCodeEncoder.ALatchB:
                set = CodeSet.B;
                return true;
            case DotCodeEncoder.LatchC:
                set = CodeSet.C;
                return true;
            case >= DotCodeEncoder.ShiftC2 and <= DotCodeEncoder.ShiftC2 + 2:
                return TryDecodeShiftC(codewords, ref i, codeword - DotCodeEncoder.ShiftC2 + 2, decoded);
            default:
                return false;
        }
    }

    /// <summary>
    /// Decodes a codeword of code set B: a character, a latch, or a shift into code set A or C.
    /// </summary>
    /// <param name="codewords">The data codewords.</param>
    /// <param name="i">The index after the codeword, moved past what it reads.</param>
    /// <param name="codeword">The codeword.</param>
    /// <param name="set">The code set, changed by a latch.</param>
    /// <param name="decoded">The data.</param>
    /// <returns><see langword="true"/> when the codeword is valid.</returns>
    private static bool TryDecodeB(ReadOnlySpan<int> codewords, ref int i, int codeword, ref CodeSet set, DecodedText decoded)
    {
        switch (codeword)
        {
            case <= 100:
                AppendB(decoded, codeword);
                return true;
            case DotCodeEncoder.BShiftA:
                if (i >= codewords.Length || codewords[i] >= 96)
                {
                    return false;
                }

                decoded.AppendByte((byte)AChar(codewords[i++]));
                return true;
            case DotCodeEncoder.BLatchA:
                set = CodeSet.A;
                return true;
            case DotCodeEncoder.LatchC:
                set = CodeSet.C;
                return true;
            case >= DotCodeEncoder.ShiftC2 and <= DotCodeEncoder.ShiftC2 + 2:
                return TryDecodeShiftC(codewords, ref i, codeword - DotCodeEncoder.ShiftC2 + 2, decoded);
            default:
                return false;
        }
    }

    /// <summary>
    /// Decodes a codeword of binary mode: a run of base 103 digits, a shift into code set C, or a terminator into a code set.
    /// </summary>
    /// <param name="codewords">The data codewords.</param>
    /// <param name="i">The index after the codeword, moved past what it reads.</param>
    /// <param name="codeword">The codeword.</param>
    /// <param name="set">The code set, changed by a terminator.</param>
    /// <param name="decoded">The data.</param>
    /// <returns><see langword="true"/> when the codeword is valid.</returns>
    private static bool TryDecodeBinaryCodeword(ReadOnlySpan<int> codewords, ref int i, int codeword, ref CodeSet set, DecodedText decoded)
    {
        switch (codeword)
        {
            case < DotCodeEncoder.BinaryCodewordRadix:
                // The digits run to the next codeword that is not one, in groups of six; a shorter last group of n + 1 digits holds n bytes.
                int start = i - 1;
                int end = i;
                while (end < codewords.Length && codewords[end] < DotCodeEncoder.BinaryCodewordRadix)
                {
                    end++;
                }

                for (int group = start; group < end; group += 6)
                {
                    if (!TryDecodeBinaryGroup(codewords[group..Math.Min(end, group + 6)], decoded))
                    {
                        return false;
                    }
                }

                i = end;
                return true;
            case <= DotCodeEncoder.BinaryShiftC2 + 5:
                return TryDecodeShiftC(codewords, ref i, codeword - DotCodeEncoder.BinaryShiftC2 + 2, decoded);
            case DotCodeEncoder.BinaryTerminateA:
                set = CodeSet.A;
                return true;
            case DotCodeEncoder.BinaryTerminateB:
                set = CodeSet.B;
                return true;
            case DotCodeEncoder.BinaryTerminateC:
                set = CodeSet.C;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Decodes one group of binary mode: n + 1 base 103 digits, the most significant first, whose value read in base 259 is n bytes.
    /// </summary>
    /// <param name="digits">The digits, two to six.</param>
    /// <param name="decoded">The data.</param>
    /// <returns><see langword="true"/> when the group holds bytes.</returns>
    private static bool TryDecodeBinaryGroup(ReadOnlySpan<int> digits, DecodedText decoded)
    {
        if (digits.Length < 2)
        {
            return false;
        }

        long value = 0;
        foreach (int digit in digits)
        {
            value = (value * DotCodeEncoder.BinaryCodewordRadix) + digit;
        }

        Span<byte> bytes = stackalloc byte[5];
        int count = digits.Length - 1;
        for (int k = count - 1; k >= 0; k--)
        {
            int b = (int)(value % DotCodeEncoder.BinaryByteRadix);
            if (b > 255)
            {
                return false;
            }

            bytes[k] = (byte)b;
            value /= DotCodeEncoder.BinaryByteRadix;
        }

        if (value != 0)
        {
            return false;
        }

        foreach (byte b in bytes[..count])
        {
            decoded.AppendByte(b);
        }

        return true;
    }

    /// <summary>
    /// Decodes the characters of a shift into code set B: characters, the CR LF pair, HT, FS, GS, RS and FNC1.
    /// </summary>
    /// <param name="codewords">The data codewords.</param>
    /// <param name="i">The index of the first character, moved past them.</param>
    /// <param name="count">The number of characters.</param>
    /// <param name="decoded">The data.</param>
    /// <returns><see langword="true"/> when the characters are valid.</returns>
    private static bool TryDecodeShiftB(ReadOnlySpan<int> codewords, ref int i, int count, DecodedText decoded)
    {
        for (int k = 0; k < count; k++)
        {
            if (i >= codewords.Length)
            {
                return false;
            }

            int codeword = codewords[i++];
            if (codeword == DotCodeEncoder.Fnc1Codeword)
            {
                decoded.AppendChar(Gs1Data.Separator);
            }
            else if (codeword <= 100)
            {
                AppendB(decoded, codeword);
            }
            else
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Decodes the units of a shift into code set C: digit pairs and FNC1 characters.
    /// </summary>
    /// <param name="codewords">The data codewords.</param>
    /// <param name="i">The index of the first unit, moved past them.</param>
    /// <param name="count">The number of units.</param>
    /// <param name="decoded">The data.</param>
    /// <returns><see langword="true"/> when the units are valid.</returns>
    private static bool TryDecodeShiftC(ReadOnlySpan<int> codewords, ref int i, int count, DecodedText decoded)
    {
        for (int k = 0; k < count; k++)
        {
            if (i >= codewords.Length)
            {
                return false;
            }

            int codeword = codewords[i++];
            if (codeword == DotCodeEncoder.Fnc1Codeword)
            {
                decoded.AppendChar(Gs1Data.Separator);
            }
            else if (codeword < 100)
            {
                AppendPair(decoded, codeword);
            }
            else
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Returns the character of a code set A value: 0 to 63 are space to underscore, and 64 to 95 the control characters NUL to US.
    /// </summary>
    /// <param name="value">The value, 0 to 95.</param>
    /// <returns>The character.</returns>
    private static int AChar(int value) => value < 64 ? value + 32 : value - 64;

    /// <summary>
    /// Appends the character of a code set B value: 0 to 95 are space to DEL, 96 the CR LF pair, and 97 to 100 HT, FS, GS and RS.
    /// </summary>
    /// <param name="decoded">The data.</param>
    /// <param name="value">The value, 0 to 100.</param>
    private static void AppendB(DecodedText decoded, int value)
    {
        switch (value)
        {
            case < 96:
                decoded.AppendByte((byte)(value + 32));
                break;
            case DotCodeEncoder.BCrLf:
                decoded.AppendByte((byte)'\r');
                decoded.AppendByte((byte)'\n');
                break;
            case 97:
                decoded.AppendByte((byte)'\t');
                break;
            default:
                decoded.AppendByte((byte)(value - 98 + 28));
                break;
        }
    }

    /// <summary>
    /// Appends a digit pair.
    /// </summary>
    /// <param name="decoded">The data.</param>
    /// <param name="value">The pair, 0 to 99.</param>
    private static void AppendPair(DecodedText decoded, int value)
    {
        decoded.AppendByte((byte)('0' + (value / 10)));
        decoded.AppendByte((byte)('0' + (value % 10)));
    }
}
