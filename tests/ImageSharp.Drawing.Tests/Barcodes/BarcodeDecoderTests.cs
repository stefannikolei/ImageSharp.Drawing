// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Round-trip tests of <see cref="BarcodeDecoder"/> for the symbologies the independent decoders of <see cref="BarcodeDecodeTests"/> do not read, and for the
/// behaviour of the decoder itself: rotated symbols and the default families. The expected data is what the specification of each symbology transmits.
/// </summary>
public class BarcodeDecoderTests
{
    /// <summary>
    /// A plain 2 of 5 symbol decodes to the digits it carries, the check digit included when the symbol carries one. The check digit of "123456" is 5: the
    /// weights 3 and 1 from the right give 45.
    /// </summary>
    /// <param name="kind">The symbology.</param>
    /// <param name="checkDigit">Whether the symbol carries the check digit.</param>
    /// <param name="expected">The digits the symbol carries.</param>
    [Theory]
    [InlineData("Industrial", CheckCharacterMode.None, "123456")]
    [InlineData("Industrial", CheckCharacterMode.Compute, "1234565")]
    [InlineData("Iata", CheckCharacterMode.None, "123456")]
    [InlineData("Iata", CheckCharacterMode.Compute, "1234565")]
    [InlineData("Matrix", CheckCharacterMode.None, "123456")]
    [InlineData("Matrix", CheckCharacterMode.Compute, "1234565")]
    [InlineData("Coop", CheckCharacterMode.None, "123456")]
    [InlineData("Coop", CheckCharacterMode.Compute, "1234565")]
    [InlineData("Datalogic", CheckCharacterMode.None, "123456")]
    [InlineData("Datalogic", CheckCharacterMode.Compute, "1234565")]
    public void Code2Of5_RoundTrips(string kind, CheckCharacterMode checkDigit, string expected)
    {
        (BarcodeSymbology symbology, BarcodeFamily family) = kind switch
        {
            "Industrial" => ((BarcodeSymbology)new Industrial2Of5Symbology(checkDigit), BarcodeFamily.Industrial2Of5),
            "Iata" => (new Iata2Of5Symbology(checkDigit), BarcodeFamily.Iata2Of5),
            "Matrix" => (new Matrix2Of5Symbology(checkDigit), BarcodeFamily.Matrix2Of5),
            "Coop" => (new Coop2Of5Symbology(checkDigit), BarcodeFamily.Coop2Of5),
            _ => (new Datalogic2Of5Symbology(checkDigit), BarcodeFamily.Datalogic2Of5),
        };

        AssertReads(symbology, "123456", family, null, expected);
    }

    /// <summary>
    /// A standalone add-on symbol decodes to its digits, with the symbology identifier ]E1 for two digits and ]E2 for five.
    /// </summary>
    [Fact]
    public void EanAddOns_RoundTrip()
    {
        AssertReads(new Ean2Symbology(), "12", BarcodeFamily.EanAddOn, "]E1", "12");
        AssertReads(new Ean5Symbology(), "52495", BarcodeFamily.EanAddOn, "]E2", "52495");
    }

    /// <summary>
    /// A Code 32 symbol decodes to the nine digits of the AIC code its six base 32 characters stand for.
    /// </summary>
    [Fact]
    public void Code32_RoundTrips()
        => AssertReads(new Code32Symbology(), "012345676", BarcodeFamily.Code32, "]A0", "012345676");

    /// <summary>
    /// A Code 11 symbol decodes to the characters between its start and stop character, the check characters included. A symbol of up to ten data
    /// characters carries the C check character, and a longer one the C and K check characters, which the symbology identifier reports as ]H0 and ]H1.
    /// </summary>
    /// <param name="checkCharacters">Whether the symbol carries check characters.</param>
    /// <param name="text">The text to encode.</param>
    /// <param name="identifier">The symbology identifier.</param>
    /// <param name="expected">The characters the symbol carries.</param>
    [Theory]
    [InlineData(CheckCharacterMode.None, "123-45", "]H0", "123-45")]
    [InlineData(CheckCharacterMode.Compute, "123-45", "]H0", "123-455")]
    [InlineData(CheckCharacterMode.Compute, "0123456789", "]H1", "012345678903")]
    public void Code11_RoundTrips(CheckCharacterMode checkCharacters, string text, string identifier, string expected)
        => AssertReads(new Code11Symbology(checkCharacters), text, BarcodeFamily.Code11, identifier, expected);

    /// <summary>
    /// A Plessey symbol decodes to its hexadecimal data. The two check characters of the cyclic redundancy check are symbol overhead.
    /// </summary>
    [Fact]
    public void Plessey_RoundTrips()
        => AssertReads(new PlesseySymbology(), "01234ABCDEF", BarcodeFamily.Plessey, "]P0", "01234ABCDEF");

    /// <summary>
    /// A Telepen Numeric symbol decodes in the numeric mode to its digit pairs, and to a digit and X for the last pair.
    /// </summary>
    [Fact]
    public void TelepenNumeric_RoundTripsWithX()
    {
        using Image<L8> image = DrawImage(new TelepenNumericSymbology(), "123X", new BarcodeOptions());

        DecodedBarcode result = Assert.Single(BarcodeDecoder.Decode(image, new BarcodeDecoderOptions { Families = BarcodeFamily.Telepen, TelepenNumeric = true }));
        Assert.Equal("123X", result.Text);
        Assert.Equal("]B1", result.SymbologyIdentifier);
    }

    /// <summary>
    /// A Channel Code symbol decodes to its value with as many digits as its channels less one, leading zeros included, with the full and the short finder
    /// pattern.
    /// </summary>
    /// <param name="shortFinder">Whether the symbol has the short finder pattern.</param>
    /// <param name="text">The digits.</param>
    [Theory]
    [InlineData(false, "12")]
    [InlineData(false, "007")]
    [InlineData(false, "1234")]
    [InlineData(false, "7742862")]
    [InlineData(true, "26")]
    [InlineData(true, "576688")]
    public void ChannelCode_RoundTrips(bool shortFinder, string text)
        => AssertReads(new ChannelCodeSymbology(shortFinder), text, BarcodeFamily.ChannelCode, null, text);

