// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Reads the two and five digit EAN/UPC add-on symbols of section 5.2.2.6 of the GS1 General Specifications on their own: the add-on guard, the digits in
/// number sets A and B with a delineator between each two, and no right guard. The number sets carry the check: the value modulo 4 for two digits, and the
/// checksum through <see cref="EanUpcEncoder.AddOnFiveParity"/> for five.
/// </summary>
internal sealed class EanAddOnReader : LinearReader
{
    private const float MaximumVariance = 0.48F;
    private const float MaximumIndividualVariance = 0.7F;

    /// <inheritdoc/>
    public override BarcodeFamily Families => BarcodeFamily.EanAddOn;

    /// <summary>
    /// Gets the three runs of the add-on guard, bar, space and a bar two modules wide.
    /// </summary>
    private static ReadOnlySpan<int> Guard => [1, 1, 2];

    /// <summary>
    /// Gets the two runs of the delineator, space and bar.
    /// </summary>
    private static ReadOnlySpan<int> Delineator => [1, 1];

    /// <inheritdoc/>
    public override bool TryRead(ReadOnlySpan<int> runs, int start, DecodeContext context, out LinearMatch match)
    {
        Span<char> digits = stackalloc char[5];
        for (int i = start; i + 3 < runs.Length; i += 2)
        {
            if (Variance(runs.Slice(i, 3), Guard, MaximumIndividualVariance) > MaximumVariance)
            {
                continue;
            }

            float module = Sum(runs, i, 3) / 4F;

            // Table 5-11: seven modules before the add-on and five after it.
            if (!IsQuiet(runs, i - 1, module * 5))
            {
                continue;
            }

            foreach (int length in (ReadOnlySpan<int>)[5, 2])
            {
                if (TryReadDigits(runs, i + 3, module, digits[..length], out int end))
                {
                    match = new LinearMatch(BarcodeFamily.EanAddOn, new string(digits[..length]), length == 2 ? "]E1" : "]E2", i, end, length == 5);
                    return true;
                }
            }
        }

        match = default;
        return false;
    }

    /// <summary>
    /// Reads the digits of an add-on of one length and checks their number sets.
    /// </summary>
    /// <param name="runs">The runs.</param>
    /// <param name="position">The index of the first run of the first digit.</param>
    /// <param name="module">The module width from the guard.</param>
    /// <param name="digits">The digits to write, two or five.</param>
    /// <param name="end">The index of the light run after the last digit.</param>
    /// <returns><see langword="true"/> when the add-on reads and checks.</returns>
    private static bool TryReadDigits(ReadOnlySpan<int> runs, int position, float module, Span<char> digits, out int end)
    {
        end = 0;
        int needed = (digits.Length * 4) + ((digits.Length - 1) * 2);
        if (position + needed >= runs.Length)
        {
            return false;
        }

        int parity = 0;
        for (int d = 0; d < digits.Length; d++)
        {
            int digit = EanUpcReader.ReadLeftDigit(runs.Slice(position, 4), out bool setB);
            if (digit < 0)
            {
                return false;
            }

            digits[d] = (char)('0' + digit);
            parity = (parity << 1) | (setB ? 1 : 0);
            position += 4;
            if (d < digits.Length - 1)
            {
                if (Variance(runs.Slice(position, 2), Delineator, MaximumIndividualVariance) > MaximumVariance)
                {
                    return false;
                }

                position += 2;
            }
        }

        if (!IsQuiet(runs, position, module * 5))
        {
            return false;
        }

        if (digits.Length == 2)
        {
            // Section 5.2.2.6.1: the value of the two digits modulo 4 selects AA, AB, BA or BB.
            int value = ((digits[0] - '0') * 10) + (digits[1] - '0');
            if (parity != value % 4)
            {
                return false;
            }
        }
        else
        {
            // Section 5.2.2.6.2: three times the digits in the odd positions plus nine times the digits in the even positions, modulo 10.
            int sum = (3 * ((digits[0] - '0') + (digits[2] - '0') + (digits[4] - '0'))) + (9 * ((digits[1] - '0') + (digits[3] - '0')));
            if (EanUpcEncoder.AddOnFiveParity[sum % 10] != parity)
            {
                return false;
            }
        }

        end = position;
        return true;
    }
}
