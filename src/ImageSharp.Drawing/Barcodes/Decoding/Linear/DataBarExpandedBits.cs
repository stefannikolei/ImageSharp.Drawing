// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Decodes the bit stream of GS1 DataBar Expanded, section 7.2.5 of ISO/IEC 24724, the inverse of the encodation of <see cref="DataBarExpandedEncoder"/>: the
/// linkage bit, the encodation method, the compressed fields of Application Identifier (01) and the weight, price and date fields, and the general purpose
/// field in its numeric, alphanumeric and ISO/IEC 646 modes. The result is the element strings as a scanner transmits them, a Function 1 Symbol Character
/// between two element strings as the group separator.
/// </summary>
internal static class DataBarExpandedBits
{
    /// <summary>
    /// The date field value that stands for no date.
    /// </summary>
    private const int NoDate = 38400;

    /// <summary>
    /// Decodes the data characters of a symbol.
    /// </summary>
    /// <param name="values">The 12 bit values of the data characters, the check character excluded.</param>
    /// <param name="text">The element strings.</param>
    /// <returns><see langword="true"/> when the bits are a valid message.</returns>
    public static bool TryDecode(ReadOnlySpan<int> values, out string text)
    {
        text = string.Empty;

        // The bits of the 12 bit values, packed eight to a byte. A symbol has at most 22 data characters.
        int length = values.Length * DataBarExpandedEncoder.BitsPerSymbolCharacter;
        byte[] packed = new byte[(length + 7) / 8];
        for (int i = 0; i < length; i++)
        {
            int shift = DataBarExpandedEncoder.BitsPerSymbolCharacter - 1 - (i % DataBarExpandedEncoder.BitsPerSymbolCharacter);
            if (((values[i / DataBarExpandedEncoder.BitsPerSymbolCharacter] >> shift) & 1) == 1)
            {
                packed[i >> 3] |= (byte)(0x80 >> (i & 7));
            }
        }

        BitStreamReader bits = new(packed, length);
        ValueStringBuilder builder = new(128);
        try
        {
            // The linkage bit says whether a 2D component is above the symbol. It carries no data.
            bits.Read(1);
            bool generalField;
            if (bits.Read(1) == 1)
            {
                // Method 1: the GTIN, then the general purpose field.
                bits.Read(2);
                int indicator = bits.Read(4);
                if (indicator > 9 || !AppendGtin(ref bits, ref builder, indicator))
                {
                    return false;
                }

                generalField = true;
            }
            else if (bits.Read(1) == 0)
            {
                // Method 00: the general purpose field alone.
                bits.Read(2);
                generalField = true;
            }
            else
            {
                generalField = TryReadCompressed(ref bits, ref builder);
                if (bits.Failed)
                {
                    return false;
                }
            }

            if (generalField && !Gs1GeneralField.TryRead(ref bits, ref builder, Gs1GeneralField.Mode.Numeric))
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
        }
    }

