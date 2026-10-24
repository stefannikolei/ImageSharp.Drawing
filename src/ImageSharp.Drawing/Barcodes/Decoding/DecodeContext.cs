// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The state of one decode: the options, the image in its forms and the symbols found so far.
/// </summary>
internal sealed class DecodeContext
{
    private readonly List<DecodedBarcode> results;
    private BitImage? turned;

    /// <summary>
    /// Initializes a new instance of the <see cref="DecodeContext"/> class.
    /// </summary>
    /// <param name="options">The options.</param>
    /// <param name="source">The image the luminance comes from.</param>
    /// <param name="luminance">The luminance of the image.</param>
    /// <param name="bits">The binarized image.</param>
    /// <param name="results">The list the symbols found go to.</param>
    public DecodeContext(BarcodeDecoderOptions options, Image source, LuminanceImage luminance, BitImage bits, List<DecodedBarcode> results)
    {
        this.Options = options;
        this.Source = source;
        this.Luminance = luminance;
        this.Bits = bits;
        this.results = results;
    }

    /// <summary>
    /// Gets the options.
    /// </summary>
    public BarcodeDecoderOptions Options { get; }

    /// <summary>
    /// Gets the image the luminance comes from. Ultracode reads its colours from it.
    /// </summary>
    public Image Source { get; }

    /// <summary>
    /// Gets the luminance of the image.
    /// </summary>
    public LuminanceImage Luminance { get; }

    /// <summary>
    /// Gets the binarized image.
    /// </summary>
    public BitImage Bits { get; }

    /// <summary>
    /// Gets the binarized image turned a quarter turn clockwise, made on first use.
    /// </summary>
    public BitImage TurnedBits => this.turned ??= this.Bits.RotateClockwise();

    /// <summary>
    /// Gets the number of symbols found so far.
    /// </summary>
    public int ResultCount => this.results.Count;

    /// <summary>
    /// Returns a value indicating whether the options name any of the families.
    /// </summary>
    /// <param name="families">The families.</param>
    /// <returns><see langword="true"/> when the decoder reads at least one of them.</returns>
    public bool Wants(BarcodeFamily families) => (this.Options.Families & families) != 0;

    /// <summary>
    /// Returns a value indicating whether a symbol of the family with the text has been found.
    /// </summary>
    /// <param name="family">The family.</param>
    /// <param name="text">The text.</param>
    /// <returns><see langword="true"/> when the symbol is in the results.</returns>
    public bool Contains(BarcodeFamily family, string text)
    {
        foreach (DecodedBarcode result in this.results)
        {
            if (result.Family == family && result.Text == text)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Adds a symbol to the results unless a symbol of the same family and text is there.
    /// </summary>
    /// <param name="family">The family.</param>
    /// <param name="text">The transmitted data.</param>
    /// <param name="identifier">The symbology identifier.</param>
    /// <param name="corners">The four corners in pixels of the image.</param>
    /// <returns><see langword="true"/> when the symbol is new.</returns>
    public bool Add(BarcodeFamily family, string text, string identifier, PointF[] corners)
    {
        if (this.Contains(family, text))
        {
            return false;
        }

        this.results.Add(new DecodedBarcode(family, text, identifier, corners));
        return true;
    }
}
