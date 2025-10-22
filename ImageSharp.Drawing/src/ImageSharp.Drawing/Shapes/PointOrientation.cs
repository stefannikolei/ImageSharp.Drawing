// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// Represents the orientation of a point from a line.
/// </summary>
internal enum PointOrientation
{
    /// <summary>
    /// The point is collinear.
    /// </summary>
    Collinear = 0,

    /// <summary>
    /// The point is clockwise.
    /// </summary>
    Clockwise = 1,

    /// <summary>
    /// The point is counter-clockwise.
    /// </summary>
    Counterclockwise = 2
}
