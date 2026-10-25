// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Finds barcode symbols in an image and decodes the data they carry. The decoder reads every family of <see cref="BarcodeFamily"/>: the linear symbols
/// along the rows and the columns of the image, the height modulated postal symbols from the heights of their bars, the stacked symbols row by row, and the
/// matrix symbols from their finder patterns. A symbol may stand upright, upside down or on either side.
/// </summary>
public static class BarcodeDecoder
{
    /// <summary>
    /// The linear readers, in the order they try a scan line.
    /// </summary>
    private static readonly LinearReader[] LinearReaders =
    [
        new EanUpcReader(),
        new EanAddOnReader(),
        new Code128Reader(),
        new Code39Reader(),
        new Code93Reader(),
        new CodabarReader(),
        new Interleaved2Of5Reader(),
        new Code2Of5Reader(Code2Of5Variant.Industrial),
        new Code2Of5Reader(Code2Of5Variant.Iata),
        new Code2Of5Reader(Code2Of5Variant.Matrix),
        new Code2Of5Reader(Code2Of5Variant.Coop),
        new Code2Of5Reader(Code2Of5Variant.Datalogic),
        new Code11Reader(),
        new MsiReader(),
        new PlesseyReader(),
        new TelepenReader(),
        new ChannelCodeReader(),
        new Bc412Reader(),
        new PosiCodeReader(),
        new PharmacodeReader(),
        new FacingIdentificationMarkReader(),
        new DataBarReader(),
        new DataBarLimitedReader(),
        new DataBarExpandedReader(),
    ];

    /// <summary>
    /// The height modulated readers.
    /// </summary>
    private static readonly HeightModulatedReader[] HeightModulatedReaders =
    [
        new UspsPostalReader(),
        new IntelligentMailReader(),
        new AustraliaPostReader(),
        new RoyalMailReader(false),
        new RoyalMailReader(true),
        new JapanPostReader(),
        new MailmarkReader(),
        new TwoTrackPharmacodeReader(),
        new DaftReader(),
    ];

    /// <summary>
    /// The stacked readers.
    /// </summary>
    private static readonly StackedReader[] StackedReaders =
    [
        new Pdf417Reader(),
        new MicroPdf417Reader(),
        new Gs1CompositeComponentReader(),
        new Code16KReader(),
        new Code49Reader(),
        new CodablockFReader(),
    ];

    /// <summary>
    /// Finds and decodes the symbols of the default families in an image.
    /// </summary>
    /// <param name="image">The image.</param>
    /// <returns>The symbols found, in the order the decoder found them. The list is empty when the image holds no symbol the decoder reads.</returns>
    public static IReadOnlyList<DecodedBarcode> Decode(Image image)
        => Decode(image, new BarcodeDecoderOptions());

    /// <summary>
    /// Finds and decodes the symbols in an image.
    /// </summary>
    /// <param name="image">The image.</param>
    /// <param name="options">The options that name the families to read.</param>
    /// <returns>The symbols found, in the order the decoder found them. The list is empty when the image holds no symbol the decoder reads.</returns>
    public static IReadOnlyList<DecodedBarcode> Decode(Image image, BarcodeDecoderOptions options)
    {
        Guard.NotNull(image, nameof(image));
        Guard.NotNull(options, nameof(options));

        List<DecodedBarcode> results = [];
        using LuminanceImage luminance = LuminanceImage.Create(image);

        // The global threshold reads a clean image exactly. The local threshold follows uneven lighting, so it is the second try.
        int passes = options.TryHarder ? 2 : 1;
        for (int pass = 0; pass < passes && results.Count == 0; pass++)
        {
            BitImage bits = pass == 0 ? BitImage.Global(luminance) : BitImage.Hybrid(luminance);
            DecodeContext context = new(options, image, luminance, bits, results);
            foreach (StackedReader reader in Select(context, StackedReaders))
            {
                reader.Read(context, bits, false);
                if (options.TryHarder)
                {
                    reader.Read(context, context.TurnedBits, true);
                }
            }

            HeightModulatedScanner.Scan(context, Select(context, HeightModulatedReaders));
            LinearScanner.Scan(context, Select(context, LinearReaders));
        }

        return results;
    }

    /// <summary>
    /// Returns the readers whose families the options name.
    /// </summary>
    /// <typeparam name="T">The kind of reader.</typeparam>
    /// <param name="context">The decode.</param>
    /// <param name="readers">The readers.</param>
    /// <returns>The readers to use.</returns>
    private static T[] Select<T>(DecodeContext context, T[] readers)
        where T : IFamilyReader
    {
        int count = 0;
        foreach (T reader in readers)
        {
            if (context.Wants(reader.Families))
            {
                count++;
            }
        }

        T[] selected = new T[count];
        count = 0;
        foreach (T reader in readers)
        {
            if (context.Wants(reader.Families))
            {
                selected[count++] = reader;
            }
        }

        return selected;
    }
}
