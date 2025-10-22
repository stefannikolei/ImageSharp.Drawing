// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Adds extensions that allow the drawing of rectangles.
/// </summary>
public static class DrawRectangleExtensions
{
    /// <summary>
    /// Draws the outline of the rectangle with the provided pen.
    /// </summary>
    /// <param name="source">The source image processing context.</param>
    /// <param name="options">The options.</param>
    /// <param name="pen">The pen.</param>
    /// <param name="shape">The shape.</param>
    /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
    public static IImageProcessingContext Draw(
        this IImageProcessingContext source,
        DrawingOptions options,
        Pen pen,
        RectangleF shape) =>
        source.Draw(options, pen, new RectangularPolygon(shape.X, shape.Y, shape.Width, shape.Height));

    /// <summary>
    /// Draws the outline of the rectangle with the provided pen.
    /// </summary>
    /// <param name="source">The source image processing context.</param>
    /// <param name="pen">The pen.</param>
    /// <param name="shape">The shape.</param>
    /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
    public static IImageProcessingContext Draw(this IImageProcessingContext source, Pen pen, RectangleF shape) =>
        source.Draw(source.GetDrawingOptions(), pen, shape);

    /// <summary>
    /// Draws the outline of the rectangle with the provided brush at the provided thickness.
    /// </summary>
    /// <param name="source">The source image processing context.</param>
    /// <param name="options">The options.</param>
    /// <param name="brush">The brush.</param>
    /// <param name="thickness">The thickness.</param>
    /// <param name="shape">The shape.</param>
    /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
    public static IImageProcessingContext Draw(
        this IImageProcessingContext source,
        DrawingOptions options,
        Brush brush,
        float thickness,
        RectangleF shape) =>
        source.Draw(options, new SolidPen(brush, thickness), shape);

    /// <summary>
    /// Draws the outline of the rectangle with the provided brush at the provided thickness.
    /// </summary>
    /// <param name="source">The source image processing context.</param>
    /// <param name="brush">The brush.</param>
    /// <param name="thickness">The thickness.</param>
    /// <param name="shape">The shape.</param>
    /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
    public static IImageProcessingContext Draw(
        this IImageProcessingContext source,
        Brush brush,
        float thickness,
        RectangleF shape) =>
        source.Draw(new SolidPen(brush, thickness), shape);

    /// <summary>
    /// Draws the outline of the rectangle with the provided brush at the provided thickness.
    /// </summary>
    /// <param name="source">The source image processing context.</param>
    /// <param name="options">The options.</param>
    /// <param name="color">The color.</param>
    /// <param name="thickness">The thickness.</param>
    /// <param name="shape">The shape.</param>
    /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
    public static IImageProcessingContext Draw(
        this IImageProcessingContext source,
        DrawingOptions options,
        Color color,
        float thickness,
        RectangleF shape) =>
        source.Draw(options, new SolidBrush(color), thickness, shape);

    /// <summary>
    /// Draws the outline of the rectangle with the provided brush at the provided thickness.
    /// </summary>
    /// <param name="source">The source image processing context.</param>
    /// <param name="color">The color.</param>
    /// <param name="thickness">The thickness.</param>
    /// <param name="shape">The shape.</param>
    /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
    public static IImageProcessingContext Draw(
        this IImageProcessingContext source,
        Color color,
        float thickness,
        RectangleF shape) =>
        source.Draw(new SolidBrush(color), thickness, shape);
}
