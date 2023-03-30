// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using GeoJSON.Net.Feature;
using Newtonsoft.Json;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.ImageComparison;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SkiaSharp;
using Xunit;

namespace SixLabors.ImageSharp.Drawing.Tests.Drawing
{
    [GroupOutput("Drawing")]
    public class DrawingRobustnessTests
    {
        [Theory(Skip = "For local testing")]
        [WithSolidFilledImages(32, 32, "Black", PixelTypes.Rgba32)]
        public void CompareToSkiaResults_SmallCircle(TestImageProvider<Rgba32> provider)
        {
            var circle = new EllipsePolygon(16, 16, 10);

            CompareToSkiaResultsImpl(provider, circle);
        }

        [Theory(Skip = "For local testing")]
        [WithSolidFilledImages(64, 64, "Black", PixelTypes.Rgba32)]
        public void CompareToSkiaResults_StarCircle(TestImageProvider<Rgba32> provider)
        {
            var circle = new EllipsePolygon(32, 32, 30);
            var star = new Star(32, 32, 7, 10, 27);
            IPath shape = circle.Clip(star);

            CompareToSkiaResultsImpl(provider, shape);
        }

        private static void CompareToSkiaResultsImpl(TestImageProvider<Rgba32> provider, IPath shape)
        {
            using Image<Rgba32> image = provider.GetImage();
            image.Mutate(c => c.Fill(Color.White, shape));
            image.DebugSave(provider, "ImageSharp", appendPixelTypeToFileName: false, appendSourceFileOrDescription: false);

            using var bitmap = new SKBitmap(new SKImageInfo(image.Width, image.Height));

            using var skPath = new SKPath();

            foreach (ISimplePath loop in shape.Flatten())
            {
                ReadOnlySpan<SKPoint> points = MemoryMarshal.Cast<PointF, SKPoint>(loop.Points.Span);
                skPath.AddPoly(points.ToArray());
            }

            using var paint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Color = SKColors.White,
                IsAntialias = true,
            };

            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(new SKColor(0, 0, 0));
            canvas.DrawPath(skPath, paint);

            using var skResultImage =
                Image.LoadPixelData<Rgba32>(bitmap.GetPixelSpan(), image.Width, image.Height);
            skResultImage.DebugSave(
                provider,
                "SkiaSharp",
                appendPixelTypeToFileName: false,
                appendSourceFileOrDescription: false);

            ImageSimilarityReport<Rgba32, Rgba32> result = ImageComparer.Exact.CompareImagesOrFrames(image, skResultImage);
            throw new Exception(result.DifferencePercentageString);
        }

        [Theory(Skip = "For local testing")]
        [WithSolidFilledImages(3600, 2400, "Black", PixelTypes.Rgba32, TestImages.GeoJson.States, 16, 30, 30)]
        public void LargeGeoJson_Lines(TestImageProvider<Rgba32> provider, string geoJsonFile, int aa, float sx, float sy)
        {
            string jsonContent = File.ReadAllText(TestFile.GetInputFileFullPath(geoJsonFile));

            PointF[][] points = PolygonFactory.GetGeoJsonPoints(jsonContent, Matrix3x2.CreateScale(sx, sy));

            using Image<Rgba32> image = provider.GetImage();
            var options = new DrawingOptions()
            {
                GraphicsOptions = new GraphicsOptions() { Antialias = aa > 0, AntialiasSubpixelDepth = aa },
            };
            foreach (PointF[] loop in points)
            {
                image.Mutate(c => c.DrawLines(options, Color.White, 1.0f, loop));
            }

            string details = $"_{System.IO.Path.GetFileName(geoJsonFile)}_{sx}x{sy}_aa{aa}";

            image.DebugSave(
                provider,
                details,
                appendPixelTypeToFileName: false,
                appendSourceFileOrDescription: false);
        }

        [Theory]
        [WithSolidFilledImages(7200, 3300, "Black", PixelTypes.Rgba32)]
        public void LargeGeoJson_States_Fill(TestImageProvider<Rgba32> provider)
        {
            using Image<Rgba32> image = this.FillGeoJsonPolygons(provider, TestImages.GeoJson.States, 16, new Vector2(60), new Vector2(0, -1000));
            var comparer = ImageComparer.TolerantPercentage(0.001f);

            image.DebugSave(provider, appendPixelTypeToFileName: false, appendSourceFileOrDescription: false);
            image.CompareToReferenceOutput(comparer, provider, appendPixelTypeToFileName: false, appendSourceFileOrDescription: false);
        }

