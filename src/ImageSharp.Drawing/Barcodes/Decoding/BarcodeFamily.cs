// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The barcode families that <see cref="BarcodeDecoder"/> reads. A family is one symbology as a scanner transmits it. A symbology that only applies rules to
/// the data of another symbology, such as <see cref="IsbnSymbology"/> on EAN-13 or <see cref="HibcCode128Symbology"/> on Code 128, decodes as the family it
/// draws with. The values are flags, so the options of the decoder can name several families. <see cref="FlattermarkenSymbology"/> has no family: its marks
/// have no start, stop or quiet zone, so a mark tells its digit only from the edge of the sheet, which an image does not show.
/// </summary>
[Flags]
public enum BarcodeFamily : ulong
{
    /// <summary>
    /// No family.
    /// </summary>
    None = 0,

    /// <summary>
    /// EAN-13, ISO/IEC 15420, with the ISBN, ISMN and ISSN symbols that draw as EAN-13.
    /// </summary>
    Ean13 = 1UL << 0,

    /// <summary>
    /// EAN-8, ISO/IEC 15420.
    /// </summary>
    Ean8 = 1UL << 1,

    /// <summary>
    /// UPC-A, ISO/IEC 15420. A UPC-A symbol is an EAN-13 symbol with a leading zero, and it decodes to those thirteen digits.
    /// </summary>
    UpcA = 1UL << 2,

    /// <summary>
    /// UPC-E, ISO/IEC 15420. It decodes to the thirteen digits of the UPC-A number it stands for.
    /// </summary>
    UpcE = 1UL << 3,

    /// <summary>
    /// The two and five digit EAN/UPC add-on symbols on their own.
    /// </summary>
    EanAddOn = 1UL << 4,

    /// <summary>
    /// Code 128, ISO/IEC 15417, with GS1-128 and the symbols that draw as Code 128.
    /// </summary>
    Code128 = 1UL << 5,

    /// <summary>
    /// Code 39, ISO/IEC 16388, read in its standard transmission mode. <see cref="BarcodeDecoderOptions.Code39FullAscii"/> selects the full ASCII mode.
    /// </summary>
    Code39 = 1UL << 6,

    /// <summary>
    /// Code 32, the Italian pharmacode: a Code 39 symbol of six base 32 characters. It decodes to its nine digits.
    /// </summary>
    Code32 = 1UL << 7,

    /// <summary>
    /// PZN, the German pharmacy number: a Code 39 symbol of a hyphen and seven or eight digits whose last digit checks the others.
    /// </summary>
    Pzn = 1UL << 8,

    /// <summary>
    /// Code 93, with its full ASCII extension.
    /// </summary>
    Code93 = 1UL << 9,

    /// <summary>
    /// Codabar, ISO/IEC 15424 identifier F.
    /// </summary>
    Codabar = 1UL << 10,

    /// <summary>
    /// Interleaved 2 of 5, ISO/IEC 16390, with ITF-14, Leitcode and Identcode.
    /// </summary>
    Interleaved2Of5 = 1UL << 11,

    /// <summary>
    /// Industrial 2 of 5, also called Standard 2 of 5.
    /// </summary>
    Industrial2Of5 = 1UL << 12,

    /// <summary>
    /// IATA 2 of 5.
    /// </summary>
    Iata2Of5 = 1UL << 13,

    /// <summary>
    /// Matrix 2 of 5.
    /// </summary>
    Matrix2Of5 = 1UL << 14,

    /// <summary>
    /// Datalogic 2 of 5, also called China Post.
    /// </summary>
    Datalogic2Of5 = 1UL << 15,

    /// <summary>
    /// COOP 2 of 5.
    /// </summary>
    Coop2Of5 = 1UL << 16,

    /// <summary>
    /// Code 11.
    /// </summary>
    Code11 = 1UL << 17,

    /// <summary>
    /// MSI, also called Modified Plessey.
    /// </summary>
    Msi = 1UL << 18,

    /// <summary>
    /// Plessey.
    /// </summary>
    Plessey = 1UL << 19,

    /// <summary>
    /// Telepen, read in its ASCII mode. <see cref="BarcodeDecoderOptions.TelepenNumeric"/> selects the numeric mode.
    /// </summary>
    Telepen = 1UL << 20,

    /// <summary>
    /// The one-track Pharmacode.
    /// </summary>
    Pharmacode = 1UL << 21,

    /// <summary>
    /// The two-track Pharmacode.
    /// </summary>
    TwoTrackPharmacode = 1UL << 22,

    /// <summary>
    /// Channel Code, ANSI/AIM BC12.
    /// </summary>
    ChannelCode = 1UL << 23,

    /// <summary>
    /// GS1 DataBar Omnidirectional, Truncated, Stacked and Stacked Omnidirectional, ISO/IEC 24724.
    /// </summary>
    DataBar = 1UL << 24,

    /// <summary>
    /// GS1 DataBar Limited, ISO/IEC 24724.
    /// </summary>
    DataBarLimited = 1UL << 25,

    /// <summary>
    /// GS1 DataBar Expanded and Expanded Stacked, ISO/IEC 24724.
    /// </summary>
    DataBarExpanded = 1UL << 26,

    /// <summary>
    /// BC412, SEMI T1-95.
    /// </summary>
    Bc412 = 1UL << 27,

