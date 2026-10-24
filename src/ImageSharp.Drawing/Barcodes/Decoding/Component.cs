// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// A connected dark area of a binarized image: its bounding box in pixels, the edges included, and its pixel count.
/// </summary>
internal readonly struct Component
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Component"/> struct.
    /// </summary>
    /// <param name="left">The leftmost column.</param>
    /// <param name="top">The topmost row.</param>
    /// <param name="right">The rightmost column.</param>
    /// <param name="bottom">The bottom row.</param>
    /// <param name="pixels">The number of dark pixels.</param>
    public Component(int left, int top, int right, int bottom, int pixels)
    {
        this.Left = left;
        this.Top = top;
        this.Right = right;
        this.Bottom = bottom;
        this.Pixels = pixels;
    }

    /// <summary>
    /// Gets the leftmost column.
    /// </summary>
    public int Left { get; }

    /// <summary>
    /// Gets the topmost row.
    /// </summary>
    public int Top { get; }

    /// <summary>
    /// Gets the rightmost column.
    /// </summary>
    public int Right { get; }

    /// <summary>
    /// Gets the bottom row.
    /// </summary>
    public int Bottom { get; }

    /// <summary>
    /// Gets the number of dark pixels.
    /// </summary>
    public int Pixels { get; }

    /// <summary>
    /// Gets the width in pixels.
    /// </summary>
    public int Width => this.Right - this.Left + 1;

    /// <summary>
    /// Gets the height in pixels.
    /// </summary>
    public int Height => this.Bottom - this.Top + 1;

    /// <summary>
    /// Gets the horizontal centre in pixels.
    /// </summary>
    public float CenterX => (this.Left + this.Right + 1) / 2F;

    /// <summary>
    /// Gets the vertical centre in pixels.
    /// </summary>
    public float CenterY => (this.Top + this.Bottom + 1) / 2F;
}
