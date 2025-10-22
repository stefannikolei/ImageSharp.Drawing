// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Reflection;

namespace SixLabors.ImageSharp.Drawing.Tests;

/// <summary>
/// Triggers passing <see cref="TestImageProvider{TPixel}"/> instances which produce a blank image of size width * height.
/// One <see cref="TestImageProvider{TPixel}"/> instance will be passed for each the pixel format defined by the pixelTypes parameter
/// </summary>
public class WithTestPatternImageAttribute : ImageDataAttributeBase
{
    /// <summary>
    /// Triggers passing an <see cref="TestImageProvider{TPixel}"/> that produces a test pattern image of size width * height
    /// </summary>
    /// <param name="width">The required width</param>
    /// <param name="height">The required height</param>
    /// <param name="pixelTypes">The requested parameter</param>
    /// <param name="additionalParameters">Additional theory parameter values</param>
    public WithTestPatternImageAttribute(int width, int height, PixelTypes pixelTypes, params object[] additionalParameters)
        : this(null, width, height, pixelTypes, additionalParameters)
    {
    }

    /// <summary>
    /// Triggers passing an <see cref="TestImageProvider{TPixel}"/> that produces a test pattern image of size width * height
    /// </summary>
    /// <param name="memberData">The member data to apply to theories</param>
    /// <param name="width">The required width</param>
    /// <param name="height">The required height</param>
    /// <param name="pixelTypes">The requested parameter</param>
    /// <param name="additionalParameters">Additional theory parameter values</param>
    public WithTestPatternImageAttribute(string memberData, int width, int height, PixelTypes pixelTypes, params object[] additionalParameters)
        : base(memberData, pixelTypes, additionalParameters)
    {
        this.Width = width;
        this.Height = height;
    }

    /// <summary>
    /// Gets the width
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Gets the height
    /// </summary>
    public int Height { get; }

    protected override string GetFactoryMethodName(MethodInfo testMethod) => "TestPattern";

    protected override object[] GetFactoryMethodArgs(MethodInfo testMethod, Type factoryType) => [this.Width, this.Height
    ];
}
