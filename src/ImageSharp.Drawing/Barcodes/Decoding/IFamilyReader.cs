// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// A reader of one or more barcode families, which the decoder uses only when the options name one of them.
/// </summary>
internal interface IFamilyReader
{
    /// <summary>
    /// Gets the families the reader returns.
    /// </summary>
    BarcodeFamily Families { get; }
}
