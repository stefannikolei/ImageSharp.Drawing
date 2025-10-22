// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.ReferenceCodecs;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using Xunit.Abstractions;
using IOPath = System.IO.Path;

// ReSharper disable InconsistentNaming
namespace SixLabors.ImageSharp.Drawing.Tests;

public class TestEnvironmentTests
{
    public TestEnvironmentTests(ITestOutputHelper output)
        => this.Output = output;

    private ITestOutputHelper Output { get; }

    private void CheckPath(string path)
    {
        this.Output.WriteLine(path);
        Assert.True(Directory.Exists(path));
    }

    [Fact]
    public void SolutionDirectoryFullPath()
        => this.CheckPath(TestEnvironment.SolutionDirectoryFullPath);

    [Fact]
    public void InputImagesDirectoryFullPath()
        => this.CheckPath(TestEnvironment.InputImagesDirectoryFullPath);

    [Fact]
    public void ExpectedOutputDirectoryFullPath()
        => this.CheckPath(TestEnvironment.ReferenceOutputDirectoryFullPath);

    [Fact]
    public void GetReferenceOutputFileName()
    {
        string actual = IOPath.Combine(TestEnvironment.ActualOutputDirectoryFullPath, @"foo\bar\lol.jpeg");
        string expected = TestEnvironment.GetReferenceOutputFileName(actual);

        this.Output.WriteLine(expected);
        Assert.Contains(TestEnvironment.ReferenceOutputDirectoryFullPath, expected);
    }

    [Theory]
    [InlineData("lol/foo.png", typeof(SystemDrawingReferenceEncoder))]
    [InlineData("lol/Rofl.bmp", typeof(SystemDrawingReferenceEncoder))]
    [InlineData("lol/Baz.JPG", typeof(JpegEncoder))]
    [InlineData("lol/Baz.gif", typeof(GifEncoder))]
    public void GetReferenceEncoder_ReturnsCorrectEncoders_Windows(string fileName, Type expectedEncoderType)
    {
        if (!TestEnvironment.IsWindows)
        {
            return;
        }

        IImageEncoder encoder = TestEnvironment.GetReferenceEncoder(fileName);
        Assert.IsType(expectedEncoderType, encoder);
    }

    [Theory]
    [InlineData("lol/foo.png", typeof(MagickReferenceDecoder))]
    [InlineData("lol/Rofl.bmp", typeof(SystemDrawingReferenceDecoder))]
    [InlineData("lol/Baz.JPG", typeof(JpegDecoder))]
    [InlineData("lol/Baz.gif", typeof(GifDecoder))]
    public void GetReferenceDecoder_ReturnsCorrectDecoders_Windows(string fileName, Type expectedDecoderType)
    {
        if (!TestEnvironment.IsWindows)
        {
            return;
        }

        IImageDecoder decoder = TestEnvironment.GetReferenceDecoder(fileName);
        Assert.IsType(expectedDecoderType, decoder);
    }

    [Theory]
    [InlineData("lol/foo.png", typeof(PngEncoder))]
    [InlineData("lol/Rofl.bmp", typeof(BmpEncoder))]
    [InlineData("lol/Baz.JPG", typeof(JpegEncoder))]
    [InlineData("lol/Baz.gif", typeof(GifEncoder))]
    public void GetReferenceEncoder_ReturnsCorrectEncoders_Linux(string fileName, Type expectedEncoderType)
    {
        if (!TestEnvironment.IsLinux)
        {
            return;
        }

        IImageEncoder encoder = TestEnvironment.GetReferenceEncoder(fileName);
        Assert.IsType(expectedEncoderType, encoder);
    }

    [Theory]
    [InlineData("lol/foo.png", typeof(MagickReferenceDecoder))]
    [InlineData("lol/Rofl.bmp", typeof(MagickReferenceDecoder))]
    [InlineData("lol/Baz.JPG", typeof(JpegDecoder))]
    [InlineData("lol/Baz.gif", typeof(GifDecoder))]
    public void GetReferenceDecoder_ReturnsCorrectDecoders_Linux(string fileName, Type expectedDecoderType)
    {
        if (!TestEnvironment.IsLinux)
        {
            return;
        }

        IImageDecoder decoder = TestEnvironment.GetReferenceDecoder(fileName);
        Assert.IsType(expectedDecoderType, decoder);
    }
}
