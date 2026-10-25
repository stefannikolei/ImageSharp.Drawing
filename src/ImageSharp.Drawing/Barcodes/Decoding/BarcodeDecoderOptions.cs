// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Options that control what <see cref="BarcodeDecoder"/> looks for and how hard it looks.
/// </summary>
public sealed class BarcodeDecoderOptions
{
    /// <summary>
    /// The families the decoder reads by default: every family except those whose symbols carry no check of their own. Any sequence of bars reads as a
    /// Pharmacode, a two-track Pharmacode, Flattermarken or a DAFT symbol, and a Facing Identification Mark is a few bars, so these families read only when
    /// the options name them. An EAN/UPC add-on on its own is a guard and two or five digits that only their number sets check, which parts of other symbols
    /// and the separator patterns of GS1 Composite symbols match, so it also reads only when the options name it.
    /// </summary>
    public const BarcodeFamily DefaultFamilies = BarcodeFamily.All
        & ~(BarcodeFamily.Pharmacode | BarcodeFamily.TwoTrackPharmacode | BarcodeFamily.Flattermarken | BarcodeFamily.FacingIdentificationMark
            | BarcodeFamily.Daft | BarcodeFamily.EanAddOn);

    /// <summary>
    /// Gets or sets the families to read. The default is <see cref="DefaultFamilies"/>. Naming fewer families makes the decoder faster and stops a symbol
    /// from reading as a family it is not.
    /// </summary>
    public BarcodeFamily Families { get; set; } = DefaultFamilies;

    /// <summary>
    /// Gets or sets a value indicating whether the decoder spends more time to find a symbol. It then scans more rows of the image, scans the columns for
    /// symbols that stand on their side, and binarizes the image a second way when the first finds nothing. The default is <see langword="true"/>.
    /// </summary>
    public bool TryHarder { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether a Code 39 symbol reads in the full ASCII mode of Annex A.3 of ISO/IEC 16388, in which a pair such as <c>+A</c>
    /// stands for one lowercase letter. The standard mode reads the characters the symbol carries. The default is <see langword="false"/>.
    /// </summary>
    public bool Code39FullAscii { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a Telepen symbol reads in its numeric mode, in which each character stands for two digits, or for a digit and
    /// X. A Telepen symbol does not mark the mode it carries, so the options name it. The default is <see langword="false"/>, the ASCII mode.
    /// </summary>
    public bool TelepenNumeric { get; set; }

    /// <summary>
    /// Gets or sets the encoding table of the customer information field of an Australia Post barcode. The barcode does not mark the table it uses, so the
    /// options name it. The default is <see cref="AustraliaPostEncodingTable.Character"/>, the table <see cref="AustraliaPostSymbology"/> encodes with by
    /// default.
    /// </summary>
    public AustraliaPostEncodingTable AustraliaPostCustomerTable { get; set; } = AustraliaPostEncodingTable.Character;
}
