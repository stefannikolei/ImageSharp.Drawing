// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Globalization;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Reads the 2D component of a GS1 Composite symbol, ISO/IEC 24723, the inverse of <see cref="Gs1CompositeComponentEncoder"/>. A CC-A reads through the rows
/// of <see cref="MicroPdf417Rows"/> in the sizes of <see cref="Gs1CompositeComponentEncoder.CcASizes"/> and carries its bit stream in base 928, clause 8. A
/// CC-B is a MicroPDF417 symbol and a CC-C a PDF417 symbol whose data starts with the codeword 920 and carries the bit stream in Byte Compaction mode, clauses
/// 9 and 10. The bit stream holds the encodation method, a compressed field for some leading Application Identifiers and the general purpose field, clause
/// 5, and decodes to the element strings, a Function 1 Symbol Character between two element strings as the group separator.
/// </summary>
internal sealed class Gs1CompositeComponentReader : StackedReader
{
    /// <summary>
    /// The value of the two bits after the method "10" that stand for no date, section 5.3.1. A date value is under 38400, so its first two bits are never
    /// both set.
    /// </summary>
    private const int NoDate = 0b11;

    /// <summary>
    /// The value of the five bits of the number of (90) that marks the ten bit number and the five bit letter, section 5.3.2.
    /// </summary>
    private const int LongPrefix = 0b11111;

    /// <inheritdoc/>
    public override BarcodeFamily Families => BarcodeFamily.Gs1CompositeComponent;

    /// <inheritdoc/>
    public override void Read(DecodeContext context, BitImage bits, bool turned)
    {
        List<CorrectedSymbol> found = [];
        MicroPdf417Reader.Collect(context, bits, turned, Gs1CompositeComponentEncoder.CcASizes, false, found);
        int ccA = found.Count;
        MicroPdf417Reader.Collect(context, bits, turned, MicroPdf417Encoder.Sizes, true, found);
        Pdf417Reader.Collect(context, bits, turned, found);
        for (int i = 0; i < found.Count; i++)
        {
            CorrectedSymbol symbol = found[i];
            byte[] stream;
            int length;
            if (i < ccA)
            {
                if (!TryReadBase928(symbol.Codewords, out stream, out length))
                {
                    continue;
                }
            }
            else if (!symbol.IsComposite || !TryReadBytes(symbol.Codewords, out stream, out length))
            {
                continue;
            }

            if (TryDecode(stream, length, out string? text))
            {
                context.Add(BarcodeFamily.Gs1CompositeComponent, text, "]e0", symbol.Corners);
            }
        }
    }

    /// <summary>
    /// Converts the data codewords of a CC-A back into its bit stream, section 8.5: each group of seven codewords is 69 bits, and a last group of k codewords is
    /// 10k - 1 bits, the most significant codeword and bit first.
    /// </summary>
    /// <param name="codewords">The data codewords.</param>
    /// <param name="stream">The packed bits.</param>
    /// <param name="length">The number of bits.</param>
    /// <returns><see langword="true"/> when every group fits its bits.</returns>
    private static bool TryReadBase928(ReadOnlySpan<int> codewords, out byte[] stream, out int length)
    {
        int groupCodewords = Gs1CompositeComponentEncoder.Base928GroupCodewords;
        int remainder = codewords.Length % groupCodewords;
        length = (Gs1CompositeComponentEncoder.Base928GroupBits * (codewords.Length / groupCodewords)) + (remainder == 0 ? 0 : (10 * remainder) - 1);
        stream = new byte[(length + 7) / 8];
        int position = 0;
        for (int start = 0; start < codewords.Length; start += groupCodewords)
        {
            int count = Math.Min(groupCodewords, codewords.Length - start);
            int groupBits = count == groupCodewords ? Gs1CompositeComponentEncoder.Base928GroupBits : (10 * count) - 1;
            UInt128 value = 0;
            for (int k = 0; k < count; k++)
            {
                int codeword = codewords[start + k];
                if (codeword >= Gs1CompositeComponentEncoder.Base928)
                {
                    return false;
                }

                value = (value * (uint)Gs1CompositeComponentEncoder.Base928) + (uint)codeword;
            }

            if (value >> groupBits != 0)
            {
                return false;
            }

            for (int b = groupBits - 1; b >= 0; b--)
            {
                if (((value >> b) & 1) != 0)
                {
                    stream[position >> 3] |= (byte)(0x80 >> (position & 7));
                }

                position++;
            }
        }

        return true;
    }