    /// <summary>
    /// A BC412 symbol decodes to its data. The check character the symbol carries after the first data character is symbol overhead.
    /// </summary>
    /// <param name="text">The data.</param>
    [Theory]
    [InlineData("AQ45670")]
    [InlineData("0R9GLVHA8EZ4NTS1J2")]
    public void Bc412_RoundTrips(string text)
        => AssertReads(new Bc412Symbology(), text, BarcodeFamily.Bc412, null, text);

    /// <summary>
    /// A PosiCode symbol decodes to its text in every version: the standard versions through their character sets, shifts and function 4 characters, and the
    /// limited versions through the limited set.
    /// </summary>
    /// <param name="version">The version.</param>
    /// <param name="text">The text.</param>
    [Theory]
    [InlineData(PosiCodeVersion.A, "PosiCode 1!")]
    [InlineData(PosiCodeVersion.B, "Hello, World")]
    [InlineData(PosiCodeVersion.A, "Größe ÄÖÜ")]
    [InlineData(PosiCodeVersion.B, "\u00C0\u00C9\u00CE\u00D5\u00DC\u00DD")]
    [InlineData(PosiCodeVersion.LimitedA, "ABC-123.")]
    [InlineData(PosiCodeVersion.LimitedB, "XYZ")]
    public void PosiCode_RoundTrips(PosiCodeVersion version, string text)
    {
        text = System.Text.RegularExpressions.Regex.Unescape(text);
        AssertReads(new PosiCodeSymbology(version), text, BarcodeFamily.PosiCode, null, text);
    }

    /// <summary>
    /// A Facing Identification Mark decodes to its pattern letter.
    /// </summary>
    /// <param name="text">The pattern letter.</param>
    [Theory]
    [InlineData("A")]
    [InlineData("B")]
    [InlineData("C")]
    [InlineData("D")]
    [InlineData("E")]
    public void FacingIdentificationMark_RoundTrips(string text)
        => AssertReads(new FacingIdentificationMarkSymbology(), text, BarcodeFamily.FacingIdentificationMark, null, text);

    /// <summary>
    /// A POSTNET or PLANET symbol decodes to its digits. The correction digit is symbol overhead.
    /// </summary>
    /// <param name="planet">Whether the symbol is PLANET.</param>
    /// <param name="text">The digits.</param>
    [Theory]
    [InlineData(false, "12345")]
    [InlineData(false, "123456789")]
    [InlineData(false, "12345678901")]
    [InlineData(true, "12345678901")]
    [InlineData(true, "1234567890123")]
    public void UspsPostal_RoundTrips(bool planet, string text)
        => AssertReads(
            planet ? new PlanetSymbology() : new PostnetSymbology(),
            text,
            planet ? BarcodeFamily.Planet : BarcodeFamily.Postnet,
            null,
            text);

    /// <summary>
    /// An Australia Post barcode decodes to its format control code, its sorting code and its customer information, in the table the options name. The
    /// parity symbols are symbol overhead.
    /// </summary>
    /// <param name="table">The encoding table of the customer information.</param>
    /// <param name="text">The text.</param>
    [Theory]
    [InlineData(AustraliaPostEncodingTable.Character, "1139549554")]
    [InlineData(AustraliaPostEncodingTable.Character, "5954516251AB1")]
    [InlineData(AustraliaPostEncodingTable.Character, "6254516251ABC123 #xy")]
    [InlineData(AustraliaPostEncodingTable.Numeric, "591234567812345678")]
    [InlineData(AustraliaPostEncodingTable.Numeric, "62123456781234567890123")]
    [InlineData(AustraliaPostEncodingTable.Numeric, "62123456789999999999999")]
    public void AustraliaPost_RoundTrips(AustraliaPostEncodingTable table, string text)
    {
        using Image<L8> image = DrawImage(new AustraliaPostSymbology(table), text, new BarcodeOptions());

        BarcodeDecoderOptions options = new() { Families = BarcodeFamily.AustraliaPost, AustraliaPostCustomerTable = table };
        DecodedBarcode result = Assert.Single(BarcodeDecoder.Decode(image, options));
        Assert.Equal(text, result.Text);
    }

    /// <summary>
    /// A Royal Mail 4-State Customer Code decodes to its characters; its check character, start bar and stop bar are symbol overhead. KIX is the same
    /// characters alone.
    /// </summary>
    [Fact]
    public void RoyalMailAndKix_RoundTrip()
    {
        AssertReads(new RoyalMailSymbology(), "SN34RD1A", BarcodeFamily.RoyalMail, null, "SN34RD1A");
        AssertReads(new KixSymbology(), "1231FZ13XHS", BarcodeFamily.Kix, null, "1231FZ13XHS");
    }

    /// <summary>
    /// A Japan Post barcode decodes to its digits, hyphens and letters. The filler codes and the check code are symbol overhead.
    /// </summary>
    /// <param name="text">The text.</param>
    [Theory]
    [InlineData("1234567")]
    [InlineData("15400233-16-4-205")]
    [InlineData("1060032AZ-K")]
    public void JapanPost_RoundTrips(string text)
        => AssertReads(new JapanPostSymbology(), text, BarcodeFamily.JapanPost, null, text);

    /// <summary>
    /// A 4-state Mailmark barcode C or L decodes to its application string.
    /// </summary>
    /// <param name="text">The application string.</param>
    [Theory]
    [InlineData("1100000000000XY11     ")]
    [InlineData("21B2254800659JW5O9QA6Y")]
    [InlineData("11000000000000000XY11     ")]
    [InlineData("41038422416563762EF61AH8T ")]
    public void Mailmark_RoundTrips(string text)
        => AssertReads(new FourStateMailmarkSymbology(), text, BarcodeFamily.Mailmark, null, text);

    /// <summary>
    /// A DAFT symbol decodes to the letters of its bar states, and a two-track Pharmacode to the number it carries.
    /// </summary>
    [Fact]
    public void DaftAndTwoTrackPharmacode_RoundTrip()
    {
        AssertReads(new DaftSymbology(), "FADTFADTTDAF", BarcodeFamily.Daft, null, "FADTFADTTDAF");
        AssertReads(new TwoTrackPharmacodeSymbology(), "117480", BarcodeFamily.TwoTrackPharmacode, null, "117480");
    }

