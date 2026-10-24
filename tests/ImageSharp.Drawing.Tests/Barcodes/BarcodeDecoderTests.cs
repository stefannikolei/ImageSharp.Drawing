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
