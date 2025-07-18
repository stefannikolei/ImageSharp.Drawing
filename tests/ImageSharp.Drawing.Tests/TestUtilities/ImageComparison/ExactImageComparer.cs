// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Tests.TestUtilities.ImageComparison;

public class ExactImageComparer : ImageComparer
{
    public static ExactImageComparer Instance { get; } = new();

    public override ImageSimilarityReport<TPixelA, TPixelB> CompareImagesOrFrames<TPixelA, TPixelB>(
        int index,
        ImageFrame<TPixelA> expected,
        ImageFrame<TPixelB> actual)
    {
        if (expected.Size != actual.Size)
        {
            throw new InvalidOperationException("Calling ImageComparer is invalid when dimensions mismatch!");
        }

        int width = actual.Width;

        // TODO: Comparing through Rgba64 may not be robust enough because of the existence of super high precision pixel types.
        Rgba64[] aBuffer = new Rgba64[width];
        Rgba64[] bBuffer = new Rgba64[width];

        List<PixelDifference> differences = [];
        Configuration configuration = expected.Configuration;
        Buffer2D<TPixelA> expectedBuffer = expected.PixelBuffer;
        Buffer2D<TPixelB> actualBuffer = actual.PixelBuffer;

        for (int y = 0; y < actual.Height; y++)
        {
            Span<TPixelA> aSpan = expectedBuffer.DangerousGetRowSpan(y);
            Span<TPixelB> bSpan = actualBuffer.DangerousGetRowSpan(y);

            PixelOperations<TPixelA>.Instance.ToRgba64(configuration, aSpan, aBuffer);
            PixelOperations<TPixelB>.Instance.ToRgba64(configuration, bSpan, bBuffer);

            for (int x = 0; x < width; x++)
            {
                Rgba64 aPixel = aBuffer[x];
                Rgba64 bPixel = bBuffer[x];

                if (aPixel != bPixel)
                {
                    PixelDifference diff = new(new Point(x, y), aPixel, bPixel);
                    differences.Add(diff);
                }
            }
        }

        return new ImageSimilarityReport<TPixelA, TPixelB>(index, expected, actual, differences);
    }
}
