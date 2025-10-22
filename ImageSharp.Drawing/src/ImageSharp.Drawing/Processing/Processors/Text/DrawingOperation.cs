// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Processing.Processors.Text;

internal struct DrawingOperation
{
    public Buffer2D<float> Map { get; set; }

    public IPath Path { get; set; }

    public byte RenderPass { get; set; }

    public Point RenderLocation { get; set; }

    public Brush Brush { get; internal set; }
}
