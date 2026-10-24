// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Reads a DAFT symbol: the state of every bar as its letter, D for a descender, A for an ascender, F for a full bar and T for a tracker. Any row of bars
/// reads, so the family is not one of the defaults.
/// </summary>
internal sealed class DaftReader : HeightModulatedReader
{
    /// <inheritdoc/>
    public override BarcodeFamily Families => BarcodeFamily.Daft;

    /// <inheritdoc/>
    public override bool Directional => true;

    /// <inheritdoc/>
    public override bool TryRead(ReadOnlySpan<FourState> states, DecodeContext context, out BarcodeFamily family, out string text, out string identifier)
    {
        family = BarcodeFamily.Daft;
        identifier = "]X0";
        Span<char> letters = stackalloc char[states.Length];
        for (int i = 0; i < states.Length; i++)
        {
            letters[i] = states[i] switch
            {
                FourState.Descender => 'D',
                FourState.Ascender => 'A',
                FourState.Full => 'F',
                _ => 'T',
            };
        }

        text = letters.ToString();
        return true;
    }
}
