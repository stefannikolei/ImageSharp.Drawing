// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Provides an implementation of a brush for painting linear gradients within areas.
/// Supported right now:
/// - a set of colors in relative distances to each other.
/// </summary>
public sealed class LinearGradientBrush : GradientBrush
{
    private readonly PointF p1;
    private readonly PointF p2;

    /// <summary>
    /// Initializes a new instance of the <see cref="LinearGradientBrush"/> class.
    /// </summary>
    /// <param name="p1">Start point</param>
    /// <param name="p2">End point</param>
    /// <param name="repetitionMode">defines how colors are repeated.</param>
    /// <param name="colorStops"><inheritdoc /></param>
    public LinearGradientBrush(
        PointF p1,
        PointF p2,
        GradientRepetitionMode repetitionMode,
        params ColorStop[] colorStops)
        : base(repetitionMode, colorStops)
    {
        this.p1 = p1;
        this.p2 = p2;
    }

    /// <inheritdoc/>
    public override bool Equals(Brush? other)
    {
        if (other is LinearGradientBrush brush)
        {
            return base.Equals(other)
                && this.p1.Equals(brush.p1)
                && this.p2.Equals(brush.p2);
        }

        return false;
    }

    /// <inheritdoc />
    public override BrushApplicator<TPixel> CreateApplicator<TPixel>(
        Configuration configuration,
        GraphicsOptions options,
        ImageFrame<TPixel> source,
        RectangleF region) =>
        new LinearGradientBrushApplicator<TPixel>(
            configuration,
            options,
            source,
            this.p1,
            this.p2,
            this.ColorStops,
            this.RepetitionMode);

    /// <inheritdoc/>
    public override int GetHashCode()
        => HashCode.Combine(base.GetHashCode(), this.p1, this.p2);

    /// <summary>
    /// The linear gradient brush applicator.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    private sealed class LinearGradientBrushApplicator<TPixel> : GradientBrushApplicator<TPixel>
        where TPixel : unmanaged, IPixel<TPixel>
    {
        private readonly PointF start;

        private readonly PointF end;

        /// <summary>
        /// the vector along the gradient, x component
        /// </summary>
        private readonly float alongX;

        /// <summary>
        /// the vector along the gradient, y component
        /// </summary>
        private readonly float alongY;

        /// <summary>
        /// the vector perpendicular to the gradient, y component
        /// </summary>
        private readonly float acrossY;

        /// <summary>
        /// the vector perpendicular to the gradient, x component
        /// </summary>
        private readonly float acrossX;

        /// <summary>
        /// the result of <see cref="alongX"/>^2 + <see cref="alongY"/>^2
        /// </summary>
        private readonly float alongsSquared;

        /// <summary>
        /// the length of the defined gradient (between source and end)
        /// </summary>
        private readonly float length;

        /// <summary>
        /// Initializes a new instance of the <see cref="LinearGradientBrushApplicator{TPixel}" /> class.
        /// </summary>
        /// <param name="configuration">The configuration instance to use when performing operations.</param>
        /// <param name="options">The graphics options.</param>
        /// <param name="source">The source image.</param>
        /// <param name="start">The start point of the gradient.</param>
        /// <param name="end">The end point of the gradient.</param>
        /// <param name="colorStops">A tuple list of colors and their respective position between 0 and 1 on the line.</param>
        /// <param name="repetitionMode">Defines how the gradient colors are repeated.</param>
        public LinearGradientBrushApplicator(
            Configuration configuration,
            GraphicsOptions options,
            ImageFrame<TPixel> source,
            PointF start,
            PointF end,
            ColorStop[] colorStops,
            GradientRepetitionMode repetitionMode)
            : base(configuration, options, source, colorStops, repetitionMode)
        {
            this.start = start;
            this.end = end;

            // the along vector:
            this.alongX = this.end.X - this.start.X;
            this.alongY = this.end.Y - this.start.Y;

            // the cross vector:
            this.acrossX = this.alongY;
            this.acrossY = -this.alongX;

            // some helpers:
            this.alongsSquared = (this.alongX * this.alongX) + (this.alongY * this.alongY);
            this.length = MathF.Sqrt(this.alongsSquared);
        }

        protected override float PositionOnGradient(float x, float y)
        {
            if (this.acrossX == 0)
            {
                return (x - this.start.X) / (this.end.X - this.start.X);
            }
            else if (this.acrossY == 0)
            {
                return (y - this.start.Y) / (this.end.Y - this.start.Y);
            }
            else
            {
                float deltaX = x - this.start.X;
                float deltaY = y - this.start.Y;
                float k = ((this.alongY * deltaX) - (this.alongX * deltaY)) / this.alongsSquared;

                // point on the line:
                float x4 = x - (k * this.alongY);
                float y4 = y + (k * this.alongX);

                // get distance from (x4,y4) to start
                float distance = MathF.Sqrt(MathF.Pow(x4 - this.start.X, 2) + MathF.Pow(y4 - this.start.Y, 2));

                // get and return ratio
                return distance / this.length;
            }
        }
    }
}
