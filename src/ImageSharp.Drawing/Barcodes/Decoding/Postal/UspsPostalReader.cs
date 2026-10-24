// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Reads POSTNET and PLANET, section 708.4.2 of the Domestic Mail Manual: a full frame bar at each end and five bars for each digit and the correction digit,
/// full bars and half bars that stand on the baseline. A POSTNET digit has two full bars and a PLANET digit three, by <see cref="UspsPostalEncoder.Digits"/>.
/// The correction digit makes the digit sum a multiple of ten, and the reader transmits the digits before it.
/// </summary>
internal sealed class UspsPostalReader : HeightModulatedReader
{
    private const int BarsPerDigit = 5;

    /// <summary>
    /// The digit of each five bar pattern, the first bar in the highest bit, or -1.
    /// </summary>
    private static readonly sbyte[] Digits = CreateDigits();

    /// <inheritdoc/>
    public override BarcodeFamily Families => BarcodeFamily.Postnet | BarcodeFamily.Planet;

    /// <inheritdoc/>
    public override bool TryRead(ReadOnlySpan<FourState> states, DecodeContext context, out BarcodeFamily family, out string text, out string identifier)
    {
        family = BarcodeFamily.None;
        text = string.Empty;
        identifier = "]X0";

        // Every bar reaches the baseline, and the frame bars are full.
        int count = states.Length;
        if (count < (6 * BarsPerDigit) + 2 || (count - 2) % BarsPerDigit != 0 || states[0] != FourState.Full || states[^1] != FourState.Full)
        {
            return false;
        }

        foreach (FourState state in states)
        {
            if ((state & FourState.Descender) == 0)
            {
                return false;
            }
        }

        // A POSTNET digit has two full bars and a PLANET digit three. POSTNET carries 5, 9 or 11 digits and PLANET 11 or 13, each with the correction digit.
        int digitCount = (count - 2) / BarsPerDigit;
        int fullBars = 0;
        for (int b = 1; b <= BarsPerDigit; b++)
        {
            fullBars += states[b] == FourState.Full ? 1 : 0;
        }

        bool postnet = fullBars == 2;
        if (postnet ? digitCount is not (6 or 10 or 12) : digitCount is not (12 or 14))
        {
            return false;
        }

        // POSTNET sets the bits of its full bars and PLANET those of its half bars.
        Span<char> digits = stackalloc char[digitCount];
        int sum = 0;
        for (int d = 0; d < digitCount; d++)
        {
            int pattern = 0;
            for (int b = 0; b < BarsPerDigit; b++)
            {
                bool full = states[1 + (d * BarsPerDigit) + b] == FourState.Full;
                pattern = (pattern << 1) | (full == postnet ? 1 : 0);
            }

            int digit = Digits[pattern];
            if (digit < 0)
            {
                return false;
            }

            digits[d] = (char)('0' + digit);
            sum += digit;
        }

        if (sum % 10 != 0)
        {
            return false;
        }

        family = postnet ? BarcodeFamily.Postnet : BarcodeFamily.Planet;
        text = digits[..^1].ToString();
        return true;
    }

    /// <summary>
    /// Inverts <see cref="UspsPostalEncoder.Digits"/>.
    /// </summary>
    /// <returns>The digit of every pattern.</returns>
    private static sbyte[] CreateDigits()
    {
        sbyte[] digits = new sbyte[32];
        digits.AsSpan().Fill(-1);
        ReadOnlySpan<byte> patterns = UspsPostalEncoder.Digits;
        for (int d = 0; d < patterns.Length; d++)
        {
            digits[patterns[d]] = (sbyte)d;
        }

        return digits;
    }
}
