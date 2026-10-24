// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;
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