    /// <summary>
    /// PosiCode, in its A, B and limited forms.
    /// </summary>
    PosiCode = 1UL << 28,

    /// <summary>
    /// The Facing Identification Marks A to E of the USPS.
    /// </summary>
    FacingIdentificationMark = 1UL << 30,

    /// <summary>
    /// POSTNET of the USPS.
    /// </summary>
    Postnet = 1UL << 31,

    /// <summary>
    /// PLANET of the USPS.
    /// </summary>
    Planet = 1UL << 32,

    /// <summary>
    /// The Intelligent Mail barcode of the USPS, USPS-B-3200.
    /// </summary>
    IntelligentMail = 1UL << 33,

    /// <summary>
    /// The Australia Post customer barcodes.
    /// </summary>
    AustraliaPost = 1UL << 34,

    /// <summary>
    /// The Royal Mail 4-State Customer Code, RM4SCC.
    /// </summary>
    RoyalMail = 1UL << 35,

    /// <summary>
    /// KIX, the Dutch PostNL code.
    /// </summary>
    Kix = 1UL << 36,

    /// <summary>
    /// The Japan Post customer barcode.
    /// </summary>
    JapanPost = 1UL << 37,

    /// <summary>
    /// The 4-state Royal Mail Mailmark barcode.
    /// </summary>
    Mailmark = 1UL << 38,

    /// <summary>
    /// DAFT, a 4-state symbol of descender, ascender, full and tracker bars that carries no other rules.
    /// </summary>
    Daft = 1UL << 39,

    /// <summary>
    /// PDF417, ISO/IEC 15438, with Compact PDF417.
    /// </summary>
    Pdf417 = 1UL << 40,

    /// <summary>
    /// MicroPDF417, ISO/IEC 24728.
    /// </summary>
    MicroPdf417 = 1UL << 41,

    /// <summary>
    /// Code 16K.
    /// </summary>
    Code16K = 1UL << 42,

    /// <summary>
    /// Code 49.
    /// </summary>
    Code49 = 1UL << 43,

    /// <summary>
    /// Codablock F.
    /// </summary>
    CodablockF = 1UL << 44,

    /// <summary>
    /// QR Code, ISO/IEC 18004.
    /// </summary>
    QrCode = 1UL << 45,

    /// <summary>
    /// Micro QR Code, ISO/IEC 18004.
    /// </summary>
    MicroQrCode = 1UL << 46,

    /// <summary>
    /// Rectangular Micro QR Code, ISO/IEC 23941.
    /// </summary>
    RectangularMicroQrCode = 1UL << 47,

    /// <summary>
    /// Data Matrix ECC 200, ISO/IEC 16022.
    /// </summary>
    DataMatrix = 1UL << 48,

    /// <summary>
    /// Aztec Code, ISO/IEC 24778.
    /// </summary>
    Aztec = 1UL << 49,

    /// <summary>
    /// Aztec Runes, ISO/IEC 24778 Annex A.
    /// </summary>
    AztecRune = 1UL << 50,

    /// <summary>
    /// MaxiCode, ISO/IEC 16023.
    /// </summary>
    MaxiCode = 1UL << 51,

    /// <summary>
    /// Han Xin Code, ISO/IEC 20830.
    /// </summary>
    HanXin = 1UL << 52,

    /// <summary>
    /// Code One.
    /// </summary>
    CodeOne = 1UL << 53,

    /// <summary>
    /// DotCode, AIM ISS DotCode.
    /// </summary>
    DotCode = 1UL << 54,

    /// <summary>
    /// Ultracode, AIM ISS Ultracode.
    /// </summary>
    Ultracode = 1UL << 55,

    /// <summary>
    /// A GS1 Composite 2D component, CC-A, CC-B or CC-C, ISO/IEC 24723, read on its own.
    /// </summary>
    Gs1CompositeComponent = 1UL << 56,

    /// <summary>
    /// The linear families.
    /// </summary>
    Linear = Ean13 | Ean8 | UpcA | UpcE | EanAddOn | Code128 | Code39 | Code32 | Pzn | Code93 | Codabar | Interleaved2Of5 | Industrial2Of5 | Iata2Of5
        | Matrix2Of5 | Datalogic2Of5 | Coop2Of5 | Code11 | Msi | Plessey | Telepen | Pharmacode | ChannelCode | DataBar | DataBarLimited | DataBarExpanded
        | Bc412 | PosiCode | FacingIdentificationMark,

    /// <summary>
    /// The height modulated families: the postal codes and the two-track Pharmacode.
    /// </summary>
    HeightModulated = TwoTrackPharmacode | Postnet | Planet | IntelligentMail | AustraliaPost | RoyalMail | Kix | JapanPost | Mailmark | Daft,

    /// <summary>
    /// The stacked families.
    /// </summary>
    Stacked = Pdf417 | MicroPdf417 | Code16K | Code49 | CodablockF | Gs1CompositeComponent,

    /// <summary>
    /// The two-dimensional matrix families.
    /// </summary>
    Matrix = QrCode | MicroQrCode | RectangularMicroQrCode | DataMatrix | Aztec | AztecRune | MaxiCode | HanXin | CodeOne | DotCode | Ultracode,

    /// <summary>
    /// Every family.
    /// </summary>
    All = Linear | HeightModulated | Stacked | Matrix,
}
