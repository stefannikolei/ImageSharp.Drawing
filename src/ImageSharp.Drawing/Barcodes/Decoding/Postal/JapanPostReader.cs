// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Reads the Japan Post customer barcode, the inverse of <see cref="JapanPostEncoder"/>: a start code, twenty codes of three bars, a check code that brings
/// the sum of the codes to a multiple of 19, and a stop code. A control code CC1, CC2 or CC3 and a digit stand for a letter, and CC4 fills the codes after the
/// data.
/// </summary>
internal sealed class JapanPostReader : HeightModulatedReader
{
    /// <summary>
    /// The code value of every three bar pattern, the first bar in the highest two bits, or -1.
    /// </summary>
    private static readonly sbyte[] Codes = CreateCodes();

    /// <inheritdoc/>
    public override BarcodeFamily Families => BarcodeFamily.JapanPost;

    /// <inheritdoc/>
    public override bool TryRead(ReadOnlySpan<FourState> states, DecodeContext context, out BarcodeFamily family, out string text, out string identifier)
    {
        family = BarcodeFamily.JapanPost;
        text = string.Empty;
        identifier = "]X0";
        if (states.Length != JapanPostEncoder.BarCount
            || states[0] != FourState.Full
            || states[1] != FourState.Descender
            || states[^2] != FourState.Descender
            || states[^1] != FourState.Full)
        {
            return false;
        }

        // The twenty codes and the check code.
        int bars = JapanPostEncoder.BarsPerCode;
        Span<byte> codes = stackalloc byte[JapanPostEncoder.CodeCount + 1];
        for (int c = 0; c < codes.Length; c++)
        {
            int pattern = 0;
            for (int b = 0; b < bars; b++)
            {
                pattern = (pattern << 2) | (int)states[2 + (c * bars) + b];
            }

            int code = Codes[pattern];
            if (code < 0)
            {
                return false;
            }

            codes[c] = (byte)code;
        }

        if (JapanPostEncoder.CheckCode(codes[..^1]) != codes[^1])
        {
            return false;
        }

        Span<char> characters = stackalloc char[JapanPostEncoder.CodeCount];
        int count = 0;
        for (int c = 0; c < JapanPostEncoder.CodeCount; c++)
        {
            int code = codes[c];
            if (code == JapanPostEncoder.ControlCode4)
            {
                // The filler fills every code after the data.
                break;
            }

            if (code < JapanPostEncoder.Hyphen)
            {
                characters[count++] = (char)('0' + code);
            }
            else if (code == JapanPostEncoder.Hyphen)
            {
                characters[count++] = '-';
            }
            else if (code < JapanPostEncoder.ControlCode4 && c + 1 < JapanPostEncoder.CodeCount && codes[c + 1] < JapanPostEncoder.Hyphen)
            {
                // CC1 to CC3 and a digit: the letter A plus ten times the control code index plus the digit.
                characters[count++] = (char)('A' + ((code - JapanPostEncoder.ControlCode1) * 10) + codes[++c]);
            }
            else
            {
                return false;
            }
        }

        if (count == 0 || characters[..count].ContainsAnyExceptInRange('-', 'Z'))
        {
            return false;
        }

        text = characters[..count].ToString();
        return true;
    }

    /// <summary>
    /// Inverts <see cref="JapanPostEncoder.Patterns"/>.
    /// </summary>
    /// <returns>The code value of every pattern.</returns>
    private static sbyte[] CreateCodes()
    {
        sbyte[] codes = new sbyte[64];
        codes.AsSpan().Fill(-1);
        ReadOnlySpan<byte> patterns = JapanPostEncoder.Patterns;
        int bars = JapanPostEncoder.BarsPerCode;
        for (int v = 0; v < patterns.Length / bars; v++)
        {
            int pattern = 0;
            for (int b = 0; b < bars; b++)
            {
                pattern = (pattern << 2) | patterns[(v * bars) + b];
            }

            codes[pattern] = (sbyte)v;
        }

        return codes;
    }
}
