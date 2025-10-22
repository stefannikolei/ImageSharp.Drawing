// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Adds extensions that allow the filling of collections of polygon outlines.
/// </summary>
public static class FillPathCollectionExtensions
{
    /// <summary>
    /// Flood fills the image in the shape of the provided polygon with the specified brush.
    /// </summary>
    /// <param name="source">The source image processing context.</param>
    /// <param name="options">The graphics options.</param>
    /// <param name="brush">The brush.</param>
    /// <param name="paths">The shapes.</param>
    /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
    public static IImageProcessingContext Fill(
        this IImageProcessingContext source,
        DrawingOptions options,
        Brush brush,
        IPathCollection paths)
    {
        foreach (IPath s in paths)
        {
            source.Fill(options, brush, s);
        }

        return source;
    }

    /// <summary>
    /// Flood fills the image in the shape of the provided polygon with the specified brush.
    /// </summary>
    /// <param name="source">The source image processing context.</param>
    /// <param name="brush">The brush.</param>
    /// <param name="paths">The paths.</param>
    /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
    public static IImageProcessingContext Fill(
        this IImageProcessingContext source,
        Brush brush,
        IPathCollection paths) =>
        source.Fill(source.GetDrawingOptions(), brush, paths);

    /// <summary>
    /// Flood fills the image in the shape of the provided polygon with the specified brush.
    /// </summary>
    /// <param name="source">The source image processing context.</param>
    /// <param name="options">The options.</param>
    /// <param name="color">The color.</param>
    /// <param name="paths">The paths.</param>
    /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
    public static IImageProcessingContext Fill(
        this IImageProcessingContext source,
        DrawingOptions options,
        Color color,
        IPathCollection paths) =>
        source.Fill(options, new SolidBrush(color), paths);

    /// <summary>
    /// Flood fills the image in the shape of the provided polygon with the specified brush.
    /// </summary>
    /// <param name="source">The source image processing context.</param>
    /// <param name="color">The color.</param>
    /// <param name="paths">The paths.</param>
    /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
    public static IImageProcessingContext Fill(
        this IImageProcessingContext source,
        Color color,
        IPathCollection paths) =>
        source.Fill(new SolidBrush(color), paths);
}
