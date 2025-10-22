// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Tests.Drawing;

[GroupOutput("Drawing")]
public class FillComplexPolygonTests
{
    [Theory]
    [WithSolidFilledImages(300, 400, "Blue", PixelTypes.Rgba32, false, false)]
    [WithSolidFilledImages(300, 400, "Blue", PixelTypes.Rgba32, true, false)]
    [WithSolidFilledImages(300, 400, "Blue", PixelTypes.Rgba32, false, true)]
    public void ComplexPolygon_SolidFill<TPixel>(TestImageProvider<TPixel> provider, bool overlap, bool transparent)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Polygon simplePath = new(new LinearLineSegment(
            new Vector2(10, 10),
            new Vector2(200, 150),
            new Vector2(50, 300)));

        Polygon hole1 = new(new LinearLineSegment(
            new Vector2(37, 85),
            overlap ? new Vector2(130, 40) : new Vector2(93, 85),
            new Vector2(65, 137)));

        IPath clipped = simplePath.Clip(hole1);

        Color color = Color.HotPink;
        if (transparent)
        {
            color = color.WithAlpha(150 / 255F);
        }

        string testDetails = string.Empty;
        if (overlap)
        {
            testDetails += "_Overlap";
        }

        if (transparent)
        {
            testDetails += "_Transparent";
        }

        provider.RunValidatingProcessorTest(
            x => x.Fill(color, clipped),
            testDetails,
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);
    }
}
