// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// A shape made up of a single closed path made up of one of more <see cref="ILineSegment"/>s
/// </summary>
public sealed class Star : Polygon
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Star" /> class.
    /// </summary>
    /// <param name="location">The location the center of the polygon will be placed.</param>
    /// <param name="prongs">The number of points the <see cref="Star" /> should have.</param>
    /// <param name="innerRadii">The inner radii.</param>
    /// <param name="outerRadii">The outer radii.</param>
    /// <param name="angle">The angle of rotation in Radians</param>
    public Star(PointF location, int prongs, float innerRadii, float outerRadii, float angle)
        : base(CreateSegment(location, innerRadii, outerRadii, prongs, angle))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Star" /> class.
    /// </summary>
    /// <param name="location">The location the center of the polygon will be placed.</param>
    /// <param name="prongs">The number of vertices the <see cref="Star" /> should have.</param>
    /// <param name="innerRadii">The inner radii.</param>
    /// <param name="outerRadii">The outer radii.</param>
    public Star(PointF location, int prongs, float innerRadii, float outerRadii)
        : this(location, prongs, innerRadii, outerRadii, 0)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Star" /> class.
    /// </summary>
    /// <param name="x">The x-coordinate of the center of the polygon.</param>
    /// <param name="y">The y-coordinate of the center of the polygon.</param>
    /// <param name="prongs">The number of vertices the <see cref="RegularPolygon" /> should have.</param>
    /// <param name="innerRadii">The inner radii.</param>
    /// <param name="outerRadii">The outer radii.</param>
    /// <param name="angle">The angle of rotation in Radians</param>
    public Star(float x, float y, int prongs, float innerRadii, float outerRadii, float angle)
        : this(new PointF(x, y), prongs, innerRadii, outerRadii, angle)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Star" /> class.
    /// </summary>
    /// <param name="x">The x-coordinate of the center of the polygon.</param>
    /// <param name="y">The y-coordinate of the center of the polygon.</param>
    /// <param name="prongs">The number of vertices the <see cref="RegularPolygon" /> should have.</param>
    /// <param name="innerRadii">The inner radii.</param>
    /// <param name="outerRadii">The outer radii.</param>
    public Star(float x, float y, int prongs, float innerRadii, float outerRadii)
        : this(new PointF(x, y), prongs, innerRadii, outerRadii)
    {
    }

    private static LinearLineSegment CreateSegment(Vector2 location, float innerRadii, float outerRadii, int prongs, float angle)
    {
        Guard.MustBeGreaterThan(prongs, 2, nameof(prongs));
        Guard.MustBeGreaterThan(innerRadii, 0, nameof(innerRadii));
        Guard.MustBeGreaterThan(outerRadii, 0, nameof(outerRadii));

        Vector2 distanceVectorInner = new(0, innerRadii);
        Vector2 distanceVectorOuter = new(0, outerRadii);

        int vertices = prongs * 2;
        float anglePerSegments = (float)(2 * Math.PI / vertices);
        float current = angle;
        PointF[] points = new PointF[vertices];
        Vector2 distance = distanceVectorInner;
        for (int i = 0; i < vertices; i++)
        {
            if (distance == distanceVectorInner)
            {
                distance = distanceVectorOuter;
            }
            else
            {
                distance = distanceVectorInner;
            }

            Vector2 rotated = Vector2.Transform(distance, Matrix3x2.CreateRotation(current));

            points[i] = rotated + location;

            current += anglePerSegments;
        }

        return new LinearLineSegment(points);
    }
}
