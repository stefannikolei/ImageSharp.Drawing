// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// A projective transform of the plane, which maps any four points onto any four points. The matrix readers map the module grid of a symbol onto the image
/// with it, so a symbol that the camera sees at an angle still samples at its module centres.
/// </summary>
internal readonly struct PerspectiveTransform
{
    private readonly double a11;
    private readonly double a12;
    private readonly double a13;
    private readonly double a21;
    private readonly double a22;
    private readonly double a23;
    private readonly double a31;
    private readonly double a32;
    private readonly double a33;

    /// <summary>
    /// Initializes a new instance of the <see cref="PerspectiveTransform"/> struct. A point (x, y) maps to ((a11 x + a21 y + a31) / d, (a12 x + a22 y + a32) /
    /// d) with d = a13 x + a23 y + a33.
    /// </summary>
    /// <param name="a11">The coefficient of x in the new x.</param>
    /// <param name="a21">The coefficient of y in the new x.</param>
    /// <param name="a31">The constant of the new x.</param>
    /// <param name="a12">The coefficient of x in the new y.</param>
    /// <param name="a22">The coefficient of y in the new y.</param>
    /// <param name="a32">The constant of the new y.</param>
    /// <param name="a13">The coefficient of x in the denominator.</param>
    /// <param name="a23">The coefficient of y in the denominator.</param>
    /// <param name="a33">The constant of the denominator.</param>
    private PerspectiveTransform(double a11, double a21, double a31, double a12, double a22, double a32, double a13, double a23, double a33)
    {
        this.a11 = a11;
        this.a12 = a12;
        this.a13 = a13;
        this.a21 = a21;
        this.a22 = a22;
        this.a23 = a23;
        this.a31 = a31;
        this.a32 = a32;
        this.a33 = a33;
    }

    /// <summary>
    /// Returns the transform that maps four points onto four other points, each quadrilateral in the same order around its edge.
    /// </summary>
    /// <param name="from">The four points to map.</param>
    /// <param name="to">The four points they map to.</param>
    /// <returns>The transform.</returns>
    public static PerspectiveTransform QuadrilateralToQuadrilateral(ReadOnlySpan<PointF> from, ReadOnlySpan<PointF> to)
        => SquareToQuadrilateral(to).Times(SquareToQuadrilateral(from).Adjoint());

    /// <summary>
    /// Maps a point.
    /// </summary>
    /// <param name="x">The x coordinate.</param>
    /// <param name="y">The y coordinate.</param>
    /// <returns>The mapped point.</returns>
    public PointF Transform(double x, double y)
    {
        double denominator = (this.a13 * x) + (this.a23 * y) + this.a33;
        return new PointF(
            (float)(((this.a11 * x) + (this.a21 * y) + this.a31) / denominator),
            (float)(((this.a12 * x) + (this.a22 * y) + this.a32) / denominator));
    }

    /// <summary>
    /// Returns the transform that maps the unit square (0, 0), (1, 0), (1, 1), (0, 1) onto four points.
    /// </summary>
    /// <param name="points">The four points.</param>
    /// <returns>The transform.</returns>
    private static PerspectiveTransform SquareToQuadrilateral(ReadOnlySpan<PointF> points)
    {
        double x0 = points[0].X;
        double y0 = points[0].Y;
        double x1 = points[1].X;
        double y1 = points[1].Y;
        double x2 = points[2].X;
        double y2 = points[2].Y;
        double x3 = points[3].X;
        double y3 = points[3].Y;
        double dx3 = x0 - x1 + x2 - x3;
        double dy3 = y0 - y1 + y2 - y3;
        if (Math.Abs(dx3) < 1e-9 && Math.Abs(dy3) < 1e-9)
        {
            // A parallelogram is an affine image of the square.
            return new PerspectiveTransform(x1 - x0, x2 - x1, x0, y1 - y0, y2 - y1, y0, 0, 0, 1);
        }

        double dx1 = x1 - x2;
        double dx2 = x3 - x2;
        double dy1 = y1 - y2;
        double dy2 = y3 - y2;
        double denominator = (dx1 * dy2) - (dx2 * dy1);
        double a13 = ((dx3 * dy2) - (dx2 * dy3)) / denominator;
        double a23 = ((dx1 * dy3) - (dx3 * dy1)) / denominator;
        return new PerspectiveTransform(
            x1 - x0 + (a13 * x1),
            x3 - x0 + (a23 * x3),
            x0,
            y1 - y0 + (a13 * y1),
            y3 - y0 + (a23 * y3),
            y0,
            a13,
            a23,
            1);
    }

    /// <summary>
    /// Returns the adjoint of the matrix, the transpose of its cofactors, which inverts the transform up to a scale that the projection divides out.
    /// </summary>
    /// <returns>The inverse transform.</returns>
    private PerspectiveTransform Adjoint()
        => new(
            (this.a22 * this.a33) - (this.a23 * this.a32),
            (this.a23 * this.a31) - (this.a21 * this.a33),
            (this.a21 * this.a32) - (this.a22 * this.a31),
            (this.a13 * this.a32) - (this.a12 * this.a33),
            (this.a11 * this.a33) - (this.a13 * this.a31),
            (this.a12 * this.a31) - (this.a11 * this.a32),
            (this.a12 * this.a23) - (this.a13 * this.a22),
            (this.a13 * this.a21) - (this.a11 * this.a23),
            (this.a11 * this.a22) - (this.a12 * this.a21));

    /// <summary>
    /// Returns the transform that applies another transform first and this one second.
    /// </summary>
    /// <param name="other">The transform to apply first.</param>
    /// <returns>The product.</returns>
    private PerspectiveTransform Times(PerspectiveTransform other)
        => new(
            (this.a11 * other.a11) + (this.a21 * other.a12) + (this.a31 * other.a13),
            (this.a11 * other.a21) + (this.a21 * other.a22) + (this.a31 * other.a23),
            (this.a11 * other.a31) + (this.a21 * other.a32) + (this.a31 * other.a33),
            (this.a12 * other.a11) + (this.a22 * other.a12) + (this.a32 * other.a13),
            (this.a12 * other.a21) + (this.a22 * other.a22) + (this.a32 * other.a23),
            (this.a12 * other.a31) + (this.a22 * other.a32) + (this.a32 * other.a33),
            (this.a13 * other.a11) + (this.a23 * other.a12) + (this.a33 * other.a13),
            (this.a13 * other.a21) + (this.a23 * other.a22) + (this.a33 * other.a23),
            (this.a13 * other.a31) + (this.a23 * other.a32) + (this.a33 * other.a33));
}
