// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Modes to repeat a gradient.
/// </summary>
public enum GradientRepetitionMode
{
    /// <summary>
    /// Don't repeat, keep the color of start and end beyond those points stable.
    /// </summary>
    None,

    /// <summary>
    /// Repeat the gradient.
    /// If it's a black-white gradient, with Repeat it will be Black->{gray}->White|Black->{gray}->White|...
    /// </summary>
    Repeat,

    /// <summary>
    /// Reflect the gradient.
    /// Similar to <see cref="Repeat"/>, but each other repetition uses inverse order of <see cref="ColorStop"/>s.
    /// Used on a Black-White gradient, Reflect leads to Black->{gray}->White->{gray}->White...
    /// </summary>
    Reflect,

    /// <summary>
    /// With DontFill a gradient does not touch any pixel beyond it's borders.
    /// For the <see cref="LinearGradientBrush"/> this is beyond the orthogonal through start and end,
    /// For <see cref="RadialGradientBrush" /> and <see cref="EllipticGradientBrush" /> it's beyond 1.0.
    /// </summary>
    DontFill
}
