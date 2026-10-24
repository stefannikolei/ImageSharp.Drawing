// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Reads the 4-state Royal Mail Mailmark barcodes C and L, the inverse of <see cref="FourStateMailmarkEncoder"/>: every three bars carry a six-bit extender
/// group, the groups give the data and check numbers through Table 6 and Table 5, the Reed-Solomon code over the Galois field of 32 values corrects them, and
/// the consolidated data value gives the six user fields of the application string.
/// </summary>
internal sealed class MailmarkReader : HeightModulatedReader
{
    private const int BarsPerGroup = 3;

    /// <summary>
    /// The number of every six-bit symbol of 30 values, or -1.
    /// </summary>
    private static readonly sbyte[] EvenNumbers = Invert(FourStateMailmarkEncoder.EvenSymbols);

    /// <summary>
    /// The number of every six-bit symbol of 32 values, or -1.
    /// </summary>
    private static readonly sbyte[] OddNumbers = Invert(FourStateMailmarkEncoder.OddSymbols);

    /// <inheritdoc/>
    public override BarcodeFamily Families => BarcodeFamily.Mailmark;

    /// <inheritdoc/>
    public override bool TryRead(ReadOnlySpan<FourState> states, DecodeContext context, out BarcodeFamily family, out string text, out string identifier)
    {
        family = BarcodeFamily.Mailmark;
        text = string.Empty;
        identifier = "]X0";

        // Barcode C has 66 bars and barcode L 78.
        bool barcodeL = states.Length == 78;
        if (!barcodeL && states.Length != 66)
        {
            return false;
        }

        int supplyChainLength = barcodeL ? 6 : 2;
        int dataCount = barcodeL ? 19 : 16;
        int thirtyValued = barcodeL ? 11 : 9;
        int checkCount = barcodeL ? 7 : 6;
        ReadOnlySpan<byte> extenderGroups = barcodeL ? FourStateMailmarkEncoder.ExtenderGroupsL : FourStateMailmarkEncoder.ExtenderGroupsC;

        // Table 7: the ascenders of an even numbered group are its high three bits and its descenders the low three, and the reverse in an odd numbered group.
        int groups = states.Length / BarsPerGroup;
        Span<int> numbers = stackalloc int[groups];
        for (int i = 0; i < groups; i++)
        {
            int group = extenderGroups[i];
            int ascenders = 0;
            int descenders = 0;
            for (int bar = 0; bar < BarsPerGroup; bar++)
            {
                FourState state = states[(group * BarsPerGroup) + bar];
                ascenders = (ascenders << 1) | ((state & FourState.Ascender) != 0 ? 1 : 0);
                descenders = (descenders << 1) | ((state & FourState.Descender) != 0 ? 1 : 0);
            }

            int symbol = (group & 1) == 0 ? (ascenders << 3) | descenders : (descenders << 3) | ascenders;
            int number = i < thirtyValued ? EvenNumbers[symbol] : OddNumbers[symbol];

            // A symbol of the wrong parity is an erasure the code can still correct as an error.
            numbers[i] = Math.Max(number, 0);
        }

        if (ReedSolomonDecoder.Correct(new BinaryReedSolomonField(FourStateMailmarkEncoder.Field), numbers, checkCount, 1) < 0)
        {
            return false;
        }

        // The data numbers are the digits of the consolidated value, the most significant first, of radix 30 for the first ones and 32 for the rest.
        UInt128 consolidated = 0;
        for (int i = 0; i < dataCount; i++)
        {
            uint radix = i < thirtyValued ? 30U : 32U;
            if (i > 0 && (uint)numbers[i] >= radix)
            {
                return false;
            }

            consolidated = i == 0 ? (uint)numbers[i] : (consolidated * radix) + (uint)numbers[i];
        }

        // The fields are the digits of a mixed radix number, the version the least significant.
        Span<char> characters = stackalloc char[FourStateMailmarkEncoder.LengthL];
        int length = barcodeL ? FourStateMailmarkEncoder.LengthL : FourStateMailmarkEncoder.LengthC;
        characters[1] = Digit(ref consolidated, FourStateMailmarkEncoder.VersionCharacters);
        characters[0] = Digit(ref consolidated, FourStateMailmarkEncoder.FormatCharacters);
        characters[2] = Digit(ref consolidated, FourStateMailmarkEncoder.ClassCharacters);
        for (int i = 3 + supplyChainLength - 1; i >= 3; i--)
        {
            characters[i] = Digit(ref consolidated, "0123456789");
        }

        int itemStart = 3 + supplyChainLength;
        for (int i = itemStart + 7; i >= itemStart; i--)
        {
            characters[i] = Digit(ref consolidated, "0123456789");
        }

        Span<char> destination = characters.Slice(itemStart + 8, FourStateMailmarkEncoder.DestinationLength);
        if (consolidated > ulong.MaxValue || !TryWriteDestination((ulong)consolidated, destination))
        {
            return false;
        }

        text = characters[..length].ToString();
        return true;
    }

    /// <summary>
    /// Takes the least significant digit of a mixed radix value as a character of a field.
    /// </summary>
    /// <param name="value">The value, divided by the radix on return.</param>
    /// <param name="allowed">The characters of the field in value order.</param>
    /// <returns>The character.</returns>
    private static char Digit(ref UInt128 value, string allowed)
    {
        int digit = (int)(value % (uint)allowed.Length);
        value /= (uint)allowed.Length;
        return allowed[digit];
    }

    /// <summary>
    /// Writes the Destination Post Code plus DPS field of its internal user field value: the international designation for zero, or the domestic sorting code
    /// of the pattern whose offset the value reaches.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="field">The nine characters to write.</param>
    /// <returns><see langword="true"/> when the value is a valid field.</returns>
    private static bool TryWriteDestination(ulong value, Span<char> field)
    {
        if (value == 0)
        {
            FourStateMailmarkEncoder.International.CopyTo(field);
            return true;
        }

        ReadOnlySpan<ulong> offsets = FourStateMailmarkEncoder.PostcodeOffsets;
        int pattern = offsets.Length - 1;
        while (pattern >= 0 && value < offsets[pattern])
        {
            pattern--;
        }

        if (pattern < 0)
        {
            return false;
        }

        // The characters of the pattern are the digits of the value less the offset, the last character the least significant.
        ulong rest = value - offsets[pattern];
        ReadOnlySpan<byte> types = FourStateMailmarkEncoder.PostcodePatterns.Slice(pattern * field.Length, field.Length);
        for (int i = field.Length - 1; i >= 0; i--)
        {
            switch ((char)types[i])
            {
                case 'F':
                    field[i] = (char)('A' + (int)(rest % 26));
                    rest /= 26;
                    break;
                case 'N':
                    field[i] = (char)('0' + (int)(rest % 10));
                    rest /= 10;
                    break;
                case 'L':
                    field[i] = FourStateMailmarkEncoder.LimitedAlphabetic[(int)(rest % 20)];
                    rest /= 20;
                    break;
                default:
                    field[i] = ' ';
                    break;
            }
        }

        return rest == 0;
    }

    /// <summary>
    /// Inverts a symbol table.
    /// </summary>
    /// <param name="symbols">The symbol of every number.</param>
    /// <returns>The number of every six-bit symbol, or -1.</returns>
    private static sbyte[] Invert(ReadOnlySpan<byte> symbols)
    {
        sbyte[] numbers = new sbyte[64];
        numbers.AsSpan().Fill(-1);
        for (int i = 0; i < symbols.Length; i++)
        {
            numbers[symbols[i]] = (sbyte)i;
        }

        return numbers;
    }
}
