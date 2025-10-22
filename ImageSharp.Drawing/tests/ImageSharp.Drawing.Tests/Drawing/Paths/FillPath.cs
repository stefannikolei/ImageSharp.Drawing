// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Processors.Drawing;
using SixLabors.ImageSharp.Drawing.Tests.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Drawing.Paths;

public class FillPath : BaseImageOperationsExtensionTest
{
    private readonly Brush brush = Brushes.Solid(Color.HotPink);
    private readonly IPath path = new Star(1, 10, 5, 23, 56);

    [Fact]
    public void Brush()
    {
        this.operations.Fill(new DrawingOptions(), this.brush, this.path);

        FillPathProcessor processor = this.Verify<FillPathProcessor>();

        Assert.NotEqual(this.shapeOptions, processor.Options.ShapeOptions);
        Assert.Equal(this.path, processor.Region);
        Assert.Equal(this.brush, processor.Brush);
    }

    [Fact]
    public void BrushDefaultOptions()
    {
        this.operations.Fill(this.brush, this.path);

        FillPathProcessor processor = this.Verify<FillPathProcessor>();

        Assert.Equal(this.shapeOptions, processor.Options.ShapeOptions);
        Assert.Equal(this.path, processor.Region);
        Assert.Equal(this.brush, processor.Brush);
    }

    [Fact]
    public void ColorSet()
    {
        this.operations.Fill(new DrawingOptions(), Color.Red, this.path);

        FillPathProcessor processor = this.Verify<FillPathProcessor>();

        Assert.NotEqual(this.shapeOptions, processor.Options.ShapeOptions);
        Assert.Equal(this.path, processor.Region);
        Assert.NotEqual(this.brush, processor.Brush);
        SolidBrush brush = Assert.IsType<SolidBrush>(processor.Brush);
        Assert.Equal(Color.Red, brush.Color);
    }

    [Fact]
    public void ColorAndThicknessDefaultOptions()
    {
        this.operations.Fill(Color.Red, this.path);

        FillPathProcessor processor = this.Verify<FillPathProcessor>();

        Assert.Equal(this.shapeOptions, processor.Options.ShapeOptions);
        Assert.Equal(this.path, processor.Region);
        Assert.NotEqual(this.brush, processor.Brush);
        SolidBrush brush = Assert.IsType<SolidBrush>(processor.Brush);
        Assert.Equal(Color.Red, brush.Color);
    }
}
