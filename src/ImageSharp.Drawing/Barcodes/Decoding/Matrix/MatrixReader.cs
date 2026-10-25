// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Reads one or more two-dimensional matrix families. A matrix reader finds the finder pattern of its symbology in the binarized image, maps the module grid
/// onto the image and samples it, then corrects and decodes the modules.
/// </summary>
internal abstract class MatrixReader : IFamilyReader
{
    /// <summary>
    /// Gets the families the reader returns.
    /// </summary>
    public abstract BarcodeFamily Families { get; }

    /// <summary>
    /// Reads the symbols of the image.
    /// </summary>
    /// <param name="context">The decode, whose binarized image the reader reads.</param>
    public abstract void Read(DecodeContext context);
}
