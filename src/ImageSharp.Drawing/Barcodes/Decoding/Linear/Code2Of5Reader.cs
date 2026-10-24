// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Reads one of the plain 2 of 5 symbologies of <see cref="Code2Of5Encoder"/>: Industrial and IATA, whose digits are five bars with a narrow space after each,
/// and Matrix, COOP and Datalogic, whose digits are three bars and two spaces with a narrow space after them. Two of the five elements of each digit are wide.
/// The symbologies define no check digit, so the reader transmits every digit.
/// </summary>
internal sealed class Code2Of5Reader : LinearReader
{
    private const float MaximumVariance = 0.3F;
    private const float MaximumIndividualVariance = 0.8F;

    private readonly BarcodeFamily family;
    private readonly string identifier;
    private readonly sbyte[] digits;
    private readonly byte[] start;
    private readonly byte[] stop;
    private readonly bool barsOnly;

    /// <summary>
    /// Initializes a new instance of the <see cref="Code2Of5Reader"/> class.
    /// </summary>
    /// <param name="variant">The symbology.</param>
    public Code2Of5Reader(Code2Of5Variant variant)
    {
        (this.family, this.identifier) = variant switch
        {
            Code2Of5Variant.Industrial => (BarcodeFamily.Industrial2Of5, "]S0"),
            Code2Of5Variant.Iata => (BarcodeFamily.Iata2Of5, "]R0"),
            Code2Of5Variant.Matrix => (BarcodeFamily.Matrix2Of5, "]X0"),
            Code2Of5Variant.Coop => (BarcodeFamily.Coop2Of5, "]X0"),
            _ => (BarcodeFamily.Datalogic2Of5, "]X0"),
        };

        this.digits = Interleaved2Of5Reader.CreateDigits(variant == Code2Of5Variant.Coop ? Code2Of5Encoder.CoopPatterns : Code2Of5Encoder.Patterns);
        this.start = Code2Of5Encoder.StartPattern(variant).ToArray();
        this.stop = Code2Of5Encoder.StopPattern(variant).ToArray();
        this.barsOnly = variant is Code2Of5Variant.Industrial or Code2Of5Variant.Iata;
    }

    /// <inheritdoc/>
    public override BarcodeFamily Families => this.family;

    /// <inheritdoc/>
    public override bool TryRead(ReadOnlySpan<int> runs, int start, DecodeContext context, out LinearMatch match)
    {
        int startLength = this.start.Length;
        int stopLength = this.stop.Length;
        int digitLength = this.barsOnly ? 10 : 6;
        Span<char> text = stackalloc char[(runs.Length / digitLength) + 1];
        Span<int> elements = stackalloc int[5];

        // Every start pattern ends on a space and every stop pattern starts with a bar, so the digits fill the runs between them whole.
        for (int i = start; i + startLength < runs.Length; i += 2)
        {
            if (Variance(runs.Slice(i, startLength), this.start, MaximumIndividualVariance) > MaximumVariance)
            {
                continue;
            }

            float narrow = Narrow(runs.Slice(i, startLength), this.start);
            if (!IsQuiet(runs, i - 1, narrow * 6))
            {
                continue;
            }

            int count = 0;
            int position = i + startLength;
            bool stopped = false;
            while (position + stopLength <= runs.Length && count < text.Length)
            {
                if (Variance(runs.Slice(position, stopLength), this.stop, MaximumIndividualVariance) <= MaximumVariance
                    && IsQuiet(runs, position + stopLength, narrow * 6))
                {
                    stopped = true;
                    position += stopLength;
                    break;
                }

                if (position + digitLength > runs.Length)
                {
                    break;
                }

                int digit = this.ReadDigit(runs.Slice(position, digitLength), narrow, elements);
                if (digit < 0)
                {
                    break;
                }

                text[count++] = (char)('0' + digit);
                position += digitLength;
            }

            if (stopped && count >= 1)
            {
                match = new LinearMatch(this.family, text[..count].ToString(), this.identifier, i, position, false);
                return true;
            }
        }

        match = default;
        return false;
    }

    /// <summary>
    /// Returns the narrow width that a run pattern gives: the total width over the total modules.
    /// </summary>
    /// <param name="runs">The runs.</param>
    /// <param name="pattern">The module widths.</param>
    /// <returns>The width of one module in pixels.</returns>
    private static float Narrow(ReadOnlySpan<int> runs, ReadOnlySpan<byte> pattern)
    {
        int modules = 0;
        foreach (byte width in pattern)
        {
            modules += width;
        }

        return Sum(runs, 0, runs.Length) / (float)modules;
    }

    /// <summary>
    /// Reads one digit. The separating spaces must be narrow.
    /// </summary>
    /// <param name="runs">The runs of the digit with its separating spaces.</param>
    /// <param name="narrow">The narrow width.</param>
    /// <param name="elements">Scratch for the five elements.</param>
    /// <returns>The digit, or -1.</returns>
    private int ReadDigit(ReadOnlySpan<int> runs, float narrow, Span<int> elements)
    {
        if (this.barsOnly)
        {
            for (int e = 0; e < 5; e++)
            {
                elements[e] = runs[e * 2];
                if (runs[(e * 2) + 1] > narrow * 1.6F)
                {
                    return -1;
                }
            }
        }
        else
        {
            runs[..5].CopyTo(elements);
            if (runs[5] > narrow * 1.6F)
            {
                return -1;
            }
        }

        int mask = WideElements(elements, 2);
        return mask < 0 ? -1 : this.digits[mask];
    }
}
