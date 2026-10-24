// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Reads EAN-13, UPC-A, EAN-8 and UPC-E, ISO/IEC 15420. Each digit is two bars and two spaces over seven modules in number set A, B or C of
/// <see cref="EanUpcEncoder.NumberSetA"/>. The number sets of the left half carry the leading digit of EAN-13 and the number system and check digit of UPC-E.
/// The reader checks the check digit of section 7.9 of the GS1 General Specifications. Every symbol transmits as thirteen digits except EAN-8.
/// </summary>
internal sealed class EanUpcReader : LinearReader
{
    /// <summary>
    /// The largest mean difference of a digit from its pattern, in modules per module.
    /// </summary>
    private const float MaximumVariance = 0.48F;

    /// <summary>
    /// The largest difference of one element from its pattern, in modules.
    /// </summary>
    private const float MaximumIndividualVariance = 0.7F;

    /// <summary>
    /// The four element widths of each digit of number set A, space first: the runs of the left half. Number set B is the same widths in reverse order, and
    /// number set C, bar first, the same widths as number set A.
    /// </summary>
    private static readonly int[] SetAWidths = CreateSetAWidths();

    /// <summary>
    /// The element widths of each digit of number set B, space first.
    /// </summary>
    private static readonly int[] SetBWidths = CreateSetBWidths();

    /// <inheritdoc/>
    public override BarcodeFamily Families => BarcodeFamily.Ean13 | BarcodeFamily.UpcA | BarcodeFamily.Ean8 | BarcodeFamily.UpcE;

    /// <summary>
    /// Gets the three runs of the normal guard pattern, bar, space, bar.
    /// </summary>
    private static ReadOnlySpan<int> NormalGuard => [1, 1, 1];

    /// <summary>
    /// Gets the five runs of the centre guard pattern, space, bar, space, bar, space.
    /// </summary>
    private static ReadOnlySpan<int> CentreGuard => [1, 1, 1, 1, 1];

    /// <summary>
    /// Gets the six runs of the UPC-E end guard pattern, space, bar, space, bar, space, bar.
    /// </summary>
    private static ReadOnlySpan<int> UpcEGuard => [1, 1, 1, 1, 1, 1];

    /// <inheritdoc/>
    public override bool TryRead(ReadOnlySpan<int> runs, int start, DecodeContext context, out LinearMatch match)
    {
        Span<char> digits = stackalloc char[13];
        for (int i = start; i + 3 < runs.Length; i += 2)
        {
            if (Variance(runs.Slice(i, 3), NormalGuard, MaximumIndividualVariance) > MaximumVariance)
            {
                continue;
            }

            // The quiet zone is at least as wide as the guard. Section 5.2.3.4 asks for 11 modules on the left of EAN-13 and 7 on the left of the others, and
            // the check digit and the fixed structure reject what a narrower gap lets through.
            if (!IsQuiet(runs, i - 1, Sum(runs, i, 3)))
            {
                continue;
            }

            int left = i + 3;
            if (context.Wants(BarcodeFamily.Ean13 | BarcodeFamily.UpcA) && TryReadEan13(runs, left, digits, out int end))
            {
                BarcodeFamily family = digits[0] == '0' && context.Wants(BarcodeFamily.UpcA) ? BarcodeFamily.UpcA : BarcodeFamily.Ean13;
                if (context.Wants(family))
                {
                    match = new LinearMatch(family, new string(digits), "]E0", i, end, true);
                    return true;
                }
            }

            if (context.Wants(BarcodeFamily.UpcE) && TryReadUpcE(runs, left, digits, out end))
            {
                match = new LinearMatch(BarcodeFamily.UpcE, new string(digits), "]E0", i, end, true);
                return true;
            }

            if (context.Wants(BarcodeFamily.Ean8) && TryReadEan8(runs, left, digits[..8], out end))
            {
                match = new LinearMatch(BarcodeFamily.Ean8, new string(digits[..8]), "]E4", i, end, true);
                return true;
            }
        }

        match = default;
        return false;
    }

