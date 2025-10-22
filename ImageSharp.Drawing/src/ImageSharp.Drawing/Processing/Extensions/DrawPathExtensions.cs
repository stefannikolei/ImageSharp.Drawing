// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing.Processors.Drawing;

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Adds extensions that allow the drawing of polygon outlines.
/// </summary>
public static class DrawPathExtensions
{
    /// <summary>
    /// Draws the outline of the polygon with the provided pen.
    /// </summary>
    /// <param name="source">The source image processing context.</param>
    /// <param name="options">The options.</param>
    /// <param name="pen">The pen.</param>
    /// <param name="path">The path.</param>
    /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
    public static IImageProcessingContext Draw(
        this IImageProcessingContext source,
        DrawingOptions options,
        Pen pen,
        IPath path) =>
        source.ApplyProcessor(new DrawPathProcessor(options, pen, path));

    /// <summary>
    /// Draws the outline of the polygon with the provided pen.
    /// </summary>
    /// <param name="source">The source image processing context.</param>
    /// <param name="pen">The pen.</param>
    /// <param name="path">The path.</param>
    /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
    public static IImageProcessingContext Draw(this IImageProcessingContext source, Pen pen, IPath path) =>
        source.Draw(source.GetDrawingOptions(), pen, path);

    /// <summary>
    /// Draws the outline of the polygon with the provided brush at the provided thickness.
    /// </summary>
    /// <param name="source">The source image processing context.</param>
    /// <param name="options">The options.</param>
    /// <param name="brush">The brush.</param>
    /// <param name="thickness">The thickness.</param>
    /// <param name="path">The shape.</param>
    /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
    public static IImageProcessingContext Draw(
        this IImageProcessingContext source,
        DrawingOptions options,
        Brush brush,
        float thickness,
        IPath path) =>
        source.Draw(options, new SolidPen(brush, thickness), path);

    /// <summary>
    /// Draws the outline of the polygon with the provided brush at the provided thickness.
    /// </summary>
    /// <param name="source">The source image processing context.</param>
    /// <param name="brush">The brush.</param>
    /// <param name="thickness">The thickness.</param>
    /// <param name="path">The path.</param>
    /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
    public static IImageProcessingContext Draw(
        this IImageProcessingContext source,
        Brush brush,
        float thickness,
        IPath path) =>
        source.Draw(new SolidPen(brush, thickness), path);

    /// <summary>
    /// Draws the outline of the polygon with the provided brush at the provided thickness.
    /// </summary>
    /// <param name="source">The source image processing context.</param>
    /// <param name="options">The options.</param>
    /// <param name="color">The color.</param>
    /// <param name="thickness">The thickness.</param>
    /// <param name="path">The path.</param>
    /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
    public static IImageProcessingContext Draw(
        this IImageProcessingContext source,
        DrawingOptions options,
        Color color,
        float thickness,
        IPath path) =>
        source.Draw(options, new SolidBrush(color), thickness, path);

    /// <summary>
    /// Draws the outline of the polygon with the provided brush at the provided thickness.
    /// </summary>
    /// <param name="source">The source image processing context.</param>
    /// <param name="color">The color.</param>
    /// <param name="thickness">The thickness.</param>
    /// <param name="path">The path.</param>
    /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
    public static IImageProcessingContext Draw(
        this IImageProcessingContext source,
        Color color,
        float thickness,
        IPath path) =>
        source.Draw(new SolidBrush(color), thickness, path);
}
