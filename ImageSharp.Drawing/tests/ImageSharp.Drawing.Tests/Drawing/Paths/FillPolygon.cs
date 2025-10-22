// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Processors.Drawing;
using SixLabors.ImageSharp.Drawing.Tests.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Drawing.Paths;

public class FillPolygon : BaseImageOperationsExtensionTest
{
    private readonly Brush brush = Brushes.Solid(Color.HotPink);
    private readonly PointF[] path =
    [
        new PointF(10, 10),
        new PointF(10, 20),
        new PointF(20, 20),
        new PointF(25, 25),
        new PointF(25, 10)
    ];

    private void VerifyPoints(PointF[] expectedPoints, IPath path)
    {
        ISimplePath simplePath = Assert.Single(path.Flatten());
        Assert.True(simplePath.IsClosed);
        Assert.Equal(expectedPoints, simplePath.Points.ToArray());
    }

    [Fact]
    public void Brush()
    {
        this.operations.FillPolygon(new DrawingOptions(), this.brush, this.path);

        FillPathProcessor processor = this.Verify<FillPathProcessor>();

        Assert.NotEqual(this.shapeOptions, processor.Options.ShapeOptions);
        this.VerifyPoints(this.path, processor.Region);
        Assert.Equal(this.brush, processor.Brush);
    }

    [Fact]
    public void BrushDefaultOptions()
    {
        this.operations.FillPolygon(this.brush, this.path);

        FillPathProcessor processor = this.Verify<FillPathProcessor>();

        Assert.Equal(this.shapeOptions, processor.Options.ShapeOptions);
        this.VerifyPoints(this.path, processor.Region);
        Assert.Equal(this.brush, processor.Brush);
    }

    [Fact]
    public void ColorSet()
    {
        this.operations.FillPolygon(new DrawingOptions(), Color.Red, this.path);

        FillPathProcessor processor = this.Verify<FillPathProcessor>();

        Assert.NotEqual(this.shapeOptions, processor.Options.ShapeOptions);
        this.VerifyPoints(this.path, processor.Region);
        Assert.NotEqual(this.brush, processor.Brush);
        SolidBrush brush = Assert.IsType<SolidBrush>(processor.Brush);
        Assert.Equal(Color.Red, brush.Color);
    }

    [Fact]
    public void ColorAndThicknessDefaultOptions()
    {
        this.operations.FillPolygon(Color.Red, this.path);

        FillPathProcessor processor = this.Verify<FillPathProcessor>();

        Assert.Equal(this.shapeOptions, processor.Options.ShapeOptions);
        this.VerifyPoints(this.path, processor.Region);
        Assert.NotEqual(this.brush, processor.Brush);
        SolidBrush brush = Assert.IsType<SolidBrush>(processor.Brush);
        Assert.Equal(Color.Red, brush.Color);
    }
}
