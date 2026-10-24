// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Reads the Australia Post 4-state customer barcodes, the inverse of <see cref="AustraliaPostEncoder"/>: the start and stop bars, the format control code and
/// the sorting code in the N Encoding Table, the customer information field in the table the options name, and the Reed Solomon parity symbols over GF(64),
/// which correct up to two wrong symbols. The bar values are 0 for a full bar, 1 for an ascender, 2 for a descender and 3 for a tracker.
/// </summary>
internal sealed class AustraliaPostReader : HeightModulatedReader
{
    private const int BarsPerSymbol = 3;
    private const int BarsPerDigit = 2;

    /// <summary>
    /// The digit of every two bar value of the N Encoding Table, or -1.
    /// </summary>
    private static readonly sbyte[] NumericValues = Invert(AustraliaPostEncoder.NumericTable, 16);

    /// <summary>
    /// The row of every three bar value of the C Encoding Table.
    /// </summary>
    private static readonly sbyte[] CharacterValues = Invert(AustraliaPostEncoder.CharacterTable, 64);

    /// <inheritdoc/>
    public override BarcodeFamily Families => BarcodeFamily.AustraliaPost;

    /// <inheritdoc/>
    public override bool TryRead(ReadOnlySpan<FourState> states, DecodeContext context, out BarcodeFamily family, out string text, out string identifier)
    {
        family = BarcodeFamily.AustraliaPost;
        text = string.Empty;
        identifier = "]X0";
        int count = states.Length;
        if (count is not (37 or 52 or 67))
        {
            return false;
        }

        Span<byte> values = stackalloc byte[count];
        for (int i = 0; i < count; i++)
        {
            values[i] = states[i] switch
            {
                FourState.Full => 0,
                FourState.Ascender => 1,
                FourState.Descender => 2,
                _ => 3,
            };
        }

        // The start bars and the stop bars are 1 and 3.
        if (values[0] != 1 || values[1] != 3 || values[^2] != 1 || values[^1] != 3)
        {
            return false;
        }

        // The information symbols are the triples of bars after the start bars, and the four parity symbols follow them. The code corrects them in place.
        int parityStart = count - AustraliaPostEncoder.TrailingBars;
        int dataSymbols = (parityStart - 2) / BarsPerSymbol;
        Span<int> codeword = stackalloc int[dataSymbols + AustraliaPostEncoder.ParitySymbols];
        for (int i = 0; i < dataSymbols; i++)
        {
            codeword[i] = SymbolValue(values, 2 + (i * BarsPerSymbol));
        }

        for (int i = 0; i < AustraliaPostEncoder.ParitySymbols; i++)
        {
            codeword[dataSymbols + i] = SymbolValue(values, parityStart + (i * BarsPerSymbol));
        }

        if (ReedSolomonDecoder.Correct(new BinaryReedSolomonField(AustraliaPostEncoder.Field), codeword, AustraliaPostEncoder.ParitySymbols, 1) < 0)
        {
            return false;
        }

        for (int i = 0; i < dataSymbols; i++)
        {
            int start = 2 + (i * BarsPerSymbol);
            values[start] = (byte)(codeword[i] >> 4);
            values[start + 1] = (byte)((codeword[i] >> 2) & 3);
            values[start + 2] = (byte)(codeword[i] & 3);
        }

        // The format control code and the sorting code are ten digits of two bars each.
        Span<char> characters = stackalloc char[AustraliaPostEncoder.FixedDigits + 16];
        for (int d = 0; d < AustraliaPostEncoder.FixedDigits; d++)
        {
            int digit = NumericValues[(values[2 + (d * BarsPerDigit)] * 4) + values[3 + (d * BarsPerDigit)]];
            if (digit < 0)
            {
                return false;
            }

            characters[d] = (char)('0' + digit);
        }

        if (AustraliaPostEncoder.BarCount(characters[..2]) != count)
        {
            return false;
        }

        // The customer information field runs to the parity bars, and filler bars of value 3 close it.
        int length = AustraliaPostEncoder.FixedDigits;
        ReadOnlySpan<byte> customer = values[AustraliaPostEncoder.CustomerFieldStart..parityStart];
        if (context.Options.AustraliaPostCustomerTable == AustraliaPostEncodingTable.Numeric)
        {
            for (int b = 0; b + BarsPerDigit <= customer.Length; b += BarsPerDigit)
            {
                // Two filler bars, value 3 3, are no digit; the digit 9 starts with a bar of value 3.
                int value = (customer[b] * 4) + customer[b + 1];
                if (value == (AustraliaPostEncoder.FillerBar * 4) + AustraliaPostEncoder.FillerBar)
                {
                    break;
                }

                int digit = NumericValues[value];
                if (digit < 0)
                {
                    return false;
                }

                characters[length++] = (char)('0' + digit);
            }
        }
        else
        {
            for (int b = 0; b + BarsPerSymbol <= customer.Length; b += BarsPerSymbol)
            {
                int value = SymbolValue(customer, b);
                if (value == 63)
                {
                    break;
                }

                characters[length++] = AustraliaPostEncoder.Character(CharacterValues[value]);
            }
        }

        text = characters[..length].ToString();
        return true;
    }

    /// <summary>
    /// Returns the value of three bars, their values the digits of a base 4 number.
    /// </summary>
    /// <param name="values">The bar values.</param>
    /// <param name="start">The first bar.</param>
    /// <returns>The value.</returns>
    private static int SymbolValue(ReadOnlySpan<byte> values, int start) => (values[start] * 16) + (values[start + 1] * 4) + values[start + 2];

    /// <summary>
    /// Inverts an encoding table.
    /// </summary>
    /// <param name="table">The bar value of every entry.</param>
    /// <param name="size">The number of bar values.</param>
    /// <returns>The entry of every bar value, or -1.</returns>
    private static sbyte[] Invert(ReadOnlySpan<byte> table, int size)
    {
        sbyte[] inverse = new sbyte[size];
        inverse.AsSpan().Fill(-1);
        for (int i = 0; i < table.Length; i++)
        {
            inverse[table[i]] = (sbyte)i;
        }

        return inverse;
    }
}