    /// <summary>
    /// Reads the methods that start with 01: the weight methods 0100 and 0101, the price methods 01100 and 01101, and the weight and date methods 0111000 to
    /// 0111111.
    /// </summary>
    /// <param name="bits">The bit stream after 01.</param>
    /// <param name="builder">The text.</param>
    /// <returns><see langword="true"/> when a general purpose field follows.</returns>
    private static bool TryReadCompressed(ref BitStreamReader bits, ref ValueStringBuilder builder)
    {
        if (bits.Read(1) == 0)
        {
            // 0100: (01) and (3103), a weight in kilograms up to 32.767. 0101: (01) and (3202) or (3203), a weight in pounds, 10000 added for (3203).
            bool pounds = bits.Read(1) == 1;
            if (!AppendGtin(ref bits, ref builder, 9))
            {
                bits.Fail();
                return false;
            }

            int weight = bits.Read(15);
            if (pounds)
            {
                builder.Append(weight >= 10000 ? "3203" : "3202");
                AppendNumber(ref builder, weight >= 10000 ? weight - 10000 : weight, 6);
            }
            else
            {
                builder.Append("3103");
                AppendNumber(ref builder, weight, 6);
            }

            return false;
        }

        if (bits.Read(1) == 0)
        {
            // 01100: (01) and (392x), the price in the general purpose field. 01101: (01) and (393x) with the ISO 4217 currency code.
            bool currency = bits.Read(1) == 1;
            bits.Read(2);
            if (!AppendGtin(ref bits, ref builder, 9))
            {
                bits.Fail();
                return false;
            }

            int decimals = bits.Read(2);
            builder.Append(currency ? "393" : "392");
            builder.Append((char)('0' + decimals));
            if (currency)
            {
                int code = bits.Read(10);
                if (code > 999)
                {
                    bits.Fail();
                    return false;
                }

                AppendNumber(ref builder, code, 3);
            }

            return true;
        }

        // 0111xyz: (01), (310x) or (320x), and (11), (13), (15) or (17) or no date. The last bit selects pounds, the two before it the date.
        int selector = bits.Read(3);
        if (!AppendGtin(ref bits, ref builder, 9))
        {
            bits.Fail();
            return false;
        }

        int weightField = bits.Read(20);
        int date = bits.Read(16);
        int decimal1 = weightField / 100000;
        if (decimal1 > 9)
        {
            bits.Fail();
            return false;
        }

        builder.Append((selector & 1) == 1 ? "320" : "310");
        builder.Append((char)('0' + decimal1));
        AppendNumber(ref builder, weightField % 100000, 6);
        if (date != NoDate)
        {
            // The date is the year times 384, the month less one times 32, and the day.
            int year = date / 384;
            int month = ((date % 384) / 32) + 1;
            int day = date % 32;
            if (year > 99 || month > 12)
            {
                bits.Fail();
                return false;
            }

            builder.Append('1');
            builder.Append((char)('1' + ((selector >> 1) * 2)));
            AppendNumber(ref builder, year, 2);
            AppendNumber(ref builder, month, 2);
            AppendNumber(ref builder, day, 2);
        }

        return false;
    }

    /// <summary>
    /// Reads the 12 digits of a GTIN after its indicator digit, four groups of three digits in ten bits each, and appends (01) and the GTIN with its check
    /// digit.
    /// </summary>
    /// <param name="bits">The bit stream.</param>
    /// <param name="builder">The text.</param>
    /// <param name="indicator">The indicator digit.</param>
    /// <returns><see langword="true"/> when the groups are three digit numbers.</returns>
    private static bool AppendGtin(ref BitStreamReader bits, ref ValueStringBuilder builder, int indicator)
    {
        Span<char> gtin = stackalloc char[14];
        gtin[0] = (char)('0' + indicator);
        for (int group = 0; group < 4; group++)
        {
            int value = bits.Read(10);
            if (value > 999)
            {
                return false;
            }

            gtin[1 + (group * 3)] = (char)('0' + (value / 100));
            gtin[2 + (group * 3)] = (char)('0' + ((value / 10) % 10));
            gtin[3 + (group * 3)] = (char)('0' + (value % 10));
        }

        gtin[13] = (char)('0' + Gs1Data.ComputeCheckDigit(gtin[..13]));
        builder.Append("01");
        builder.Append(gtin);
        return !bits.Failed;
    }

    /// <summary>
    /// Appends a number with leading zeros.
    /// </summary>
    /// <param name="builder">The text.</param>
    /// <param name="value">The number.</param>
    /// <param name="digits">The number of digits.</param>
    private static void AppendNumber(ref ValueStringBuilder builder, int value, int digits)
    {
        Span<char> number = builder.AppendSpan(digits);
        for (int i = digits - 1; i >= 0; i--)
        {
            number[i] = (char)('0' + (value % 10));
            value /= 10;
        }
    }
}
