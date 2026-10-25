// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The modules of a matrix symbol as the grid sampler reads them, a set value for a dark module.
/// </summary>
internal sealed class ModuleMatrix
{
    private readonly bool[] modules;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModuleMatrix"/> class with every module light.
    /// </summary>
    /// <param name="rows">The number of rows.</param>
    /// <param name="columns">The number of columns.</param>
    public ModuleMatrix(int rows, int columns)
    {
        this.Rows = rows;
        this.Columns = columns;
        this.modules = new bool[rows * columns];
    }

    /// <summary>
    /// Gets the number of rows.
    /// </summary>
    public int Rows { get; }

    /// <summary>
    /// Gets the number of columns.
    /// </summary>
    public int Columns { get; }

    /// <summary>
    /// Gets or sets a module.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <param name="column">The column.</param>
    /// <returns><see langword="true"/> for a dark module.</returns>
    public bool this[int row, int column]
    {
        get => this.modules[(row * this.Columns) + column];
        set => this.modules[(row * this.Columns) + column] = value;
    }

    /// <summary>
    /// Gets a module by its row-major index.
    /// </summary>
    /// <param name="index">The index, row times the column count plus column.</param>
    /// <returns><see langword="true"/> for a dark module.</returns>
    public bool At(int index) => this.modules[index];

    /// <summary>
    /// Returns the matrix mirrored about its main diagonal, which reads a symbol printed or seen from the back.
    /// </summary>
    /// <returns>The transposed matrix.</returns>
    public ModuleMatrix Transpose()
    {
        ModuleMatrix transposed = new(this.Columns, this.Rows);
        for (int row = 0; row < this.Rows; row++)
        {
            for (int column = 0; column < this.Columns; column++)
            {
                transposed[column, row] = this[row, column];
            }
        }

        return transposed;
    }

    /// <summary>
    /// Samples a grid of modules from a binarized image: each module reads the pixel under its centre, which the transform maps from the module coordinates,
    /// columns along x and rows along y. A centre less than a pixel outside the image reads the edge pixel.
    /// </summary>
    /// <param name="bits">The image.</param>
    /// <param name="transform">The transform from module coordinates to pixels.</param>
    /// <param name="rows">The number of rows.</param>
    /// <param name="columns">The number of columns.</param>
    /// <param name="matrix">The modules.</param>
    /// <returns><see langword="true"/> when every module centre is inside the image.</returns>
    public static bool TrySample(BitImage bits, PerspectiveTransform transform, int rows, int columns, out ModuleMatrix matrix)
    {
        matrix = new ModuleMatrix(rows, columns);
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                PointF point = transform.Transform(column + 0.5, row + 0.5);
                int x = (int)MathF.Floor(point.X);
                int y = (int)MathF.Floor(point.Y);
                if (x < -1 || y < -1 || x > bits.Width || y > bits.Height || float.IsNaN(point.X) || float.IsNaN(point.Y))
                {
                    return false;
                }

                matrix[row, column] = bits[Math.Clamp(x, 0, bits.Width - 1), Math.Clamp(y, 0, bits.Height - 1)];
            }
        }

        return true;
    }
}
