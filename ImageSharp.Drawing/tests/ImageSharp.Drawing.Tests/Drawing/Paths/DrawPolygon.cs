// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Processors.Drawing;
using SixLabors.ImageSharp.Drawing.Tests.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Drawing.Paths;

public class DrawPolygon : BaseImageOperationsExtensionTest
{
    private readonly SolidPen pen = Pens.Solid(Color.HotPink, 2);
    private readonly PointF[] points =
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
    public void Pen()
    {
        this.operations.DrawPolygon(new DrawingOptions(), this.pen, this.points);

        DrawPathProcessor processor = this.Verify<DrawPathProcessor>();

        Assert.NotEqual(this.shapeOptions, processor.Options.ShapeOptions);
        this.VerifyPoints(this.points, processor.Path);
        Assert.Equal(this.pen, processor.Pen);
    }

    [Fact]
    public void PenDefaultOptions()
    {
        this.operations.DrawPolygon(this.pen, this.points);

        DrawPathProcessor processor = this.Verify<DrawPathProcessor>();

        Assert.Equal(this.shapeOptions, processor.Options.ShapeOptions);
        this.VerifyPoints(this.points, processor.Path);
        Assert.Equal(this.pen, processor.Pen);
    }

    [Fact]
    public void BrushAndThickness()
    {
        this.operations.DrawPolygon(new DrawingOptions(), this.pen.StrokeFill, 10, this.points);

        DrawPathProcessor processor = this.Verify<DrawPathProcessor>();

        Assert.NotEqual(this.shapeOptions, processor.Options.ShapeOptions);
        this.VerifyPoints(this.points, processor.Path);
        SolidPen processorPen = Assert.IsType<SolidPen>(processor.Pen);
        Assert.Equal(this.pen.StrokeFill, processorPen.StrokeFill);
        Assert.Equal(10, processorPen.StrokeWidth);
    }

    [Fact]
    public void BrushAndThicknessDefaultOptions()
    {
        this.operations.DrawPolygon(this.pen.StrokeFill, 10, this.points);

        DrawPathProcessor processor = this.Verify<DrawPathProcessor>();

        Assert.Equal(this.shapeOptions, processor.Options.ShapeOptions);
        this.VerifyPoints(this.points, processor.Path);
        SolidPen processorPen = Assert.IsType<SolidPen>(processor.Pen);
        Assert.Equal(this.pen.StrokeFill, processorPen.StrokeFill);
        Assert.Equal(10, processorPen.StrokeWidth);
    }

    [Fact]
    public void ColorAndThickness()
    {
        this.operations.DrawPolygon(new DrawingOptions(), Color.Red, 10, this.points);

        DrawPathProcessor processor = this.Verify<DrawPathProcessor>();

        Assert.NotEqual(this.shapeOptions, processor.Options.ShapeOptions);
        this.VerifyPoints(this.points, processor.Path);
        SolidBrush brush = Assert.IsType<SolidBrush>(processor.Pen.StrokeFill);
        SolidPen processorPen = Assert.IsType<SolidPen>(processor.Pen);
        Assert.Equal(Color.Red, brush.Color);
        Assert.Equal(10, processorPen.StrokeWidth);
    }

    [Fact]
    public void ColorAndThicknessDefaultOptions()
    {
        this.operations.DrawPolygon(Color.Red, 10, this.points);

        DrawPathProcessor processor = this.Verify<DrawPathProcessor>();

        Assert.Equal(this.shapeOptions, processor.Options.ShapeOptions);
        this.VerifyPoints(this.points, processor.Path);
        SolidBrush brush = Assert.IsType<SolidBrush>(processor.Pen.StrokeFill);
        Assert.Equal(Color.Red, brush.Color);
        SolidPen processorPen = Assert.IsType<SolidPen>(processor.Pen);
        Assert.Equal(10, processorPen.StrokeWidth);
    }

    [Fact]
    public void JointAndEndCapStyle()
    {
        this.operations.DrawPolygon(new DrawingOptions(), this.pen.StrokeFill, 10, this.points);

        DrawPathProcessor processor = this.Verify<DrawPathProcessor>();

        Assert.NotEqual(this.shapeOptions, processor.Options.ShapeOptions);
        this.VerifyPoints(this.points, processor.Path);
        SolidPen processorPen = Assert.IsType<SolidPen>(processor.Pen);
        Assert.Equal(this.pen.JointStyle, processorPen.JointStyle);
        Assert.Equal(this.pen.EndCapStyle, processorPen.EndCapStyle);
    }

    [Fact]
    public void JointAndEndCapStyleDefaultOptions()
    {
        this.operations.DrawPolygon(this.pen.StrokeFill, 10, this.points);

        DrawPathProcessor processor = this.Verify<DrawPathProcessor>();

        Assert.Equal(this.shapeOptions, processor.Options.ShapeOptions);
        this.VerifyPoints(this.points, processor.Path);
        SolidPen processorPen = Assert.IsType<SolidPen>(processor.Pen);
        Assert.Equal(this.pen.JointStyle, processorPen.JointStyle);
        Assert.Equal(this.pen.EndCapStyle, processorPen.EndCapStyle);
    }
}
