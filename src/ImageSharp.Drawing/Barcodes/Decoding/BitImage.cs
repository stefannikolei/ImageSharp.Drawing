// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// A binarized image, one byte for each pixel, row by row: 1 for a dark pixel and 0 for a light pixel.
/// </summary>
internal sealed class BitImage
{
    /// <summary>
    /// The side of a block of the local threshold, in pixels.
    /// </summary>
    private const int BlockSize = 8;

    /// <summary>
    /// The smallest difference between the darkest and the lightest pixel of a block that makes the block hold an edge. A block with less contrast takes the
    /// threshold of its neighbours.
    /// </summary>
    private const int MinimumDynamicRange = 24;

    private readonly byte[] bits;

    /// <summary>
    /// Initializes a new instance of the <see cref="BitImage"/> class.
    /// </summary>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    public BitImage(int width, int height)
    {
        this.Width = width;
        this.Height = height;
        this.bits = new byte[width * height];
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
    /// Gets a value indicating whether a pixel is dark. A pixel outside the image is light.
    /// </summary>
    /// <param name="x">The column.</param>
    /// <param name="y">The row.</param>
    /// <returns><see langword="true"/> for a dark pixel.</returns>
    public bool this[int x, int y]
        => (uint)x < (uint)this.Width && (uint)y < (uint)this.Height && this.bits[(y * this.Width) + x] != 0;

    /// <summary>
    /// Returns one row, 1 for each dark pixel.
    /// </summary>
    /// <param name="y">The row.</param>
    /// <returns>The pixels of the row.</returns>
    public ReadOnlySpan<byte> Row(int y) => this.bits.AsSpan(y * this.Width, this.Width);

    /// <summary>
    /// Returns one row to write, 1 for each dark pixel.
    /// </summary>
    /// <param name="y">The row.</param>
    /// <returns>The pixels of the row.</returns>
    public Span<byte> RowToWrite(int y) => this.bits.AsSpan(y * this.Width, this.Width);

    /// <summary>
    /// Binarizes the luminance at one threshold for the whole image, the threshold that best separates the two classes of the luminance histogram by the
    /// method of Otsu. A printed or rendered symbol has a dark and a light class, so this threshold falls between them.
    /// </summary>
    /// <param name="luminance">The luminance.</param>
    /// <returns>The binarized image.</returns>
    public static BitImage Global(LuminanceImage luminance)
    {
        ReadOnlySpan<byte> pixels = luminance.Pixels;
        Span<int> histogram = stackalloc int[256];
        histogram.Clear();
        foreach (byte value in pixels)
        {
            histogram[value]++;
        }

        // The threshold maximizes the variance between the classes: w0 w1 (m0 - m1)^2, where w is the pixel count of a class and m its mean.
        long total = pixels.Length;
        long sum = 0;
        for (int i = 0; i < 256; i++)
        {
            sum += (long)i * histogram[i];
        }

        long darkCount = 0;
        long darkSum = 0;
        double best = -1;
        int threshold = 127;
        for (int t = 0; t < 255; t++)
        {
            darkCount += histogram[t];
            darkSum += (long)t * histogram[t];
            long lightCount = total - darkCount;
            if (darkCount == 0 || lightCount == 0)
            {
                continue;
            }

            double darkMean = (double)darkSum / darkCount;
            double lightMean = (double)(sum - darkSum) / lightCount;
            double variance = (double)darkCount * lightCount * (darkMean - lightMean) * (darkMean - lightMean);
            if (variance > best)
            {
                best = variance;
                threshold = t;
            }
        }

        BitImage image = new(luminance.Width, luminance.Height);
        Threshold(pixels, image.bits, (byte)(threshold + 1));
        return image;
    }

    /// <summary>
    /// Binarizes the luminance at a local threshold. The image splits into blocks of 8 x 8 pixels. A block with contrast takes the mean of its pixels as its
    /// black point. A block without contrast stands inside a light or a dark area, so it takes half its darkest value, or the black point of its neighbours
    /// above and to the left when that is lighter. Each pixel then compares with the mean black point of the 5 x 5 blocks around its own. The local threshold
    /// follows uneven lighting, which one global threshold cannot.
    /// </summary>
    /// <param name="luminance">The luminance.</param>
    /// <returns>The binarized image.</returns>
    public static BitImage Hybrid(LuminanceImage luminance)
    {
        int width = luminance.Width;
        int height = luminance.Height;
        int blocksX = (width + BlockSize - 1) / BlockSize;
        int blocksY = (height + BlockSize - 1) / BlockSize;
        int[] blackPoints = new int[blocksX * blocksY];
        ReadOnlySpan<byte> pixels = luminance.Pixels;

        for (int by = 0; by < blocksY; by++)
        {
            int top = Math.Min(by * BlockSize, Math.Max(0, height - BlockSize));
            int rows = Math.Min(BlockSize, height - top);
            for (int bx = 0; bx < blocksX; bx++)
            {
                int left = Math.Min(bx * BlockSize, Math.Max(0, width - BlockSize));
                int columns = Math.Min(BlockSize, width - left);
                int sum = 0;
                int min = 255;
                int max = 0;
                for (int y = 0; y < rows; y++)
                {
                    ReadOnlySpan<byte> row = pixels.Slice(((top + y) * width) + left, columns);
                    foreach (byte value in row)
                    {
                        sum += value;
                        min = Math.Min(min, value);
                        max = Math.Max(max, value);
                    }
                }

                int average = sum / (rows * columns);
                if (max - min <= MinimumDynamicRange)
                {
                    average = min / 2;
                    if (by > 0 && bx > 0)
                    {
                        int neighbours = (blackPoints[((by - 1) * blocksX) + bx] + (2 * blackPoints[(by * blocksX) + bx - 1])
                            + blackPoints[((by - 1) * blocksX) + bx - 1]) / 4;
                        if (min < neighbours)
                        {
                            average = neighbours;
                        }
                    }
                }

                blackPoints[(by * blocksX) + bx] = average;
            }
        }

        BitImage image = new(width, height);
        for (int by = 0; by < blocksY; by++)
        {
            int top = Math.Min(by * BlockSize, Math.Max(0, height - BlockSize));
            int rows = Math.Min(BlockSize, height - top);
            int cy = Math.Clamp(by, 2, Math.Max(2, blocksY - 3));
            for (int bx = 0; bx < blocksX; bx++)
            {
                int left = Math.Min(bx * BlockSize, Math.Max(0, width - BlockSize));
                int columns = Math.Min(BlockSize, width - left);
                int cx = Math.Clamp(bx, 2, Math.Max(2, blocksX - 3));
                int sum = 0;
                int count = 0;
                for (int dy = -2; dy <= 2; dy++)
                {
                    int ny = cy + dy;
                    if ((uint)ny >= (uint)blocksY)
                    {
                        continue;
                    }

                    for (int dx = -2; dx <= 2; dx++)
                    {
                        int nx = cx + dx;
                        if ((uint)nx < (uint)blocksX)
                        {
                            sum += blackPoints[(ny * blocksX) + nx];
                            count++;
                        }
                    }
                }

                byte threshold = (byte)Math.Min(255, (sum / count) + 1);
                for (int y = 0; y < rows; y++)
                {
                    int offset = ((top + y) * width) + left;
                    Threshold(pixels.Slice(offset, columns), image.bits.AsSpan(offset, columns), threshold);
                }
            }
        }

        return image;
    }

    /// <summary>
    /// Returns the image turned a quarter turn clockwise, so a symbol that stands on its side reads along the rows. Column x of the new image is row
    /// <c>Height - 1 - x</c> of this image read from the top down.
    /// </summary>
    /// <returns>The turned image.</returns>
    public BitImage RotateClockwise()
    {
        BitImage turned = new(this.Height, this.Width);
        for (int y = 0; y < this.Height; y++)
        {
            ReadOnlySpan<byte> row = this.Row(y);
            int column = this.Height - 1 - y;
            for (int x = 0; x < this.Width; x++)
            {
                turned.bits[(x * turned.Width) + column] = row[x];
            }
        }

        return turned;
    }

    /// <summary>
    /// Writes 1 for each value below the threshold and 0 for the others. A vector compares many pixels at once: the comparison sets every bit of a lane where
    /// the value is below the threshold, and the mask with one keeps a single bit.
    /// </summary>
    /// <param name="values">The luminance.</param>
    /// <param name="target">The pixels to write.</param>
    /// <param name="threshold">The threshold: a value below it is dark.</param>
    private static void Threshold(ReadOnlySpan<byte> values, Span<byte> target, byte threshold)
    {
        int i = 0;
        if (Vector.IsHardwareAccelerated && values.Length >= Vector<byte>.Count)
        {
            Vector<byte> limit = new(threshold);
            Vector<byte> one = Vector<byte>.One;
            for (; i <= values.Length - Vector<byte>.Count; i += Vector<byte>.Count)
            {
                Vector<byte> dark = Vector.LessThan(new Vector<byte>(values[i..]), limit);
                (dark & one).CopyTo(target[i..]);
            }
        }

        for (; i < values.Length; i++)
        {
            target[i] = values[i] < threshold ? (byte)1 : (byte)0;
        }
    }
}
