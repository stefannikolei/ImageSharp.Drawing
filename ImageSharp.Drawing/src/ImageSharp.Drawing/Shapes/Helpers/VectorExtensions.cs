// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// Extensions on arrays.
/// </summary>
internal static class VectorExtensions
{
    /// <summary>
    /// Merges the specified source2.
    /// </summary>
    /// <param name="source1">The source1.</param>
    /// <param name="source2">The source2.</param>
    /// <param name="threshold">The threshold.</param>
    /// <returns>
    /// the Merged arrays
    /// </returns>
    public static bool Equivalent(this PointF source1, PointF source2, float threshold)
    {
        Vector2 abs = Vector2.Abs(source1 - source2);

        return abs.X < threshold && abs.Y < threshold;
    }

    /// <summary>
    /// Merges the specified source2.
    /// </summary>
    /// <param name="source1">The source1.</param>
    /// <param name="source2">The source2.</param>
    /// <param name="threshold">The threshold.</param>
    /// <returns>
    /// the Merged arrays
    /// </returns>
    public static bool Equivalent(this Vector2 source1, Vector2 source2, float threshold)
    {
        Vector2 abs = Vector2.Abs(source1 - source2);

        return abs.X < threshold && abs.Y < threshold;
    }
}
