// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Adds extensions that allow the flood filling of polygon outlines without blending.
/// </summary>
public static class ClearPathExtensions
{
    /// <summary>
    /// Flood fills the image within the provided region defined by an <see cref="IPath"/> using the specified
    /// color without any blending.
    /// </summary>
    /// <param name="source">The source image processing context.</param>
    /// <param name="color">The color.</param>
    /// <param name="region">The <see cref="IPath"/> defining the region to fill.</param>
    /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
    public static IImageProcessingContext Clear(
        this IImageProcessingContext source,
        Color color,
        IPath region)
        => source.Clear(new SolidBrush(color), region);

    /// <summary>
    /// Flood fills the image within the provided region defined by an <see cref="IPath"/> using the specified color
    /// without any blending.
    /// </summary>
    /// <param name="source">The source image processing context.</param>
    /// <param name="options">The drawing options.</param>
    /// <param name="color">The color.</param>
    /// <param name="region">The <see cref="IPath"/> defining the region to fill.</param>
    /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
    public static IImageProcessingContext Clear(
        this IImageProcessingContext source,
        DrawingOptions options,
        Color color,
        IPath region)
        => source.Clear(options, new SolidBrush(color), region);

    /// <summary>
    /// Flood fills the image within the provided region defined by an <see cref="IPath"/> using the specified brush
    /// without any blending.
    /// </summary>
    /// <param name="source">The source image processing context.</param>
    /// <param name="brush">The brush.</param>
    /// <param name="region">The <see cref="IPath"/> defining the region to fill.</param>
    /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
    public static IImageProcessingContext Clear(
        this IImageProcessingContext source,
        Brush brush,
        IPath region)
        => source.Clear(source.GetDrawingOptions(), brush, region);

    /// <summary>
    /// Flood fills the image within the provided region defined by an <see cref="IPath"/> using the specified brush
    /// without any blending.
    /// </summary>
    /// <param name="source">The source image processing context.</param>
    /// <param name="options">The drawing options.</param>
    /// <param name="brush">The brush.</param>
    /// <param name="region">The <see cref="IPath"/> defining the region to fill.</param>
    /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
    public static IImageProcessingContext Clear(
        this IImageProcessingContext source,
        DrawingOptions options,
        Brush brush,
        IPath region)
        => source.Fill(options.CloneForClearOperation(), brush, region);
}
