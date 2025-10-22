// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Processors.Drawing;
using SixLabors.ImageSharp.Drawing.Tests.Processing;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities;

namespace SixLabors.ImageSharp.Drawing.Tests.Drawing.Paths;

public class ClearRectangle : BaseImageOperationsExtensionTest
{
    private readonly Brush brush = Brushes.Solid(Color.HotPink);
    private RectangleF rectangle = new(10, 10, 20, 20);

    private RectangularPolygon RectanglePolygon => new(this.rectangle);

    [Fact]
    public void Brush()
    {
        this.operations.Clear(new DrawingOptions(), this.brush, this.rectangle);

        FillPathProcessor processor = this.Verify<FillPathProcessor>();

        Assert.NotEqual(this.shapeOptions, processor.Options.ShapeOptions);
        Assert.True(RectangularPolygonValueComparer.Equals(this.RectanglePolygon, processor.Region));
        Assert.Equal(this.brush, processor.Brush);
    }

    [Fact]
    public void BrushDefaultOptions()
    {
        this.operations.Clear(this.brush, this.rectangle);

        FillPathProcessor processor = this.Verify<FillPathProcessor>();

        Assert.Equal(this.shapeOptions, processor.Options.ShapeOptions);
        Assert.True(RectangularPolygonValueComparer.Equals(this.RectanglePolygon, processor.Region));
        Assert.Equal(this.brush, processor.Brush);
    }

    [Fact]
    public void ColorSet()
    {
        this.operations.Clear(new DrawingOptions(), Color.Red, this.rectangle);

        FillPathProcessor processor = this.Verify<FillPathProcessor>();

        Assert.NotEqual(this.shapeOptions, processor.Options.ShapeOptions);
        Assert.True(RectangularPolygonValueComparer.Equals(this.RectanglePolygon, processor.Region));
        Assert.NotEqual(this.brush, processor.Brush);
        SolidBrush brush = Assert.IsType<SolidBrush>(processor.Brush);
        Assert.Equal(Color.Red, brush.Color);
    }

    [Fact]
    public void ColorAndThicknessDefaultOptions()
    {
        this.operations.Clear(Color.Red, this.rectangle);

        FillPathProcessor processor = this.Verify<FillPathProcessor>();

        Assert.Equal(this.shapeOptions, processor.Options.ShapeOptions);
        Assert.True(RectangularPolygonValueComparer.Equals(this.RectanglePolygon, processor.Region));
        Assert.NotEqual(this.brush, processor.Brush);
        SolidBrush brush = Assert.IsType<SolidBrush>(processor.Brush);
        Assert.Equal(Color.Red, brush.Color);
    }
}