        private Image<Rgba32> FillGeoJsonPolygons(TestImageProvider<Rgba32> provider, string geoJsonFile, int aa, Vector2 scale, Vector2 pixelOffset)
        {
            string jsonContent = File.ReadAllText(TestFile.GetInputFileFullPath(geoJsonFile));

            PointF[][] points = PolygonFactory.GetGeoJsonPoints(jsonContent, Matrix3x2.CreateScale(scale) * Matrix3x2.CreateTranslation(pixelOffset));

            Image<Rgba32> image = provider.GetImage();
            var options = new DrawingOptions()
            {
                GraphicsOptions = new GraphicsOptions() { Antialias = aa > 0, AntialiasSubpixelDepth = aa },
            };
            var rnd = new Random(42);
            byte[] rgb = new byte[3];
            foreach (PointF[] loop in points)
            {
                rnd.NextBytes(rgb);

                var color = Color.FromRgb(rgb[0], rgb[1], rgb[2]);
                image.Mutate(c => c.FillPolygon(options, color, loop));
            }

            return image;
        }

        [Theory]
        [WithSolidFilledImages(400, 400, "Black", PixelTypes.Rgba32, 0)]
        [WithSolidFilledImages(6000, 6000, "Black", PixelTypes.Rgba32, 5500)]
        public void LargeGeoJson_Mississippi_Lines(TestImageProvider<Rgba32> provider, int pixelOffset)
        {
            string jsonContent = File.ReadAllText(TestFile.GetInputFileFullPath(TestImages.GeoJson.States));

            FeatureCollection features = JsonConvert.DeserializeObject<FeatureCollection>(jsonContent);

            Feature missisipiGeom = features.Features.Single(f => (string)f.Properties["NAME"] == "Mississippi");

            Matrix3x2 transform = Matrix3x2.CreateTranslation(-87, -54)
                            * Matrix3x2.CreateScale(60, 60)
                            * Matrix3x2.CreateTranslation(pixelOffset, pixelOffset);
            IReadOnlyList<PointF[]> points = PolygonFactory.GetGeoJsonPoints(missisipiGeom, transform);

            using Image<Rgba32> image = provider.GetImage();

            foreach (PointF[] loop in points)
            {
                image.Mutate(c => c.DrawLines(Color.White, 1.0f, loop));
            }

            // Very strict tolerance, since the image is sparse (relaxed on .NET Framework)
            ImageComparer comparer = TestEnvironment.IsFramework
                ? ImageComparer.TolerantPercentage(1e-3f)
                : ImageComparer.TolerantPercentage(1e-7f);

            string details = $"PixelOffset({pixelOffset})";
            image.DebugSave(provider, details, appendPixelTypeToFileName: false, appendSourceFileOrDescription: false);
            image.CompareToReferenceOutput(comparer, provider, testOutputDetails: details, appendPixelTypeToFileName: false, appendSourceFileOrDescription: false);
        }

        [Theory(Skip = "For local experiments only")]
        [InlineData(0)]
        [InlineData(5000)]
        [InlineData(9000)]
        public void Missisippi_Skia(int offset)
        {
            string jsonContent = File.ReadAllText(TestFile.GetInputFileFullPath(TestImages.GeoJson.States));

            FeatureCollection features = JsonConvert.DeserializeObject<FeatureCollection>(jsonContent);

            Feature missisipiGeom = features.Features.Single(f => (string)f.Properties["NAME"] == "Mississippi");

            Matrix3x2 transform = Matrix3x2.CreateTranslation(-87, -54)
                            * Matrix3x2.CreateScale(60, 60)
                            * Matrix3x2.CreateTranslation(offset, offset);
            IReadOnlyList<PointF[]> points = PolygonFactory.GetGeoJsonPoints(missisipiGeom, transform);

            var path = new SKPath();

            foreach (PointF[] pts in points.Where(p => p.Length > 2))
            {
                path.MoveTo(pts[0].X, pts[0].Y);

                for (int i = 0; i < pts.Length; i++)
                {
                    path.LineTo(pts[i].X, pts[i].Y);
                }

                path.LineTo(pts[0].X, pts[0].Y);
            }

            var imageInfo = new SKImageInfo(10000, 10000);

            using var paint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = SKColors.White,
                StrokeWidth = 1f,
                IsAntialias = true,
            };

            using var surface = SKSurface.Create(imageInfo);
            SKCanvas canvas = surface.Canvas;
            canvas.Clear(new SKColor(0, 0, 0));
            canvas.DrawPath(path, paint);

            string outDir = TestEnvironment.CreateOutputDirectory("Skia");
            string fn = System.IO.Path.Combine(outDir, $"Missisippi_Skia_{offset}.png");

            using SKImage image = surface.Snapshot();
            using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);

            using FileStream fs = File.Create(fn);
            data.SaveTo(fs);
        }
    }
}