    /// <summary>
    /// Reads one digit of the left half or of an add-on: four runs, space first, in number set A or B.
    /// </summary>
    /// <param name="runs">The four runs.</param>
    /// <param name="setB">Whether the digit is in number set B.</param>
    /// <returns>The digit, or -1 when no pattern matches.</returns>
    public static int ReadLeftDigit(ReadOnlySpan<int> runs, out bool setB)
    {
        float best = MaximumVariance;
        int digit = -1;
        setB = false;
        for (int d = 0; d < 10; d++)
        {
            float a = Variance(runs, SetAWidths.AsSpan(d * 4, 4), MaximumIndividualVariance);
            if (a < best)
            {
                best = a;
                digit = d;
                setB = false;
            }

            float b = Variance(runs, SetBWidths.AsSpan(d * 4, 4), MaximumIndividualVariance);
            if (b < best)
            {
                best = b;
                digit = d;
                setB = true;
            }
        }

        return digit;
    }

    /// <summary>
    /// Reads an EAN-13 symbol from the first digit of its left half.
    /// </summary>
    /// <param name="runs">The runs.</param>
    /// <param name="position">The index of the first run of the left half.</param>
    /// <param name="digits">The thirteen digits to write.</param>
    /// <param name="end">The index of the light run after the right guard.</param>
    /// <returns><see langword="true"/> when the symbol reads and checks.</returns>
    private static bool TryReadEan13(ReadOnlySpan<int> runs, int position, Span<char> digits, out int end)
    {
        end = 0;

        // Six left digits, the centre guard, six right digits and the right guard.
        if (position + 53 >= runs.Length)
        {
            return false;
        }

        int parity = 0;
        for (int d = 0; d < 6; d++)
        {
            int digit = ReadLeftDigit(runs.Slice(position + (d * 4), 4), out bool setB);
            if (digit < 0)
            {
                return false;
            }

            digits[d + 1] = (char)('0' + digit);
            parity = (parity << 1) | (setB ? 1 : 0);
        }

        int leading = EanUpcEncoder.Ean13LeftParity.IndexOf((byte)parity);
        if (leading < 0
            || Variance(runs.Slice(position + 24, 5), CentreGuard, MaximumIndividualVariance) > MaximumVariance
            || !TryReadRightDigits(runs, position + 29, digits.Slice(7, 6)))
        {
            return false;
        }

        int guard = position + 53;
        if (Variance(runs.Slice(guard, 3), NormalGuard, MaximumIndividualVariance) > MaximumVariance || !IsQuiet(runs, guard + 3, Sum(runs, guard, 3)))
        {
            return false;
        }

        digits[0] = (char)('0' + leading);
        if (Gs1Data.ComputeCheckDigit(digits[..12]) != digits[12] - '0')
        {
            return false;
        }

        end = guard + 3;
        return true;
    }

    /// <summary>
    /// Reads an EAN-8 symbol from the first digit of its left half. Every left digit is in number set A.
    /// </summary>
    /// <param name="runs">The runs.</param>
    /// <param name="position">The index of the first run of the left half.</param>
    /// <param name="digits">The eight digits to write.</param>
    /// <param name="end">The index of the light run after the right guard.</param>
    /// <returns><see langword="true"/> when the symbol reads and checks.</returns>
    private static bool TryReadEan8(ReadOnlySpan<int> runs, int position, Span<char> digits, out int end)
    {
        end = 0;
        if (position + 37 >= runs.Length)
        {
            return false;
        }

        for (int d = 0; d < 4; d++)
        {
            int digit = ReadLeftDigit(runs.Slice(position + (d * 4), 4), out bool setB);
            if (digit < 0 || setB)
            {
                return false;
            }

            digits[d] = (char)('0' + digit);
        }

        if (Variance(runs.Slice(position + 16, 5), CentreGuard, MaximumIndividualVariance) > MaximumVariance
            || !TryReadRightDigits(runs, position + 21, digits.Slice(4, 4)))
        {
            return false;
        }

        int guard = position + 37;
        if (Variance(runs.Slice(guard, 3), NormalGuard, MaximumIndividualVariance) > MaximumVariance || !IsQuiet(runs, guard + 3, Sum(runs, guard, 3)))
        {
            return false;
        }

        if (Gs1Data.ComputeCheckDigit(digits[..7]) != digits[7] - '0')
        {
            return false;
        }

        end = guard + 3;
        return true;
    }

