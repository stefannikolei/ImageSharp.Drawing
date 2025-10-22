// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Text;

/// <summary>
/// rendering surface that Fonts can use to generate Shapes.
/// </summary>
internal class GlyphBuilder : BaseGlyphBuilder
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GlyphBuilder"/> class.
    /// </summary>
    public GlyphBuilder()
        : this(Vector2.Zero)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GlyphBuilder"/> class.
    /// </summary>
    /// <param name="origin">The origin.</param>
    public GlyphBuilder(Vector2 origin) => this.Builder.SetOrigin(origin);
}
