// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The luminance of an image, one byte for each pixel, row by row. The decoder reads every symbology except Ultracode from the luminance alone.
/// </summary>
internal sealed class LuminanceImage : IDisposable
{
    private byte[]? pixels;

    /// <summary>
    /// Initializes a new instance of the <see cref="LuminanceImage"/> class.
    /// </summary>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    public LuminanceImage(int width, int height)
    {
        this.Width = width;
        this.Height = height;
        this.pixels = ArrayPool<byte>.Shared.Rent(width * height);
    }

    /// <summary>
    /// Gets the width in pixels.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Gets the height in pixels.
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// Gets the luminance of every pixel, row by row, 0 for black and 255 for white.
    /// </summary>
    public Span<byte> Pixels => this.pixels.AsSpan(0, this.Width * this.Height);

    /// <summary>
    /// Returns the luminance of one pixel.
    /// </summary>
    /// <param name="x">The column.</param>
    /// <param name="y">The row.</param>
    /// <returns>The luminance.</returns>
    public byte this[int x, int y] => this.pixels![(y * this.Width) + x];

    /// <summary>
    /// Returns one row of the luminance.
    /// </summary>
    /// <param name="y">The row.</param>
    /// <returns>The luminance of the row.</returns>
    public ReadOnlySpan<byte> Row(int y) => this.pixels.AsSpan(y * this.Width, this.Width);

    /// <summary>
    /// Creates the luminance of an image. The pixel conversion of ImageSharp gives the luminance, so every pixel format converts the same way.
    /// </summary>
    /// <param name="image">The image.</param>
    /// <returns>The luminance.</returns>
    public static LuminanceImage Create(Image image)
    {
        LuminanceImage luminance = new(image.Width, image.Height);
        if (image is Image<L8> grey)
        {
            grey.CopyPixelDataTo(luminance.Pixels);
        }
        else
        {
            using Image<L8> converted = image.CloneAs<L8>();
            converted.CopyPixelDataTo(luminance.Pixels);
        }

        return luminance;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        byte[]? toReturn = this.pixels;
        this.pixels = null;
        if (toReturn is not null)
        {
            ArrayPool<byte>.Shared.Return(toReturn);
        }
    }
}