    /// <summary>
    /// Reads the bit stream of a CC-B or CC-C: the codeword 920, a latch to Byte Compaction mode and the bytes, then only pad codewords.
    /// </summary>
    /// <param name="codewords">The data codewords.</param>
    /// <param name="stream">The bytes.</param>
    /// <param name="length">The number of bits.</param>
    /// <returns><see langword="true"/> when the codewords hold a byte stream.</returns>
    private static bool TryReadBytes(ReadOnlySpan<int> codewords, out byte[] stream, out int length)
    {
        stream = [];
        length = 0;
        if (codewords.Length < 3 || codewords[1] is not (Pdf417Text.ByteLatch or Pdf417Text.ByteLatchSix))
        {
            return false;
        }

        List<byte> bytes = [];
        int i = 2;
        Pdf417Text.DecodeBytes(codewords, ref i, codewords[1] == Pdf417Text.ByteLatchSix, bytes);
        for (; i < codewords.Length; i++)
        {
            if (codewords[i] != Pdf417Text.TextLatch)
            {
                return false;
            }
        }

        stream = [.. bytes];
        length = stream.Length * 8;
        return length > 0;
    }

    /// <summary>
    /// Decodes the bit stream into the element strings, clause 5.
    /// </summary>
    /// <param name="stream">The packed bits.</param>
    /// <param name="length">The number of bits.</param>
    /// <param name="text">The element strings.</param>
    /// <returns><see langword="true"/> when the bit stream is valid.</returns>
    private static bool TryDecode(ReadOnlySpan<byte> stream, int length, out string text)
    {
        text = string.Empty;
        BitStreamReader bits = new(stream, length);
        ValueStringBuilder builder = new(256);
        ValueStringBuilder field = new(256);
        try
        {
            if (bits.Read(1) == 0)
            {
                // Method "0": the general purpose field carries all the data.
                if (!Gs1GeneralField.TryRead(ref bits, ref field, Gs1GeneralField.Mode.Numeric) || bits.Failed)
                {
                    return false;
                }

                builder.Append(field.AsSpan());
            }
            else if (bits.Read(1) == 0)
            {
                if (!TryReadDateAndLot(ref bits, ref builder, ref field))
                {
                    return false;
                }
            }
            else if (!TryReadAi90(ref bits, ref builder, ref field))
            {
                return false;
            }

            ReadOnlySpan<char> result = Gs1GeneralField.TrimSeparators(builder.AsSpan());
            if (result.IsEmpty)
            {
                return false;
            }

            text = result.ToString();
            return true;
        }
        finally
        {
            builder.Dispose();
            field.Dispose();
        }
    }

