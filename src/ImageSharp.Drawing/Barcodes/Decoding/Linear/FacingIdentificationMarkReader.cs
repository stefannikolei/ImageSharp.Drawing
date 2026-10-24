// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Reads the Facing Identification Marks A to E of the USPS: nine positions two modules apart, each a bar of one module or a space, in the patterns of
/// <see cref="FacingIdentificationMarkSymbology.Patterns"/>. Every pattern starts and ends with a bar and reads the same from either end. A mark is a few
/// bars, so the family is not one of the defaults.
/// </summary>
internal sealed class FacingIdentificationMarkReader : LinearReader
{
    /// <summary>
    /// The number of positions of a pattern.
    /// </summary>
    private const int Positions = 9;

    /// <inheritdoc/>
    public override BarcodeFamily Families => BarcodeFamily.FacingIdentificationMark;

    /// <inheritdoc/>
    public override bool TryRead(ReadOnlySpan<int> runs, int start, DecodeContext context, out LinearMatch match)
    {
        ReadOnlySpan<ushort> patterns = FacingIdentificationMarkSymbology.Patterns;
        for (int i = start; i + 1 < runs.Length; i += 2)
        {
            float module = runs[i];
            if (!IsQuiet(runs, i - 1, module * 2))
            {
                continue;
            }

            // The positions of the bars from the first one: a space of 2k - 1 modules after a bar puts the next bar k positions on. The last bar stands at
            // the last position.
            int pattern = 1 << (Positions - 1);
            int bit = Positions - 1;
            int position = i;
            bool valid = IsModule(runs[i], module);
            while (valid && bit > 0 && position + 2 < runs.Length)
            {
                float steps = (runs[position + 1] + module) / (module * 2);
                int step = (int)MathF.Round(steps);
                if (MathF.Abs(steps - step) > 0.3F || step < 1 || step > bit)
                {
                    valid = false;
                    break;
                }

                bit -= step;
                pattern |= 1 << bit;
                position += 2;
                valid = IsModule(runs[position], module);
            }

            int index = valid && bit == 0 && IsQuiet(runs, position + 1, module * 2) ? patterns.IndexOf((ushort)pattern) : -1;
            if (index >= 0)
            {
                match = new LinearMatch(BarcodeFamily.FacingIdentificationMark, ((char)('A' + index)).ToString(), "]X0", i, position + 1, false);
                return true;
            }
        }

        match = default;
        return false;
    }

    /// <summary>
    /// Returns a value indicating whether a bar is one module wide.
    /// </summary>
    /// <param name="run">The bar.</param>
    /// <param name="module">The module width.</param>
    /// <returns><see langword="true"/> for a bar of one module.</returns>
    private static bool IsModule(int run, float module) => run >= module * 0.5F && run <= module * 1.5F;
}
