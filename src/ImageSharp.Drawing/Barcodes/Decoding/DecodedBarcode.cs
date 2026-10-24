// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// A symbol that <see cref="BarcodeDecoder"/> found in an image and the data it carries.
/// </summary>
public sealed class DecodedBarcode
{
    private readonly PointF[] corners;

    /// <summary>
    /// Initializes a new instance of the <see cref="DecodedBarcode"/> class.
    /// </summary>
    /// <param name="family">The family of the symbol.</param>
    /// <param name="text">The transmitted data.</param>
    /// <param name="symbologyIdentifier">The symbology identifier.</param>
    /// <param name="corners">The four corners of the symbol in pixels.</param>
    internal DecodedBarcode(BarcodeFamily family, string text, string symbologyIdentifier, PointF[] corners)
    {
        this.Family = family;
        this.Text = text;
        this.SymbologyIdentifier = symbologyIdentifier;
        this.corners = corners;
    }

    /// <summary>
    /// Gets the family the symbol reads as, one value of <see cref="BarcodeFamily"/>.
    /// </summary>
    public BarcodeFamily Family { get; }

    /// <summary>
    /// Gets the data the symbol transmits, as a scanner sends it in its standard transmission mode. Start, stop and check characters are not part of the
    /// data unless the symbology transmits them. A leading Function 1 Symbol Character marks GS1 data. It is not part of the text: the symbology identifier
    /// reports it, and every later Function 1 Symbol Character is the group separator U+001D. A byte the symbol carries without a character set reads as the
    /// character of ISO/IEC 8859-1 with that value, unless the bytes form UTF-8.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Gets the symbology identifier of ISO/IEC 15424, such as <c>]C1</c> for GS1-128 or <c>]Q1</c> for QR Code: a closing bracket, the code character of
    /// the symbology and the modifier character that tells the options the symbol uses.
    /// </summary>
    public string SymbologyIdentifier { get; }

    /// <summary>
    /// Gets the corners of the symbol in pixels of the image: the top left, the top right, the bottom right and the bottom left corner as the symbol reads.
    /// A linear symbol gives the ends of the rows the decoder read it on.
    /// </summary>
    public ReadOnlySpan<PointF> Corners => this.corners;

    /// <inheritdoc/>
    public override string ToString() => $"{this.Family}: {this.Text}";
}
