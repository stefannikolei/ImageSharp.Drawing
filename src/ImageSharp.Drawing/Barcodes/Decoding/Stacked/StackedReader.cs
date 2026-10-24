// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Reads one or more stacked families. A stacked symbol is rows of symbol characters, so a reader scans the rows of the image itself and joins what each
/// scan line reads into the rows of a symbol.
/// </summary>
internal abstract class StackedReader : IFamilyReader
{
    /// <summary>
    /// Gets the families the reader returns.
    /// </summary>
    public abstract BarcodeFamily Families { get; }

    /// <summary>
    /// Reads the symbols of one orientation of the image.
    /// </summary>
    /// <param name="context">The decode.</param>
    /// <param name="bits">The image in this orientation.</param>
    /// <param name="turned">Whether the image is the quarter turn of the source.</param>
    public abstract void Read(DecodeContext context, BitImage bits, bool turned);

    /// <summary>
    /// Maps a point of a scanned orientation to the source image. The quarter turn takes the source pixel edge (x, y) to (h - y, x), where h is the height of
    /// the source, the width of the turned image.
    /// </summary>
    /// <param name="x">The column in the scanned orientation.</param>
    /// <param name="y">The row in the scanned orientation.</param>
    /// <param name="bits">The image in this orientation.</param>
    /// <param name="turned">Whether the image is the quarter turn of the source.</param>
    /// <returns>The point in the source image.</returns>
    protected static PointF Map(float x, float y, BitImage bits, bool turned)
        => turned ? new PointF(y, bits.Width - x) : new PointF(x, y);
}
