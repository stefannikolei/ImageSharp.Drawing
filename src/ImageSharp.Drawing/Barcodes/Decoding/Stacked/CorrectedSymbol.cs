// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// A PDF417 or MicroPDF417 symbol whose codewords the Reed-Solomon code corrected: its data codewords and its corners in the image.
/// </summary>
internal sealed class CorrectedSymbol
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CorrectedSymbol"/> class.
    /// </summary>
    /// <param name="codewords">The data codewords, without the Symbol Length Descriptor of PDF417.</param>
    /// <param name="corners">The four corners in pixels of the image, from the top left corner of the symbol as it reads.</param>
    public CorrectedSymbol(int[] codewords, PointF[] corners)
    {
        this.Codewords = codewords;
        this.Corners = corners;
    }

    /// <summary>
    /// Gets the data codewords.
    /// </summary>
    public int[] Codewords { get; }

    /// <summary>
    /// Gets the four corners in pixels of the image.
    /// </summary>
    public PointF[] Corners { get; }

    /// <summary>
    /// Gets a value indicating whether the symbol is the CC-B or CC-C 2D component of a GS1 Composite symbol, whose data starts with the codeword 920, clauses
    /// 9 a) and 10 a) of ISO/IEC 24723.
    /// </summary>
    public bool IsComposite => this.Codewords.Length > 0 && this.Codewords[0] == Pdf417Text.CompositeCodeword;
}