    /// <summary>
    /// Reads method "10" of section 5.3.1: a production date (11) or an expiration date (17) in 16 bits and a bit that names its Application Identifier, or
    /// "11" for no date. The general purpose field starts with the data of a batch or lot number (10) up to a separator, or with the separator alone when no
    /// lot number follows the date.
    /// </summary>
    /// <param name="bits">The bit stream after the method.</param>
    /// <param name="builder">The element strings.</param>
    /// <param name="field">The scratch for the general purpose field.</param>
    /// <returns><see langword="true"/> when the fields are valid.</returns>
    private static bool TryReadDateAndLot(ref BitStreamReader bits, ref ValueStringBuilder builder, ref ValueStringBuilder field)
    {
        if (bits.Peek(2) == NoDate)
        {
            bits.Read(2);
        }
        else
        {
            // The date is the year times 384, the month less one times 32, and the day.
            int date = bits.Read(16);
            int identifier = bits.Read(1);
            int year = date / 384;
            int month = ((date % 384) / 32) + 1;
            int day = date % 32;
            if (year > 99 || month > 12)
            {
                return false;
            }

            builder.Append(identifier == 1 ? "17" : "11");
            AppendNumber(ref builder, year);
            AppendNumber(ref builder, month);
            AppendNumber(ref builder, day);
        }

        if (!Gs1GeneralField.TryRead(ref bits, ref field, Gs1GeneralField.Mode.Numeric) || bits.Failed)
        {
            return false;
        }

        ReadOnlySpan<char> data = field.AsSpan();
        if (data.Length > 0 && data[0] == Gs1Data.Separator)
        {
            builder.Append(data[1..]);
        }
        else if (data.Length > 0)
        {
            builder.Append("10");
            builder.Append(data);
        }

        return true;
    }

    /// <summary>
    /// Reads method "11" of section 5.3.2: the leading digits and capital letter of the data of (90), the encodation the rest of it takes, and whether a serial
    /// number (21) or an (8004) follows it. The general purpose field starts with the rest of (90), then the data of the implied Application Identifier.
    /// </summary>
    /// <param name="bits">The bit stream after the method.</param>
    /// <param name="builder">The element strings.</param>
    /// <param name="field">The scratch for the general purpose field.</param>
    /// <returns><see langword="true"/> when the fields are valid.</returns>
    private static bool TryReadAi90(ref BitStreamReader bits, ref ValueStringBuilder builder, ref ValueStringBuilder field)
    {
        // Section 5.3.3: 0 for alphanumeric mode, 10 for numeric mode and 11 for alpha mode.
        Gs1GeneralField.Mode mode = bits.Read(1) == 0
            ? Gs1GeneralField.Mode.Alphanumeric
            : bits.Read(1) == 0 ? Gs1GeneralField.Mode.Numeric : Gs1GeneralField.Mode.Alpha;

        // 0 for no implied Application Identifier, 10 for (21) and 11 for (8004).
        string? implied = bits.Read(1) == 0 ? null : bits.Read(1) == 0 ? "21" : "8004";

        // The number and the letter take nine bits when the number is under 31 and Table 3 holds the letter, and otherwise 11111, ten bits and five.
        int number = bits.Read(5);
        char letter;
        if (number == LongPrefix)
        {
            number = bits.Read(10);
            int code = bits.Read(5);
            if (number > 999 || code > 25)
            {
                return false;
            }

            letter = (char)('A' + code);
        }
        else
        {
            letter = Gs1CompositeComponentEncoder.Table3Letters[bits.Read(4)];
        }

        if (bits.Failed || !Gs1GeneralField.TryRead(ref bits, ref field, mode) || bits.Failed)
        {
            return false;
        }

        // A number of zero stands for no digits, because the digits have no leading zero.
        builder.Append("90");
        if (number > 0)
        {
            builder.Append(number.ToString(CultureInfo.InvariantCulture));
        }

        builder.Append(letter);
        ReadOnlySpan<char> data = field.AsSpan();
        if (implied is null)
        {
            builder.Append(data);
            return true;
        }

        int separator = data.IndexOf(Gs1Data.Separator);
        if (separator < 0)
        {
            return false;
        }

        builder.Append(data[..(separator + 1)]);
        builder.Append(implied);
        builder.Append(data[(separator + 1)..]);
        return true;
    }

    /// <summary>
    /// Appends a number of two digits.
    /// </summary>
    /// <param name="builder">The text.</param>
    /// <param name="value">The number, 0 to 99.</param>
    private static void AppendNumber(ref ValueStringBuilder builder, int value)
    {
        builder.Append((char)('0' + (value / 10)));
        builder.Append((char)('0' + (value % 10)));
    }
}
