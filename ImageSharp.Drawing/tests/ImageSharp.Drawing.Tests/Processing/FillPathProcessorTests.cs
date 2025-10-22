// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using System.Reflection;
using Moq;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Processors.Drawing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing;

public class FillPathProcessorTests
{
    [Fact]
    public void FillOffCanvas()
    {
        Rectangle bounds = new(-100, -10, 10, 10);

        // Specifically not using RectangularPolygon here to ensure the FillPathProcessor is used.
        LinearLineSegment[] points =
        [
            new(new PointF(bounds.Left, bounds.Top), new PointF(bounds.Right, bounds.Top)),
            new(new PointF(bounds.Right, bounds.Top), new PointF(bounds.Right, bounds.Bottom)),
            new(new PointF(bounds.Right, bounds.Bottom), new PointF(bounds.Left, bounds.Bottom)),
            new(new PointF(bounds.Left, bounds.Bottom), new PointF(bounds.Left, bounds.Top))
        ];
        Path path = new(points);
        Mock<Brush> brush = new();
        GraphicsOptions options = new() { Antialias = true };
        FillPathProcessor processor = new(new DrawingOptions() { GraphicsOptions = options }, brush.Object, path);
        Image<Rgba32> img = new(10, 10);
        processor.Execute(img.Configuration, img, bounds);
    }

    [Fact]
    public void DrawOffCanvas()
    {
        using (Image<Rgba32> img = new(10, 10))
        {
            img.Mutate(x => x.DrawLine(
                new SolidPen(Color.Black, 10),
                new Vector2(-10, 5),
                new Vector2(20, 5)));
        }
    }

    [Fact]
    public void OtherShape()
    {
        Rectangle imageSize = new(0, 0, 500, 500);
        EllipsePolygon path = new(1, 1, 23);
        FillPathProcessor processor = new(
            new DrawingOptions()
            {
                GraphicsOptions = { Antialias = true }
            },
            Brushes.Solid(Color.Red),
            path);

        IImageProcessor<Rgba32> pixelProcessor = processor.CreatePixelSpecificProcessor<Rgba32>(null, null, imageSize);

        Assert.IsType<FillPathProcessor<Rgba32>>(pixelProcessor);
    }

    [Fact]
    public void RectangleFloatAndAntialias()
    {
        Rectangle imageSize = new(0, 0, 500, 500);
        RectangleF floatRect = new(10.5f, 10.5f, 400.6f, 400.9f);
        Rectangle expectedRect = new(10, 10, 400, 400);
        RectangularPolygon path = new(floatRect);
        FillPathProcessor processor = new(
            new DrawingOptions()
            {
                GraphicsOptions = { Antialias = true }
            },
            Brushes.Solid(Color.Red),
            path);

        IImageProcessor<Rgba32> pixelProcessor = processor.CreatePixelSpecificProcessor<Rgba32>(null, null, imageSize);

        Assert.IsType<FillPathProcessor<Rgba32>>(pixelProcessor);
    }

    [Fact]
    public void IntRectangle()
    {
        Rectangle imageSize = new(0, 0, 500, 500);
        Rectangle expectedRect = new(10, 10, 400, 400);
        RectangularPolygon path = new(expectedRect);
        FillPathProcessor processor = new(
            new DrawingOptions()
            {
                GraphicsOptions = { Antialias = true }
            },
            Brushes.Solid(Color.Red),
            path);

        IImageProcessor<Rgba32> pixelProcessor = processor.CreatePixelSpecificProcessor<Rgba32>(null, null, imageSize);

        FillProcessor<Rgba32> fill = Assert.IsType<FillProcessor<Rgba32>>(pixelProcessor);
        Assert.Equal(expectedRect, fill.GetProtectedValue<Rectangle>("SourceRectangle"));
    }

    [Fact]
    public void FloatRectAntialiasingOff()
    {
        Rectangle imageSize = new(0, 0, 500, 500);
        RectangleF floatRect = new(10.5f, 10.5f, 400.6f, 400.9f);
        Rectangle expectedRect = new(10, 10, 400, 400);
        RectangularPolygon path = new(floatRect);
        FillPathProcessor processor = new(
            new DrawingOptions()
            {
                GraphicsOptions = { Antialias = false }
            },
            Brushes.Solid(Color.Red),
            path);

        IImageProcessor<Rgba32> pixelProcessor = processor.CreatePixelSpecificProcessor<Rgba32>(null, null, imageSize);
        FillProcessor<Rgba32> fill = Assert.IsType<FillProcessor<Rgba32>>(pixelProcessor);

        Assert.Equal(expectedRect, fill.GetProtectedValue<Rectangle>("SourceRectangle"));
    }

    [Fact]
    public void DoesNotThrowForIssue928()
    {
        RectangleF rectText = new(0, 0, 2000, 2000);
        using (Image<Rgba32> img = new((int)rectText.Width, (int)rectText.Height))
        {
            img.Mutate(x => x.Fill(Color.Transparent));

            img.Mutate(
                ctx => ctx.DrawLine(
                    Color.Red,
                    0.984252f,
                    new PointF(104.762581f, 1074.99365f),
                    new PointF(104.758667f, 1075.01721f),
                    new PointF(104.757675f, 1075.04114f),
                    new PointF(104.759628f, 1075.065f),
                    new PointF(104.764488f, 1075.08838f),
                    new PointF(104.772186f, 1075.111f),
                    new PointF(104.782608f, 1075.13245f),
                    new PointF(104.782608f, 1075.13245f)));
        }
    }

    [Fact]
    public void DoesNotThrowFillingTriangle()
    {
        using (Image<Rgba32> image = new(28, 28))
        {
            Polygon path = new(
                new LinearLineSegment(new PointF(17.11f, 13.99659f), new PointF(14.01433f, 27.06201f)),
                new LinearLineSegment(new PointF(14.01433f, 27.06201f), new PointF(13.79267f, 14.00023f)),
                new LinearLineSegment(new PointF(13.79267f, 14.00023f), new PointF(17.11f, 13.99659f)));

            image.Mutate(ctx => ctx.Fill(Color.White, path));
        }
    }
}

internal static class ReflectionHelpers
{
    internal static T GetProtectedValue<T>(this object obj, string name)
        => (T)obj.GetType()
        .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy)
        .Single(x => x.Name == name)
        .GetValue(obj);
}