    /// <summary>
    /// A PDF417 symbol decodes to its data in every compaction mode: text in the four sub-modes with their latches and shifts, runs of digits in Numeric
    /// Compaction mode, and bytes in Byte Compaction mode, single and in groups of six, at every error correction level and column count.
    /// </summary>
    /// <param name="columns">The data columns.</param>
    /// <param name="level">The error correction level.</param>
    /// <param name="text">The data.</param>
    [Theory]
    [InlineData(3, 2, "Hello, World! (lower case and UPPER CASE; mixed 123 & punctuation: @#$%)")]
    [InlineData(2, 0, "12345678901234567890123456789012345678901234567890123")]
    [InlineData(4, 5, "\u0001\u0002\u0003\u00FF\u00FEbinary\u0000bytes\u0080")]
    [InlineData(6, 8, "Gr\u00F6\u00DFe \u00C4\u00D6\u00DC 123456789012345 caf\u00E9")]
    [InlineData(10, 4, "Tab\tCR\rLF\nend")]
    public void Pdf417_RoundTripsEveryMode(int columns, int level, string text)
    {
        text = System.Text.RegularExpressions.Regex.Unescape(text);
        AssertReads(new Pdf417Symbology(columns, level), text, BarcodeFamily.Pdf417, "]L2", text);
        AssertReads(new CompactPdf417Symbology(columns, level), text, BarcodeFamily.Pdf417, "]L2", text);
    }

    /// <summary>
    /// A MicroPDF417 symbol decodes at every column count, and a message in format 05 or 06 decodes to its header and trailer, which the macro codeword
    /// stands for.
    /// </summary>
    /// <param name="columns">The data columns.</param>
    /// <param name="text">The data.</param>
    [Theory]
    [InlineData(1, "MICRO")]
    [InlineData(2, "MicroPDF417 two")]
    [InlineData(3, "MicroPDF417 with three columns 0123456789")]
    [InlineData(4, "MicroPDF417 with four columns and a little more data 0123456789 \u00E9")]
    [InlineData(4, "[)>\u001E05\u001D1234567890123\u001E\u0004")]
    [InlineData(4, "[)>\u001E06\u001D12345 ABC\u001E\u0004")]
    public void MicroPdf417_RoundTripsEveryColumnCount(int columns, string text)
    {
        text = System.Text.RegularExpressions.Regex.Unescape(text);
        AssertReads(new MicroPdf417Symbology(columns), text, BarcodeFamily.MicroPdf417, "]L2", text);
    }

    /// <summary>
    /// A Code 16K symbol decodes to its data in every starting mode and code set, from two rows to sixteen.
    /// </summary>
    /// <param name="text">The data.</param>
    [Theory]
    [InlineData("CODE 16K")]
    [InlineData("12345678")]
    [InlineData("a1234567890")]
    [InlineData("ab1234567890")]
    [InlineData("Hello\tWorld\r\nControl\u0001and lower case 1234 and 123456 digits")]
    [InlineData("The quick brown fox jumps over the lazy dog 0123456789 !\"#$%&'()*+")]
    public void Code16K_RoundTrips(string text)
    {
        text = System.Text.RegularExpressions.Regex.Unescape(text);
        AssertReads(new Code16KSymbology(), text, BarcodeFamily.Code16K, "]K0", text);
    }

    /// <summary>
    /// A Code 49 symbol decodes to its data in every starting mode, with digit runs of every remainder of the numeric encodation method, from two rows to eight.
    /// </summary>
    /// <param name="text">The data.</param>
    [Theory]
    [InlineData("CODE 49")]
    [InlineData("A")]
    [InlineData("12345")]
    [InlineData("123456")]
    [InlineData("1234567")]
    [InlineData("12345678")]
    [InlineData("123456789")]
    [InlineData("A1234567890123B")]
    [InlineData("lower Case\u0001\t\r\n")]
    [InlineData("!\"#&'()*,:;<=>?@[\\]")]
    [InlineData("^_`{|}~\u007F")]
    [InlineData("THE QUICK BROWN FOX JUMPS OVER THE LAZY DOG 01234")]
    [InlineData("123456789012345678901234567890123456789012345678901234567890123456789012345678901")]
    public void Code49_RoundTrips(string text)
    {
        text = System.Text.RegularExpressions.Regex.Unescape(text);
        AssertReads(new Code49Symbology(), text, BarcodeFamily.Code49, "]T0", text);
    }

    /// <summary>
    /// A Codablock F symbol decodes to its data in every code set and column count, the extended characters of FNC4 included.
    /// </summary>
    /// <param name="columns">The number of data columns.</param>
    /// <param name="text">The data.</param>
    [Theory]
    [InlineData(4, "CODABLOCK F")]
    [InlineData(8, "1234567890123456")]
    [InlineData(8, "a1234567b")]
    [InlineData(5, "lower and UPPER case\u0001\t\r\n control characters")]
    [InlineData(10, "Latin-1 \u00E9\u00FC\u00DF and digits 0123456789 and more text to fill several rows")]
    [InlineData(20, "The quick brown fox jumps over the lazy dog 0123456789 !\"#$%&'()*+,-./")]
    public void CodablockF_RoundTrips(int columns, string text)
    {
        text = System.Text.RegularExpressions.Regex.Unescape(text);
        AssertReads(new CodablockFSymbology(columns), text, BarcodeFamily.CodablockF, "]O4", text);
    }

    /// <summary>
    /// A HIBC Codablock F symbol decodes to the flag character, the data and the check character it carries.
    /// </summary>
    [Fact]
    public void HibcCodablockF_ReadsTheEncodedData()
        => AssertReads(new HibcCodablockFSymbology(), "A123BJC5D6E71", BarcodeFamily.CodablockF, "]O4", "+A123BJC5D6E71G");