    /// <summary>
    /// Reads a UPC-E symbol from its first digit and expands it to the thirteen digit GTIN-13 form of the UPC-A number it stands for. The number sets of the
    /// six digits give the check digit through <see cref="EanUpcEncoder.UpcEParity"/> for number system 0, and through the complement for number system 1.
    /// </summary>
    /// <param name="runs">The runs.</param>
    /// <param name="position">The index of the first run of the six digits.</param>
    /// <param name="digits">The thirteen digits to write.</param>
    /// <param name="end">The index of the light run after the end guard.</param>
    /// <returns><see langword="true"/> when the symbol reads and checks.</returns>
    private static bool TryReadUpcE(ReadOnlySpan<int> runs, int position, Span<char> digits, out int end)
    {
        end = 0;
        if (position + 30 >= runs.Length)
        {
            return false;
        }

        Span<char> compressed = stackalloc char[7];
        int parity = 0;
        for (int d = 0; d < 6; d++)
        {
            int digit = ReadLeftDigit(runs.Slice(position + (d * 4), 4), out bool setB);
            if (digit < 0)
            {
                return false;
            }

            compressed[d + 1] = (char)('0' + digit);
            parity = (parity << 1) | (setB ? 1 : 0);
        }

        int guard = position + 24;
        if (Variance(runs.Slice(guard, 6), UpcEGuard, MaximumIndividualVariance) > MaximumVariance || !IsQuiet(runs, guard + 6, Sum(runs, guard, 3)))
        {
            return false;
        }

        int check = EanUpcEncoder.UpcEParity.IndexOf((byte)parity);
        int numberSystem = 0;
        if (check < 0)
        {
            check = EanUpcEncoder.UpcEParity.IndexOf((byte)(~parity & 0b111111));
            numberSystem = 1;
        }

        if (check < 0)
        {
            return false;
        }

        compressed[0] = (char)('0' + numberSystem);
        digits[0] = '0';
        UpcESymbology.Expand(compressed, digits.Slice(1, 11));
        if (Gs1Data.ComputeCheckDigit(digits.Slice(1, 11)) != check)
        {
            return false;
        }

        digits[12] = (char)('0' + check);
        end = guard + 6;
        return true;
    }

    /// <summary>
    /// Reads the digits of a right half: four runs each, bar first, in number set C.
    /// </summary>
    /// <param name="runs">The runs.</param>
    /// <param name="position">The index of the first run.</param>
    /// <param name="digits">The digits to write.</param>
    /// <returns><see langword="true"/> when every digit reads.</returns>
    private static bool TryReadRightDigits(ReadOnlySpan<int> runs, int position, Span<char> digits)
    {
        for (int d = 0; d < digits.Length; d++)
        {
            ReadOnlySpan<int> character = runs.Slice(position + (d * 4), 4);
            float best = MaximumVariance;
            int digit = -1;
            for (int v = 0; v < 10; v++)
            {
                float variance = Variance(character, SetAWidths.AsSpan(v * 4, 4), MaximumIndividualVariance);
                if (variance < best)
                {
                    best = variance;
                    digit = v;
                }
            }

            if (digit < 0)
            {
                return false;
            }

            digits[d] = (char)('0' + digit);
        }

        return true;
    }

    /// <summary>
    /// Turns the module patterns of number set A into run widths: a run ends where the module changes.
    /// </summary>
    /// <returns>Four widths for each digit.</returns>
    private static int[] CreateSetAWidths()
    {
        int[] widths = new int[40];
        ReadOnlySpan<byte> patterns = EanUpcEncoder.NumberSetA;
        for (int d = 0; d < 10; d++)
        {
            int run = 0;
            int previous = 0;
            for (int bit = 6; bit >= 0; bit--)
            {
                int module = (patterns[d] >> bit) & 1;
                if (module != previous)
                {
                    run++;
                    previous = module;
                }

                widths[(d * 4) + run]++;
            }
        }

        return widths;
    }

    /// <summary>
    /// Reverses the widths of number set A: number set B is number set C read in reverse, and number set C has the widths of number set A.
    /// </summary>
    /// <returns>Four widths for each digit.</returns>
    private static int[] CreateSetBWidths()
    {
        int[] widths = new int[40];
        for (int d = 0; d < 10; d++)
        {
            for (int e = 0; e < 4; e++)
            {
                widths[(d * 4) + e] = SetAWidths[(d * 4) + 3 - e];
            }
        }

        return widths;
    }
}
