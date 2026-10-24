// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using System.Text;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Decodes the data codewords of PDF417 and MicroPDF417, section 5.4 of ISO/IEC 15438, the inverse of <see cref="Pdf417Codewords"/>: Text Compaction mode
/// and its four sub-modes, Byte Compaction mode, Numeric Compaction mode, the Extended Channel Interpretations, the Macro PDF417 control block and the
/// MicroPDF417 message formats 05 and 06. The bytes read as ISO/IEC 8859-1 unless an ECI names another character set or they form UTF-8.
/// </summary>
internal static class Pdf417Text
{
    private const int TextLatch = 900;
    private const int ByteLatch = 901;
    private const int NumericLatch = 902;
    private const int ByteShift = 913;
    private const int Macro05 = 916;
    private const int Macro06 = 917;
    private const int ReaderInitialization = 921;
    private const int MacroTerminator = 922;
    private const int MacroOptionalField = 923;
    private const int ByteLatchSix = 924;
    private const int EciUserDefined = 925;
    private const int EciGeneralPurpose = 926;
    private const int EciCharacterSet = 927;
    private const int MacroControlBlock = 928;

    /// <summary>
    /// The longest group of Numeric Compaction mode, in codewords.
    /// </summary>
    private const int NumericGroup = 15;

    /// <summary>
    /// The modes the data starts in.
    /// </summary>
    public enum StartMode
    {
        /// <summary>
        /// Text Compaction mode, Alpha sub-mode, where every PDF417 symbol starts.
        /// </summary>
        Text,

        /// <summary>
        /// Byte Compaction mode, where every MicroPDF417 symbol starts.
        /// </summary>
        Byte,
    }

    /// <summary>
    /// The sub-modes of Text Compaction mode.
    /// </summary>
    private enum SubMode
    {
        /// <summary>
        /// The capital letters and the space.
        /// </summary>
        Alpha,

        /// <summary>
        /// The small letters and the space.
        /// </summary>
        Lower,

        /// <summary>
        /// The digits, the space and some punctuation.
        /// </summary>
        Mixed,

        /// <summary>
        /// The punctuation.
        /// </summary>
        Punctuation,
    }

    /// <summary>
    /// Gets the characters of the Text Compaction values of each sub-mode, 30 to a sub-mode in the order Alpha, Lower, Mixed and Punctuation, from Table 5.
    /// A latch or a shift has no character and is '\0' here.
    /// </summary>
    private static ReadOnlySpan<char> TextCharacters =>
        "ABCDEFGHIJKLMNOPQRSTUVWXYZ \0\0\0"
        + "abcdefghijklmnopqrstuvwxyz \0\0\0"
        + "0123456789&\r\t,:#-.$/+%*=^\0 \0\0\0"
        + ";<>@[\\]_`~!\r\t,:\n-.$/\"|*()?{}'\0";