    /// <summary>
    /// A GS1 Composite 2D component decodes to its element strings in CC-A, CC-B and CC-C, through every encodation method and mode of its bit stream.
    /// </summary>
    /// <param name="component">The 2D component.</param>
    /// <param name="columns">The data columns.</param>
    /// <param name="text">The element strings.</param>
    /// <param name="expected">The transmitted data, a group separator after a variable length element string.</param>
    [Theory]
    [InlineData(Gs1CompositeComponent.CcA, 2, "(21)A12345678", "21A12345678")]
    [InlineData(Gs1CompositeComponent.CcA, 3, "(11)990102", "11990102")]
    [InlineData(Gs1CompositeComponent.CcA, 4, "(17)010615(10)A123456", "1701061510A123456")]
    [InlineData(Gs1CompositeComponent.CcA, 4, "(17)010200(10)ABC(21)123", "1701020010ABC\u001D21123")]
    [InlineData(Gs1CompositeComponent.CcA, 3, "(15)021231", "15021231")]
    [InlineData(Gs1CompositeComponent.CcA, 4, "(99)1234-abcd", "991234-abcd")]
    [InlineData(Gs1CompositeComponent.CcA, 4, "(90)12BXYZ123", "9012BXYZ123")]
    [InlineData(Gs1CompositeComponent.CcA, 4, "(90)7QLETTERS", "907QLETTERS")]
    [InlineData(Gs1CompositeComponent.CcA, 4, "(90)3H12345", "903H12345")]
    [InlineData(Gs1CompositeComponent.CcB, 4, "(90)999AXYZ(21)12345", "90999AXYZ\u001D2112345")]
    [InlineData(Gs1CompositeComponent.CcB, 3, "(90)5D123(8004)95012345ABC(21)7", "905D123\u001D800495012345ABC\u001D217")]
    [InlineData(Gs1CompositeComponent.CcB, 2, "(10)ABCD123456(410)3898765432108", "10ABCD123456\u001D4103898765432108")]
    [InlineData(Gs1CompositeComponent.CcC, 3, "(10)ABCD123456(410)3898765432108", "10ABCD123456\u001D4103898765432108")]
    [InlineData(Gs1CompositeComponent.CcC, 5, "(91)1A2B3C4D5E(92)abcdefghijklmnopqrstuvwxyz(93)0123456789", "911A2B3C4D5E\u001D92abcdefghijklmnopqrstuvwxyz\u001D930123456789")]
    public void Gs1CompositeComponent_RoundTrips(Gs1CompositeComponent component, int columns, string text, string expected)
    {
        expected = System.Text.RegularExpressions.Regex.Unescape(expected);
        AssertReads(new Gs1CompositeComponentSymbology(component, columns), text, BarcodeFamily.Gs1CompositeComponent, "]e0", expected);
    }

    /// <summary>
    /// With the default families a GS1 Composite symbol reads as its linear component and its 2D component.
    /// </summary>
    /// <param name="kind">The composite symbology.</param>
    /// <param name="linear">The data of the linear component.</param>
    /// <param name="component">The element strings of the 2D component.</param>
    /// <param name="family">The family of the linear component.</param>
    /// <param name="linearExpected">The transmitted data of the linear component.</param>
    /// <param name="componentExpected">The transmitted data of the 2D component.</param>
    [Theory]
    [InlineData("Ean13", "3312345678903", "(99)1234-abcd", BarcodeFamily.Ean13, "3312345678903", "991234-abcd")]
    [InlineData("UpcE", "01234558", "(15)021231", BarcodeFamily.UpcE, "0012345000058", "15021231")]
    [InlineData("Omnidirectional", "(01)03612345678904", "(11)990102", BarcodeFamily.DataBar, "0103612345678904", "11990102")]
    [InlineData("Limited", "(01)13112345678906", "(17)010615(10)A123456", BarcodeFamily.DataBarLimited, "0113112345678906", "1701061510A123456")]
    [InlineData("Expanded", "(01)93712345678904(3103)001234", "(91)1A2B3C4D5E", BarcodeFamily.DataBarExpanded, "01937123456789043103001234", "911A2B3C4D5E")]
    [InlineData("Gs1128", "(01)03212345678906", "(21)A1B2C3D4E5F6G7H8", BarcodeFamily.Code128, "0103212345678906", "21A1B2C3D4E5F6G7H8")]
    [InlineData("Gs1128CcC", "(01)93812345678901", "(10)ABCD123456(410)3898765432108", BarcodeFamily.Code128, "0193812345678901", "10ABCD123456\u001D4103898765432108")]
    public void Gs1Composite_ReadsBothComponents(
        string kind,
        string linear,
        string component,
        BarcodeFamily family,
        string linearExpected,
        string componentExpected)
    {
        componentExpected = System.Text.RegularExpressions.Regex.Unescape(componentExpected);
        CompositeBarcodeSymbology symbology = kind switch
        {
            "Ean13" => new Ean13CompositeSymbology(),
            "UpcE" => new UpcECompositeSymbology(),
            "Omnidirectional" => new Gs1DataBarOmnidirectionalCompositeSymbology(),
            "Limited" => new Gs1DataBarLimitedCompositeSymbology(),
            "Expanded" => new Gs1DataBarExpandedCompositeSymbology(),
            "Gs1128" => new Gs1128CompositeSymbology(),
            _ => new Gs1128CompositeSymbology(Gs1CompositeComponent.CcC),
        };

        PointF origin = new(20, 20);
        BarcodeOptions options = new();
        RectangleF bounds = BarcodeMeasurer.MeasureRenderableBounds(symbology, options, origin, linear, component);
        using Image<L8> image = new((int)MathF.Ceiling(bounds.Right) + 20, (int)MathF.Ceiling(bounds.Bottom) + 20, new L8(255));
        image.Mutate(x => x.Paint(canvas => canvas.DrawBarcode(symbology, options, origin, linear, component)));

        string[] results = [.. BarcodeDecoder.Decode(image).Select(r => $"{r.Family}:{r.Text}").Order(StringComparer.Ordinal)];
        string[] expected = [.. new[] { $"{family}:{linearExpected}", $"{BarcodeFamily.Gs1CompositeComponent}:{componentExpected}" }.Order(StringComparer.Ordinal)];
        Assert.Equal(expected, results);
    }

    /// <summary>
    /// A QR Code symbol decodes in every version group and at every error correction level, with the version information from version 7.
    /// </summary>
    /// <param name="version">The version.</param>
    /// <param name="level">The error correction level.</param>
    [Theory]
    [InlineData(1, QrErrorCorrectionLevel.L)]
    [InlineData(2, QrErrorCorrectionLevel.M)]
    [InlineData(6, QrErrorCorrectionLevel.Q)]
    [InlineData(7, QrErrorCorrectionLevel.H)]
    [InlineData(10, QrErrorCorrectionLevel.M)]
    [InlineData(27, QrErrorCorrectionLevel.Q)]
    public void QrCode_RoundTripsEveryVersionGroup(int version, QrErrorCorrectionLevel level)
        => AssertReads(new QrCodeSymbology(level, version), "QR 0123456789 abc", BarcodeFamily.QrCode, "]Q1", "QR 0123456789 abc");

