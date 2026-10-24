// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Reads the Intelligent Mail barcode, USPS-B-3200, the inverse of <see cref="IntelligentMailEncoder"/>: the 65 bars give the bits of ten 13-bit characters
/// by Table 22, a character with eight or eleven bits set is the negation of one of Table 19 or Table 20 and carries a bit of the frame check sequence, the
/// characters give the codewords, the codewords the binary value, and the frame check sequence of the value must be the one the characters carry. The value
/// gives the 20-digit tracking code and the routing code.
/// </summary>
internal sealed class IntelligentMailReader : HeightModulatedReader
{
    private const int BarCount = 65;
    private const int CharacterCount = 10;
    private const int TrackingLength = 20;

    /// <summary>
    /// The codeword of every 13-bit character with five or two bits set: its index in Table 19, or 1287 plus its index in Table 20, or -1.
    /// </summary>
    private static readonly short[] Codewords = CreateCodewords();

    /// <inheritdoc/>
    public override BarcodeFamily Families => BarcodeFamily.IntelligentMail;

    /// <inheritdoc/>
    public override bool TryRead(ReadOnlySpan<FourState> states, DecodeContext context, out BarcodeFamily family, out string text, out string identifier)
    {
        family = BarcodeFamily.IntelligentMail;
        text = string.Empty;
        identifier = "]X0";
        if (states.Length != BarCount)
        {
            return false;
        }

        // Table 22: each bar takes its descender from one character bit and its ascender from another.
        Span<int> characters = stackalloc int[CharacterCount];
        characters.Clear();
        ReadOnlySpan<byte> map = IntelligentMailEncoder.BarMap;
        for (int bar = 0; bar < BarCount; bar++)
        {
            int entry = bar * 4;
            if ((states[bar] & FourState.Descender) != 0)
            {
                characters[map[entry]] |= 1 << map[entry + 1];
            }

            if ((states[bar] & FourState.Ascender) != 0)
            {
                characters[map[entry + 2]] |= 1 << map[entry + 3];
            }
        }

        // A character with more than half its bits set is negated, and the negation is bit i of the frame check sequence.
        Span<int> codewords = stackalloc int[CharacterCount];
        int fcs = 0;
        for (int i = 0; i < CharacterCount; i++)
        {
            int character = characters[i];
            if (BitOperations.PopCount((uint)character) > 6)
            {
                character ^= IntelligentMailEncoder.CharacterMask;
                fcs |= 1 << i;
            }

            codewords[i] = Codewords[character];
            if (codewords[i] < 0)
            {
                return false;
            }
        }

        // Codeword J is doubled, and codeword A carries the most significant bit of the frame check sequence.
        if ((codewords[9] & 1) != 0)
        {
            return false;
        }

        if (codewords[0] >= IntelligentMailEncoder.CodewordAOffset)
        {
            codewords[0] -= IntelligentMailEncoder.CodewordAOffset;
            fcs |= IntelligentMailEncoder.FrameCheckTopBit;
        }

        UInt128 binary = (uint)codewords[0];
        for (int i = 1; i <= 8; i++)
        {
            binary = (binary * IntelligentMailEncoder.CodewordRadix) + (uint)codewords[i];
        }

        binary = (binary * IntelligentMailEncoder.CodewordJRadix) + (uint)(codewords[9] / 2);
        if (IntelligentMailEncoder.FrameCheckSequence(binary) != fcs)
        {
            return false;
        }

        // The inverse of step 1: the last 18 tracking digits, the second tracking digit modulo 5, the first, and the routing code with the offset of its
        // length.
        Span<char> digits = stackalloc char[TrackingLength + 11];
        for (int i = TrackingLength - 1; i >= 2; i--)
        {
            digits[i] = (char)('0' + (int)(binary % 10U));
            binary /= 10U;
        }

        digits[1] = (char)('0' + (int)(binary % 5U));
        binary /= 5U;
        digits[0] = (char)('0' + (int)(binary % 10U));
        binary /= 10U;

        ulong routing = (ulong)binary;
        int routingLength;
        if (routing == 0)
        {
            routingLength = 0;
        }
        else if (routing <= 100000)
        {
            routingLength = 5;
            routing -= 1;
        }
        else if (routing <= 1000100000)
        {
            routingLength = 9;
            routing -= 100001;
        }
        else if (routing <= 101000100000)
        {
            routingLength = 11;
            routing -= 1000100001;
        }
        else
        {
            return false;
        }

        for (int i = TrackingLength + routingLength - 1; i >= TrackingLength; i--)
        {
            digits[i] = (char)('0' + (int)(routing % 10));
            routing /= 10;
        }

        text = digits[..(TrackingLength + routingLength)].ToString();
        return true;
    }

    /// <summary>
    /// Inverts Table 19 and Table 20.
    /// </summary>
    /// <returns>The codeword of every character.</returns>
    private static short[] CreateCodewords()
    {
        short[] codewords = new short[IntelligentMailEncoder.CharacterMask + 1];
        codewords.AsSpan().Fill(-1);
        ReadOnlySpan<ushort> five = IntelligentMailEncoder.FiveOf13;
        for (int i = 0; i < five.Length; i++)
        {
            codewords[five[i]] = (short)i;
        }

        ReadOnlySpan<ushort> two = IntelligentMailEncoder.TwoOf13;
        for (int i = 0; i < two.Length; i++)
        {
            codewords[two[i]] = (short)(IntelligentMailEncoder.FiveOf13Length + i);
        }

        return codewords;
    }
}