    /// <summary>
    /// Decodes the data codewords.
    /// </summary>
    /// <param name="codewords">The data codewords, after the Symbol Length Descriptor of PDF417, up to the error correction codewords.</param>
    /// <param name="start">The mode the data starts in.</param>
    /// <param name="text">The data.</param>
    /// <returns><see langword="true"/> when the codewords are a valid message.</returns>
    public static bool TryDecode(ReadOnlySpan<int> codewords, StartMode start, out string text)
    {
        text = string.Empty;
        Segments segments = new();
        int i = 0;
        int mode = start == StartMode.Text ? TextLatch : ByteLatch;
        SubMode subMode = SubMode.Alpha;
        string? trailer = null;

        // A MicroPDF417 message in format 05 or 06 starts with the codeword that stands for its header and trailer, and with the mode the format starts in.
        if (codewords.Length > 0 && codewords[0] is Macro05 or Macro06)
        {
            segments.AppendText(codewords[0] == Macro05 ? "[)>\u001E05\u001D" : "[)>\u001E06\u001D");
            trailer = "\u001E\u0004";
            mode = codewords[0] == Macro05 ? NumericLatch : TextLatch;
            subMode = codewords[0] == Macro05 ? SubMode.Alpha : SubMode.Mixed;
            i = 1;
        }

        while (i < codewords.Length)
        {
            int codeword = codewords[i];
            if (codeword >= TextLatch)
            {
                i++;
                switch (codeword)
                {
                    case TextLatch:
                        mode = TextLatch;
                        subMode = SubMode.Alpha;
                        continue;
                    case ByteLatch:
                    case ByteLatchSix:
                    case NumericLatch:
                        mode = codeword;
                        continue;
                    case ByteShift:
                        if (i >= codewords.Length || codewords[i] > 255)
                        {
                            return false;
                        }

                        segments.AppendByte((byte)codewords[i++]);
                        continue;
                    case EciCharacterSet:
                    case EciGeneralPurpose:
                    case EciUserDefined:
                        // Section 5.5: an ECI of one or two codewords. Only the character sets change how the bytes read.
                        int eci = codeword switch
                        {
                            EciCharacterSet when i < codewords.Length => codewords[i],
                            EciGeneralPurpose when i + 1 < codewords.Length => (900 * (codewords[i] + 1)) + codewords[i + 1],
                            EciUserDefined when i < codewords.Length => 810900 + codewords[i],
                            _ => -1,
                        };

                        if (eci < 0)
                        {
                            return false;
                        }

                        i += codeword == EciGeneralPurpose ? 2 : 1;
                        segments.SetEci(eci);
                        continue;
                    case MacroControlBlock:
                    case MacroOptionalField:
                    case MacroTerminator:
                        // The Macro PDF417 control block ends the data of the symbol. It carries the segment of a message across symbols, not the data.
                        i = codewords.Length;
                        continue;
                    case ReaderInitialization:
                        continue;
                    default:
                        // The reserved codewords carry no data.
                        continue;
                }
            }

            switch (mode)
            {
                case TextLatch:
                    if (!TryDecodeText(codewords, ref i, ref subMode, segments))
                    {
                        return false;
                    }

                    break;
                case NumericLatch:
                    if (!TryDecodeNumeric(codewords, ref i, segments))
                    {
                        return false;
                    }

                    break;
                default:
                    DecodeBytes(codewords, ref i, mode == ByteLatchSix, segments);
                    break;
            }
        }

        if (trailer is not null)
        {
            segments.AppendText(trailer);
        }

        text = segments.ToString();
        return text.Length > 0;
    }

    /// <summary>
    /// Decodes Text Compaction codewords up to the next mode codeword: each codeword is two base 30 values, and the values read through the sub-modes, their
    /// latches and their shifts.
    /// </summary>
    /// <param name="codewords">The codewords.</param>
    /// <param name="i">The index of the first codeword, moved past the run.</param>
    /// <param name="subMode">The sub-mode, kept across runs.</param>
    /// <param name="segments">The text.</param>
    /// <returns><see langword="true"/> when the values are valid.</returns>
    private static bool TryDecodeText(ReadOnlySpan<int> codewords, ref int i, ref SubMode subMode, Segments segments)
    {
        ReadOnlySpan<char> characters = TextCharacters;
        SubMode? shift = null;
        while (i < codewords.Length && codewords[i] < TextLatch)
        {
            int codeword = codewords[i++];
            for (int half = 0; half < 2; half++)
            {
                int value = half == 0 ? codeword / 30 : codeword % 30;
                SubMode current = shift ?? subMode;
                shift = null;
                char character = characters[((int)current * 30) + value];
                if (character != '\0')
                {
                    segments.AppendChar(character);
                    continue;
                }

                // The latches and shifts of Table 5.
                switch (current, value)
                {
                    case (SubMode.Alpha, 27):
                    case (SubMode.Mixed, 27):
                        subMode = SubMode.Lower;
                        break;
                    case (SubMode.Alpha, 28):
                    case (SubMode.Lower, 28):
                        subMode = SubMode.Mixed;
                        break;
                    case (SubMode.Lower, 27):
                        shift = SubMode.Alpha;
                        break;
                    case (SubMode.Mixed, 25):
                        subMode = SubMode.Punctuation;
                        break;
                    case (SubMode.Mixed, 28):
                    case (SubMode.Punctuation, 29):
                        subMode = SubMode.Alpha;
                        break;
                    case (_, 29):
                        shift = SubMode.Punctuation;
                        break;
                    default:
                        return false;
                }
            }

            // A shift as the last value of a codeword pads the run and has no character after it, section 5.4.2.4.
            if (shift is not null && (i >= codewords.Length || codewords[i] >= TextLatch))
            {
                shift = null;
            }
        }

        return true;
    }

