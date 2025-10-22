// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Collections;
using System.Numerics;

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// A aggregate of <see cref="IPath"/>s to apply common operations to them.
/// </summary>
/// <seealso cref="IPath" />
public class PathCollection : IPathCollection
{
    private readonly IPath[] paths;

    /// <summary>
    /// Initializes a new instance of the <see cref="PathCollection"/> class.
    /// </summary>
    /// <param name="paths">The collection of paths</param>
    public PathCollection(IEnumerable<IPath> paths)
        : this(paths.ToArray())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PathCollection"/> class.
    /// </summary>
    /// <param name="paths">The collection of paths</param>
    public PathCollection(params IPath[] paths)
    {
        this.paths = paths ?? throw new ArgumentNullException(nameof(paths));

        if (this.paths.Length == 0)
        {
            this.Bounds = new RectangleF(0, 0, 0, 0);
        }
        else
        {
            float minX, minY, maxX, maxY;
            minX = minY = float.MaxValue;
            maxX = maxY = float.MinValue;

            foreach (IPath path in this.paths)
            {
                minX = Math.Min(path.Bounds.Left, minX);
                minY = Math.Min(path.Bounds.Top, minY);
                maxX = Math.Max(path.Bounds.Right, maxX);
                maxY = Math.Max(path.Bounds.Bottom, maxY);
            }

            this.Bounds = new RectangleF(minX, minY, maxX - minX, maxY - minY);
        }
    }

    /// <inheritdoc />
    public RectangleF Bounds { get; }

    /// <inheritdoc />
    public IEnumerator<IPath> GetEnumerator() => ((IEnumerable<IPath>)this.paths).GetEnumerator();

    /// <inheritdoc />
    public IPathCollection Transform(Matrix3x2 matrix)
    {
        IPath[] result = new IPath[this.paths.Length];

        for (int i = 0; i < this.paths.Length && i < result.Length; i++)
        {
            result[i] = this.paths[i].Transform(matrix);
        }

        return new PathCollection(result);
    }

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<IPath>)this.paths).GetEnumerator();
}