    /// <summary>
    /// A GS1 QR Code symbol transmits its element strings with the symbology identifier ]Q3.
    /// </summary>
    [Fact]
    public void Gs1QrCode_ReadsTheElementStrings()
        => AssertReads(new Gs1QrCodeSymbology(), "(01)09521234543213(10)ABC123(21)XYZ789", BarcodeFamily.QrCode, "]Q3", "010952123454321310ABC123\u001D21XYZ789");

    /// <summary>
    /// A Micro QR Code symbol decodes in every version and at every level the version has.
    /// </summary>
    /// <param name="version">The version, 1 to 4 for M1 to M4.</param>
    /// <param name="level">The error correction level.</param>
    /// <param name="text">The data.</param>
    [Theory]
    [InlineData(1, QrErrorCorrectionLevel.L, "12345")]
    [InlineData(2, QrErrorCorrectionLevel.L, "ABC12")]
    [InlineData(2, QrErrorCorrectionLevel.M, "AB12")]
    [InlineData(3, QrErrorCorrectionLevel.L, "Micro QR")]
    [InlineData(3, QrErrorCorrectionLevel.M, "MICRO 1")]
    [InlineData(4, QrErrorCorrectionLevel.L, "Micro QR M4 abc")]
    [InlineData(4, QrErrorCorrectionLevel.M, "Micro QR 12345")]
    [InlineData(4, QrErrorCorrectionLevel.Q, "MICRO 123")]
    public void MicroQrCode_RoundTripsEveryVersion(int version, QrErrorCorrectionLevel level, string text)
        => AssertReads(new MicroQrCodeSymbology(level, version), text, BarcodeFamily.MicroQrCode, "]Q1", text);

    /// <summary>
    /// A Rectangular Micro QR Code symbol decodes in every height and at both levels.
    /// </summary>
    /// <param name="version">The version.</param>
    /// <param name="level">The error correction level.</param>
    /// <param name="text">The data.</param>
    [Theory]
    [InlineData(RectangularMicroQrCodeVersion.R7x43, QrErrorCorrectionLevel.M, "1234567")]
    [InlineData(RectangularMicroQrCodeVersion.R7x139, QrErrorCorrectionLevel.H, "rMQR 12")]
    [InlineData(RectangularMicroQrCodeVersion.R9x77, QrErrorCorrectionLevel.M, "rMQR 12")]
    [InlineData(RectangularMicroQrCodeVersion.R11x27, QrErrorCorrectionLevel.M, "RMQR")]
    [InlineData(RectangularMicroQrCodeVersion.R13x99, QrErrorCorrectionLevel.H, "rMQR 12")]
    [InlineData(RectangularMicroQrCodeVersion.R15x59, QrErrorCorrectionLevel.M, "rMQR 12")]
    [InlineData(RectangularMicroQrCodeVersion.R17x139, QrErrorCorrectionLevel.H, "rMQR 12")]
    public void RectangularMicroQrCode_RoundTripsEveryHeight(RectangularMicroQrCodeVersion version, QrErrorCorrectionLevel level, string text)
        => AssertReads(new RectangularMicroQrCodeSymbology(level, version), text, BarcodeFamily.RectangularMicroQrCode, "]Q1", text);