    /// <summary>
    /// Decodes Byte Compaction codewords up to the next mode codeword. A group of five codewords is six bytes in base 900; after the latch 901 the codewords
    /// after the last whole group, which nothing but a mode codeword or the end follows, are one byte each.
    /// </summary>
    /// <param name="codewords">The codewords.</param>
    /// <param name="i">The index of the first codeword, moved past the run.</param>
    /// <param name="six">Whether the run took the latch 924, so every group is six bytes.</param>
    /// <param name="segments">The text.</param>
    private static void DecodeBytes(ReadOnlySpan<int> codewords, ref int i, bool six, Segments segments)
    {
        int end = i;
        while (end < codewords.Length && codewords[end] < TextLatch)
        {
            end++;
        }

        // After the latch 901 the byte count is not a multiple of six, so the last one to five codewords are single bytes even when they are five.
        int count = end - i;
        int groups = six ? count / 5 : (count - 1) / 5;

        Span<byte> bytes = stackalloc byte[6];
        for (int g = 0; g < groups; g++)
        {
            long value = 0;
            for (int k = 0; k < 5; k++)
            {
                value = (value * 900) + codewords[i++];
            }

            for (int k = 5; k >= 0; k--)
            {
                bytes[k] = (byte)(value & 0xFF);
                value >>= 8;
            }

            foreach (byte b in bytes)
            {
                segments.AppendByte(b);
            }
        }

        while (i < end)
        {
            segments.AppendByte((byte)codewords[i++]);
        }
    }

    /// <summary>
    /// Decodes Numeric Compaction codewords up to the next mode codeword: each group of up to 15 codewords is a number in base 900 whose decimal digits after
    /// the leading 1 are the data.
    /// </summary>
    /// <param name="codewords">The codewords.</param>
    /// <param name="i">The index of the first codeword, moved past the run.</param>
    /// <param name="segments">The text.</param>
    /// <returns><see langword="true"/> when every group starts with the digit 1.</returns>
    private static bool TryDecodeNumeric(ReadOnlySpan<int> codewords, ref int i, Segments segments)
    {
        while (i < codewords.Length && codewords[i] < TextLatch)
        {
            BigInteger value = BigInteger.Zero;
            int count = 0;
            while (count < NumericGroup && i < codewords.Length && codewords[i] < TextLatch)
            {
                value = (value * 900) + codewords[i++];
                count++;
            }

            string digits = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (digits.Length < 2 || digits[0] != '1')
            {
                return false;
            }

            segments.AppendText(digits.AsSpan(1));
        }

        return true;
    }

    /// <summary>
    /// The decoded data: text, and bytes that read in the character set of the current ECI when the text ends or the ECI changes.
    /// </summary>
    private sealed class Segments
    {
        private readonly StringBuilder text = new();
        private readonly List<byte> bytes = [];
        private int eci = -1;

        /// <summary>
        /// Appends text.
        /// </summary>
        /// <param name="value">The text.</param>
        public void AppendText(ReadOnlySpan<char> value)
        {
            this.Flush();
            this.text.Append(value);
        }

        /// <summary>
        /// Appends a character of Text Compaction mode.
        /// </summary>
        /// <param name="value">The character.</param>
        public void AppendChar(char value)
        {
            this.Flush();
            this.text.Append(value);
        }

        /// <summary>
        /// Appends a byte.
        /// </summary>
        /// <param name="value">The byte.</param>
        public void AppendByte(byte value) => this.bytes.Add(value);

        /// <summary>
        /// Sets the ECI the bytes after it read in.
        /// </summary>
        /// <param name="value">The ECI number.</param>
        public void SetEci(int value)
        {
            this.Flush();
            this.eci = value;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            this.Flush();
            return this.text.ToString();
        }

        /// <summary>
        /// Converts the pending bytes to text.
        /// </summary>
        private void Flush()
        {
            if (this.bytes.Count == 0)
            {
                return;
            }

            this.text.Append(ByteText.Decode(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(this.bytes), this.eci));
            this.bytes.Clear();
        }
    }
}
