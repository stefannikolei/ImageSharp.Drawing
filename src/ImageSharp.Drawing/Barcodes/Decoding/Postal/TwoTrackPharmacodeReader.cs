// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Globalization;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Reads the two-track Pharmacode of the Laetus Pharmacode Guide: two to sixteen lower half, upper half and full bars, read from the right. A bar in position
/// n weighs 3 to the power n - 1 times 1 for a lower bar, 2 for an upper bar and 3 for a full bar. Any such bars read as a number, so the reader reads the
/// upright image only and the family is not one of the defaults.
/// </summary>
internal sealed class TwoTrackPharmacodeReader : HeightModulatedReader
{
    /// <inheritdoc/>
    public override BarcodeFamily Families => BarcodeFamily.TwoTrackPharmacode;

    /// <inheritdoc/>
    public override bool Directional => true;

    /// <inheritdoc/>
    public override bool SharedBand => false;

    /// <inheritdoc/>
    public override bool TryRead(ReadOnlySpan<FourState> states, DecodeContext context, out BarcodeFamily family, out string text, out string identifier)
    {
        family = BarcodeFamily.TwoTrackPharmacode;
        text = string.Empty;
        identifier = "]X0";
        if (states.Length is < 2 or > 16)
        {
            return false;
        }

        long value = 0;
        long weight = 1;
        for (int i = states.Length - 1; i >= 0; i--)
        {
            // A lower half bar reaches the bottom of the row alone, and an upper half bar the top alone.
            int kind = states[i] switch
            {
                FourState.Descender => 1,
                FourState.Ascender => 2,
                FourState.Full => 3,
                _ => 0,
            };

            if (kind == 0)
            {
                return false;
            }

            value += kind * weight;
            weight *= 3;
        }

        if (value < TwoTrackPharmacodeSymbology.MinimumValue || value > TwoTrackPharmacodeSymbology.MaximumValue)
        {
            return false;
        }

        text = value.ToString(CultureInfo.InvariantCulture);
        return true;
    }
}
