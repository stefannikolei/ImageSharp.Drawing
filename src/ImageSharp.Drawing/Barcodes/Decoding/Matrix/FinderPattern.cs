// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The centre of a finder or alignment pattern found in the image, with the module size its rings give and the number of scan lines that confirmed it.
/// </summary>
internal sealed class FinderPattern
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FinderPattern"/> class.
    /// </summary>
    /// <param name="x">The x coordinate of the centre.</param>
    /// <param name="y">The y coordinate of the centre.</param>
    /// <param name="moduleSize">The module size in pixels.</param>
    public FinderPattern(float x, float y, float moduleSize)
    {
        this.X = x;
        this.Y = y;
        this.ModuleSize = moduleSize;
        this.Count = 1;
    }

    /// <summary>
    /// Gets the x coordinate of the centre.
    /// </summary>
    public float X { get; private set; }

    /// <summary>
    /// Gets the y coordinate of the centre.
    /// </summary>
    public float Y { get; private set; }

    /// <summary>
    /// Gets the module size in pixels.
    /// </summary>
    public float ModuleSize { get; private set; }

    /// <summary>
    /// Gets the number of scan lines that confirmed the pattern.
    /// </summary>
    public int Count { get; private set; }

    /// <summary>
    /// Gets the centre.
    /// </summary>
    public PointF Centre => new(this.X, this.Y);

    /// <summary>
    /// Returns the distance between two patterns.
    /// </summary>
    /// <param name="a">The first pattern.</param>
    /// <param name="b">The second pattern.</param>
    /// <returns>The distance in pixels.</returns>
    public static float Distance(FinderPattern a, FinderPattern b) => MathF.Sqrt(SquaredDistance(a, b));

    /// <summary>
    /// Returns the squared distance between two patterns.
    /// </summary>
    /// <param name="a">The first pattern.</param>
    /// <param name="b">The second pattern.</param>
    /// <returns>The squared distance.</returns>
    public static float SquaredDistance(FinderPattern a, FinderPattern b)
    {
        float dx = a.X - b.X;
        float dy = a.Y - b.Y;
        return (dx * dx) + (dy * dy);
    }

    /// <summary>
    /// Returns whether a new reading is of this pattern: its centre within a module of this one, and its module size close.
    /// </summary>
    /// <param name="moduleSize">The module size of the reading.</param>
    /// <param name="x">The x coordinate of the reading.</param>
    /// <param name="y">The y coordinate of the reading.</param>
    /// <returns><see langword="true"/> when the reading is of this pattern.</returns>
    public bool AboutEquals(float moduleSize, float x, float y)
    {
        if (MathF.Abs(y - this.Y) <= moduleSize && MathF.Abs(x - this.X) <= moduleSize)
        {
            float difference = MathF.Abs(moduleSize - this.ModuleSize);
            return difference <= 1F || difference <= this.ModuleSize;
        }

        return false;
    }

    /// <summary>
    /// Averages a new reading into the pattern.
    /// </summary>
    /// <param name="x">The x coordinate of the reading.</param>
    /// <param name="y">The y coordinate of the reading.</param>
    /// <param name="moduleSize">The module size of the reading.</param>
    public void Combine(float x, float y, float moduleSize)
    {
        int count = this.Count + 1;
        this.X = ((this.Count * this.X) + x) / count;
        this.Y = ((this.Count * this.Y) + y) / count;
        this.ModuleSize = ((this.Count * this.ModuleSize) + moduleSize) / count;
        this.Count = count;
    }
}
