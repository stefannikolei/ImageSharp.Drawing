// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Reads one or more height modulated families from the states of a row of bars. The states read from the left: a bar that reaches the top of the row has an
/// ascender, and a bar that reaches the bottom has a descender, so a full bar has both and a tracker neither.
/// </summary>
internal abstract class HeightModulatedReader : IFamilyReader
{
    /// <summary>
    /// Gets the families the reader returns.
    /// </summary>
    public abstract BarcodeFamily Families { get; }

    /// <summary>
    /// Gets a value indicating whether the symbology reads in one direction only. The scanner then gives it the states of an upright row alone, not those of
    /// the row turned upside down.
    /// </summary>
    public virtual bool Directional => false;

    /// <summary>
    /// Reads a symbol from the states of a row of bars.
    /// </summary>
    /// <param name="states">The bar states from the left.</param>
    /// <param name="context">The decode.</param>
    /// <param name="family">The family read.</param>
    /// <param name="text">The transmitted data.</param>
    /// <param name="identifier">The symbology identifier.</param>
    /// <returns><see langword="true"/> when the states are a symbol of the reader's families.</returns>
    public abstract bool TryRead(ReadOnlySpan<FourState> states, DecodeContext context, out BarcodeFamily family, out string text, out string identifier);
}