    /// <summary>
    /// A Data Matrix symbol decodes in every encodation the encoder chooses: ASCII with digit pairs and Upper Shift, C40, Text, X12, EDIFACT and Base 256.
    /// </summary>
    /// <param name="text">The data.</param>
    [Theory]
    [InlineData("DATA MATRIX")]
    [InlineData("12345678901234567890")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZ 0123456789")]
    [InlineData("data matrix in text encodation with small letters")]
    [InlineData("X12*X12>X12\rX12*X12>X12\r")]
    [InlineData("EDIFACT=:;<>?@[\\]^_ EDIFACT=:;<>?@[\\]^_")]
    [InlineData("Base 256 \u00E9\u00E8\u00EA\u00EB\u00E0\u00E2\u00E4\u00F6\u00FC\u00DF\u0001\u0002\u0003")]
    [InlineData("Mixed: abc ABC 123 \u00E9 !\"#$%&'()*+,-./")]
    public void DataMatrix_RoundTripsEveryEncodation(string text)
    {
        text = System.Text.RegularExpressions.Regex.Unescape(text);
        AssertReads(new DataMatrixSymbology(), text, BarcodeFamily.DataMatrix, "]d1", text);
    }

    /// <summary>
    /// A Data Matrix symbol decodes in sizes of one to thirty-six data regions, square and rectangular, with the rectangular extension.
    /// </summary>
    /// <param name="rows">The rows.</param>
    /// <param name="columns">The columns.</param>
    [Theory]
    [InlineData(10, 10)]
    [InlineData(26, 26)]
    [InlineData(32, 32)]
    [InlineData(64, 64)]
    [InlineData(120, 120)]
    [InlineData(144, 144)]
    [InlineData(8, 18)]
    [InlineData(16, 48)]
    public void DataMatrix_RoundTripsEverySizeKind(int rows, int columns)
        => AssertReads(new DataMatrixSymbology(rows, columns), "DM1", BarcodeFamily.DataMatrix, "]d1", "DM1");

    /// <summary>
    /// A Data Matrix symbol of the rectangular extension decodes.
    /// </summary>
    /// <param name="rows">The rows.</param>
    /// <param name="columns">The columns.</param>
    [Theory]
    [InlineData(8, 48)]
    [InlineData(8, 144)]
    [InlineData(12, 88)]
    [InlineData(20, 44)]
    [InlineData(26, 64)]
    public void DataMatrixRectangularExtension_RoundTrips(int rows, int columns)
        => AssertReads(new DataMatrixRectangularExtensionSymbology(rows, columns), "DMRE 1", BarcodeFamily.DataMatrix, "]d1", "DMRE 1");

    /// <summary>
    /// A GS1 Data Matrix symbol transmits its element strings with the symbology identifier ]d2, and a message in format 06 its header and trailer.
    /// </summary>
    [Fact]
    public void DataMatrix_ReadsGs1AndMacroMessages()
    {
        AssertReads(new Gs1DataMatrixSymbology(), "(01)09521234543213(10)ABC123(21)XYZ", BarcodeFamily.DataMatrix, "]d2", "010952123454321310ABC123\u001D21XYZ");
        AssertReads(new DataMatrixSymbology(), "[)>\u001E06\u001D12345 ABC\u001E\u0004", BarcodeFamily.DataMatrix, "]d1", "[)>\u001E06\u001D12345 ABC\u001E\u0004");
    }

    /// <summary>
    /// An Aztec Code symbol decodes through every code set of Table 2, its punctuation pairs and its byte strings.
    /// </summary>
    /// <param name="text">The data.</param>
    [Theory]
    [InlineData("AZTEC CODE")]
    [InlineData("lower case aztec")]
    [InlineData("Mixed CASE with Digits 0123456789, 42.5")]
    [InlineData("Punctuation: !\"#$%&'()*+,-./:;<=>?[]{} and pairs. Done, ok\r\n")]
    [InlineData("Control \u0001\u0007\t and @\\^_`|~\u007F")]
    [InlineData("Bytes \u00E9\u00E8\u00EA\u00EB\u00E0\u00E2\u00E4\u00F6\u00FC\u00DF\u0080\u00FF")]
    public void AztecCode_RoundTripsEveryCodeSet(string text)
    {
        text = System.Text.RegularExpressions.Regex.Unescape(text);
        AssertReads(new AztecCodeSymbology(), text, BarcodeFamily.Aztec, "]z0", text);
    }

    /// <summary>
    /// An Aztec Code symbol decodes in every compact size and in full-range sizes of every codeword size, with the reference grid.
    /// </summary>
    /// <param name="compact">Whether the symbol is compact.</param>
    /// <param name="layers">The number of data layers.</param>
    [Theory]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    [InlineData(true, 3)]
    [InlineData(true, 4)]
    [InlineData(false, 1)]
    [InlineData(false, 5)]
    [InlineData(false, 9)]
    [InlineData(false, 15)]
    [InlineData(false, 23)]
    [InlineData(false, 32)]
    public void AztecCode_RoundTripsEverySizeKind(bool compact, int layers)
    {
        BarcodeSymbology symbology = compact ? new AztecCodeCompactSymbology(23, layers) : new AztecCodeSymbology(23, layers);
        AssertReads(symbology, "Aztec 123", BarcodeFamily.Aztec, "]z0", "Aztec 123");
    }

    /// <summary>
    /// An Aztec Rune decodes to its value as three digits.
    /// </summary>
    /// <param name="value">The value.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(127)]
    [InlineData(255)]
    public void AztecRune_RoundTrips(int value)
    {
        string text = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        AssertReads(new AztecRuneSymbology(), text, BarcodeFamily.AztecRune, "]zC", value.ToString("D3", System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// A MaxiCode symbol decodes in modes 4, 5 and 6 through every code set of Table 3, its numeric shift and its locks.
    /// </summary>
    /// <param name="mode">The mode.</param>
    /// <param name="text">The data.</param>
    [Theory]
    [InlineData(MaxiCodeMode.Standard, "MAXICODE 4")]
    [InlineData(MaxiCodeMode.FullErrorCorrection, "MaxiCode 5 with lower case")]
    [InlineData(MaxiCodeMode.ReaderProgramming, "READER")]
    [InlineData(MaxiCodeMode.Standard, "Digits 123456789012345678 and more")]
    [InlineData(MaxiCodeMode.Standard, "Latin-1 \u00C0\u00C1\u00C2\u00E0\u00E1\u00E2 \u00AA\u00BF and control \u0001\u0002")]
    [InlineData(MaxiCodeMode.Standard, "Punctuation {}~;<=>?[\\]^_,./:@!| \u007F")]
    public void MaxiCode_RoundTripsEveryModeAndCodeSet(MaxiCodeMode mode, string text)
    {
        text = System.Text.RegularExpressions.Regex.Unescape(text);
        AssertReads(new MaxiCodeSymbology(mode), text, BarcodeFamily.MaxiCode, "]U0", text);
    }

    /// <summary>
    /// A MaxiCode structured carrier message decodes to the postal code, the country code and the class of service, then the secondary data, in modes 2 and 3,
    /// and after the header of a message in format 01.
    /// </summary>
    /// <param name="mode">The mode.</param>
    /// <param name="text">The data.</param>
    [Theory]
    [InlineData(MaxiCodeMode.StructuredCarrierNumeric, "152382802\u001D840\u001D001\u001DMESSAGE 1")]
    [InlineData(MaxiCodeMode.StructuredCarrierAlphanumeric, "B1050\u001D056\u001D999\u001DMessage 2")]
    [InlineData(MaxiCodeMode.StructuredCarrierNumeric, "[)>\u001E01\u001D96123456789\u001D840\u001D002\u001D1Z12345\u001DUPSN\u001E\u0004")]
    public void MaxiCode_ReadsStructuredCarrierMessages(MaxiCodeMode mode, string text)
    {
        text = System.Text.RegularExpressions.Regex.Unescape(text);
        AssertReads(new MaxiCodeSymbology(mode), text, BarcodeFamily.MaxiCode, "]U1", text);
    }

    /// <summary>
    /// A Han Xin Code symbol decodes with Numeric and Byte segments at every level and in versions with and without alignment patterns.
    /// </summary>
    /// <param name="level">The error correction level.</param>
    /// <param name="version">The version, or 0 for the smallest.</param>
    /// <param name="text">The data.</param>
    [Theory]
    [InlineData(HanXinErrorCorrectionLevel.L1, 0, "Han Xin Code")]
    [InlineData(HanXinErrorCorrectionLevel.L2, 0, "1234567890123")]
    [InlineData(HanXinErrorCorrectionLevel.L3, 0, "abc 12345 def 12345678 \u00E9\u00FC")]
    [InlineData(HanXinErrorCorrectionLevel.L4, 5, "HX5")]
    [InlineData(HanXinErrorCorrectionLevel.L2, 20, "Version 20 with alignment patterns 1")]
    [InlineData(HanXinErrorCorrectionLevel.L1, 84, "V84")]
    public void HanXin_RoundTrips(HanXinErrorCorrectionLevel level, int version, string text)
    {
        text = System.Text.RegularExpressions.Regex.Unescape(text);
        AssertReads(version == 0 ? new HanXinSymbology(level) : new HanXinSymbology(level, version), text, BarcodeFamily.HanXin, "]h0", text);
    }

    /// <summary>
    /// A Code One symbol decodes through the ASCII, C40, Text, X12, Decimal and Byte modes, in the versions A to H and T.
    /// </summary>
    /// <param name="version">The version, or <see langword="null"/> for the smallest of A to H.</param>
    /// <param name="text">The data.</param>
    [Theory]
    [InlineData(null, "Code One")]
    [InlineData(null, "CODE ONE IN C40 MODE")]
    [InlineData(null, "code one in text mode")]
    [InlineData(null, "X12>X12*ABC\r123456")]
    [InlineData(null, "123456789012345678901234567890123")]
    [InlineData(null, "AB12345678901234567890123456789012345")]
    [InlineData(null, "1234567890123456789012345678901234X")]
    [InlineData(null, "Bytes \u00E9\u00FC\u00E9\u00FC\u00E9\u00FC\u00E9\u00FC\u00E9\u00FC")]
    [InlineData(null, "Mixed abc ABC DEF GHI 1234 \u0001\u0002 xyz 99")]
    [InlineData(CodeOneVersion.B, "B")]
    [InlineData(CodeOneVersion.D, "Version D with more room")]
    [InlineData(CodeOneVersion.F, "F")]
    [InlineData(CodeOneVersion.H, "H")]
    [InlineData(CodeOneVersion.T16, "T16")]
    [InlineData(CodeOneVersion.T32, "Version T32")]
    [InlineData(CodeOneVersion.T48, "Version T48 holds more")]
    public void CodeOne_RoundTrips(CodeOneVersion? version, string text)
    {
        text = System.Text.RegularExpressions.Regex.Unescape(text);
        CodeOneSymbology symbology = version is null ? new CodeOneSymbology() : new CodeOneSymbology(version.Value);
        AssertReads(symbology, text, BarcodeFamily.CodeOne, "]o0", text);
    }

    /// <summary>
    /// A Code One S version decodes to its number, which carries no leading zeros.
    /// </summary>
    /// <param name="version">The version.</param>
    /// <param name="text">The digits.</param>
    /// <param name="expected">The number.</param>
    [Theory]
    [InlineData(CodeOneVersion.S10, "123456", "123456")]
    [InlineData(CodeOneVersion.S10, "0042", "42")]
    [InlineData(CodeOneVersion.S20, "123456789012", "123456789012")]
    [InlineData(CodeOneVersion.S30, "999999999999999999", "999999999999999999")]
    public void CodeOne_ReadsNumericVersions(CodeOneVersion version, string text, string expected)
        => AssertReads(new CodeOneSymbology(version), text, BarcodeFamily.CodeOne, "]o0", expected);

    /// <summary>
    /// A DotCode symbol decodes through code sets A, B and C, their shifts and latches, the upper shifts, binary mode and the macro codewords, at sizes from
    /// the data and fixed sizes with even and odd rows.
    /// </summary>
    /// <param name="columns">The columns, or 0 for the size from the data.</param>
    /// <param name="rows">The rows, or 0 for the rows from the columns.</param>
    /// <param name="text">The data.</param>
    [Theory]
    [InlineData(0, 0, "DotCode")]
    [InlineData(0, 0, "1234567890123456")]
    [InlineData(0, 0, "UPPER CASE CONTROL \u0001\u0002\u0003\u0004\u0005\u0006\u0007 AND MORE")]
    [InlineData(0, 0, "Mixed Case with digits 12345678 and punctuation {}|~")]
    [InlineData(0, 0, "Line one\r\nLine two\ttab")]
    [InlineData(0, 0, "Bytes \u00E9\u00FC\u00C0 and a run \u00E9\u00E9\u00E9\u00E9\u00E9\u00E9\u00E9\u00E9\u00E9\u00E9\u00E9 end")]
    [InlineData(0, 0, "\u00E9\u00E9\u00E9\u00E9\u00E91234567890\u00E9\u00E9")]
    [InlineData(0, 0, "A\u0080B\u00A01")]
    [InlineData(0, 0, "Expiry 1726123110ABC")]
    [InlineData(0, 0, "[)>\u001E05\u001DMACRO 05\u001E\u0004")]
    [InlineData(0, 0, "[)>\u001E06\u001DMACRO 06\u001E\u0004")]
    [InlineData(0, 0, "[)>\u001E12\u001DMACRO 12\u001E\u0004")]
    [InlineData(0, 0, "[)>\u001E42MACRO ANY\u0004")]
    [InlineData(20, 0, "Twenty columns")]
    [InlineData(13, 10, "sm")]
    [InlineData(40, 31, "Odd rows")]
    [InlineData(41, 30, "Even rows")]
    public void DotCode_RoundTrips(int columns, int rows, string text)
    {
        text = System.Text.RegularExpressions.Regex.Unescape(text);
        DotCodeSymbology symbology = columns == 0 ? new DotCodeSymbology() : rows == 0 ? new DotCodeSymbology(columns) : new DotCodeSymbology(columns, rows);
        AssertReads(symbology, text, BarcodeFamily.DotCode, "]J0", text);
    }

    /// <summary>
    /// A GS1 DotCode symbol transmits its element strings with the symbology identifier ]J1, through the 17xxxxxx10 macro where the expiry date meets a batch.
    /// </summary>
    /// <param name="text">The element strings.</param>
    /// <param name="expected">The transmitted data.</param>
    [Theory]
    [InlineData("(01)09521234543213(17)240101(10)ABC123", "01095212345432131724010110ABC123")]
    [InlineData("(10)ABC123(21)XYZ789", "10ABC123\u001D21XYZ789")]
    [InlineData("(01)09521234543213(3103)000123", "01095212345432133103000123")]
    public void Gs1DotCode_ReadsTheElementStrings(string text, string expected)
        => AssertReads(new Gs1DotCodeSymbology(), text, BarcodeFamily.DotCode, "]J1", System.Text.RegularExpressions.Regex.Unescape(expected));

    /// <summary>
    /// A symbol reads upright, on either side and upside down.
    /// </summary>
    /// <param name="mode">The rotation of the image.</param>
    [Theory]
    [InlineData(RotateMode.None)]
    [InlineData(RotateMode.Rotate90)]
    [InlineData(RotateMode.Rotate180)]
    [InlineData(RotateMode.Rotate270)]
    public void Decode_ReadsRotatedSymbols(RotateMode mode)
    {
        foreach ((BarcodeSymbology symbology, string text, BarcodeFamily family, string expected) in RotationCases())
        {
            using Image<L8> image = DrawImage(symbology, text, new BarcodeOptions());
            image.Mutate(x => x.Rotate(mode));

            DecodedBarcode result = Assert.Single(BarcodeDecoder.Decode(image, new BarcodeDecoderOptions { Families = family }));
            Assert.Equal(expected, result.Text);
        }
    }

    /// <summary>
    /// With the default families every symbol reads once, as the family it draws with.
    /// </summary>
    [Fact]
    public void Decode_DefaultFamiliesReadEachSymbolOnce()
    {
        foreach ((BarcodeSymbology symbology, string text, BarcodeFamily family, string expected) in RotationCases())
        {
            using Image<L8> image = DrawImage(symbology, text, new BarcodeOptions());

            DecodedBarcode result = Assert.Single(BarcodeDecoder.Decode(image));
            Assert.Equal(family, result.Family);
            Assert.Equal(expected, result.Text);
        }
    }

    /// <summary>
    /// Gets the symbols the rotation and default family tests draw.
    /// </summary>
    /// <returns>The symbology, the text, the family and the transmitted data.</returns>
    private static IEnumerable<(BarcodeSymbology Symbology, string Text, BarcodeFamily Family, string Expected)> RotationCases()
    {
        yield return (new Ean13Symbology(), "5901234123457", BarcodeFamily.Ean13, "5901234123457");
        yield return (new Code128Symbology(), "ABC-123", BarcodeFamily.Code128, "ABC-123");
        yield return (new Code93Symbology(), "CODE93", BarcodeFamily.Code93, "CODE93");
        yield return (new RoyalMailSymbology(), "SN34RD1A", BarcodeFamily.RoyalMail, "SN34RD1A");
        yield return (new Pdf417Symbology(), "PDF417 rotated", BarcodeFamily.Pdf417, "PDF417 rotated");
        yield return (new MicroPdf417Symbology(), "Micro rotated", BarcodeFamily.MicroPdf417, "Micro rotated");
        yield return (new Code16KSymbology(), "Code 16K rotated", BarcodeFamily.Code16K, "Code 16K rotated");
        yield return (new Code49Symbology(), "Code 49 rotated", BarcodeFamily.Code49, "Code 49 rotated");
        yield return (new CodablockFSymbology(), "Codablock F rotated", BarcodeFamily.CodablockF, "Codablock F rotated");
        yield return (new Gs1CompositeComponentSymbology(), "(21)A12345678", BarcodeFamily.Gs1CompositeComponent, "21A12345678");
        yield return (new Gs1CompositeComponentSymbology(Gs1CompositeComponent.CcB, 3), "(10)ABC123", BarcodeFamily.Gs1CompositeComponent, "10ABC123");
        yield return (new QrCodeSymbology(), "QR rotated", BarcodeFamily.QrCode, "QR rotated");
        yield return (new MicroQrCodeSymbology(), "Micro QR", BarcodeFamily.MicroQrCode, "Micro QR");
        yield return (new RectangularMicroQrCodeSymbology(), "rMQR rotated", BarcodeFamily.RectangularMicroQrCode, "rMQR rotated");
        yield return (new DataMatrixSymbology(), "Data Matrix rotated", BarcodeFamily.DataMatrix, "Data Matrix rotated");
        yield return (new DataMatrixRectangularSymbology(), "DM rect", BarcodeFamily.DataMatrix, "DM rect");
        yield return (new AztecCodeSymbology(), "Aztec rotated", BarcodeFamily.Aztec, "Aztec rotated");
        yield return (new AztecCodeSymbology(23, 6), "Aztec full range", BarcodeFamily.Aztec, "Aztec full range");
        yield return (new AztecRuneSymbology(), "42", BarcodeFamily.AztecRune, "042");
        yield return (new MaxiCodeSymbology(), "MaxiCode rotated", BarcodeFamily.MaxiCode, "MaxiCode rotated");
        yield return (new HanXinSymbology(), "Han Xin rotated", BarcodeFamily.HanXin, "Han Xin rotated");
        yield return (new CodeOneSymbology(), "Code One rotated", BarcodeFamily.CodeOne, "Code One rotated");
        yield return (new CodeOneSymbology(CodeOneVersion.T16), "T16", BarcodeFamily.CodeOne, "T16");
        yield return (new CodeOneSymbology(CodeOneVersion.S10), "123456", BarcodeFamily.CodeOne, "123456");
        yield return (new DotCodeSymbology(), "DotCode rotated", BarcodeFamily.DotCode, "DotCode rotated");
        yield return (new DotCodeSymbology(30, 21), "DotCode odd rows", BarcodeFamily.DotCode, "DotCode odd rows");
        yield return (new IntelligentMailSymbology(), "0123456709498765432101234567891", BarcodeFamily.IntelligentMail, "0123456709498765432101234567891");
    }

    /// <summary>
    /// Draws a symbol, decodes it restricted to one family and checks the family, the symbology identifier and the transmitted data.
    /// </summary>
    /// <param name="symbology">The symbology.</param>
    /// <param name="text">The text to encode.</param>
    /// <param name="family">The family to read.</param>
    /// <param name="identifier">The symbology identifier, or <see langword="null"/> where the test does not check it.</param>
    /// <param name="expected">The transmitted data.</param>
    private static void AssertReads(BarcodeSymbology symbology, string text, BarcodeFamily family, string identifier, string expected)
    {
        using Image<L8> image = DrawImage(symbology, text, new BarcodeOptions());

        DecodedBarcode result = Assert.Single(BarcodeDecoder.Decode(image, new BarcodeDecoderOptions { Families = family }));
        Assert.Equal(family, result.Family);
        if (identifier is not null)
        {
            Assert.Equal(identifier, result.SymbologyIdentifier);
        }

        Assert.Equal(expected, result.Text);
    }

    /// <summary>
    /// Draws a symbol into a grey image with a margin on every side.
    /// </summary>
    /// <param name="symbology">The symbology.</param>
    /// <param name="text">The text to encode.</param>
    /// <param name="options">The options to draw with.</param>
    /// <returns>The image.</returns>
    private static Image<L8> DrawImage(BarcodeSymbology symbology, string text, BarcodeOptions options)
    {
        byte[] pixels = BarcodeDecodeTests.Draw(symbology, text, options, out int width, out int height);
        return Image.LoadPixelData<L8>(pixels, width, height);
    }
}
