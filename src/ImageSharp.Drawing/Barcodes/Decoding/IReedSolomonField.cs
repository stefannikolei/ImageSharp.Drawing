// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The arithmetic of a finite field for <see cref="ReedSolomonDecoder"/>. The elements are the integers 0 to <see cref="Order"/>.
/// </summary>
internal interface IReedSolomonField
{
    /// <summary>
    /// Gets the order of the multiplicative group: the number of elements less one.
    /// </summary>
    int Order { get; }

    /// <summary>
    /// Returns the sum of two elements.
    /// </summary>
    /// <param name="a">The first element.</param>
    /// <param name="b">The second element.</param>
    /// <returns>The sum.</returns>
    int Add(int a, int b);

    /// <summary>
    /// Returns the difference of two elements.
    /// </summary>
    /// <param name="a">The first element.</param>
    /// <param name="b">The element to subtract.</param>
    /// <returns>The difference.</returns>
    int Subtract(int a, int b);

    /// <summary>
    /// Returns the product of two elements.
    /// </summary>
    /// <param name="a">The first element.</param>
    /// <param name="b">The second element.</param>
    /// <returns>The product.</returns>
    int Multiply(int a, int b);

    /// <summary>
    /// Returns an element added to itself a number of times.
    /// </summary>
    /// <param name="a">The element.</param>
    /// <param name="count">The number of times, 1 or more.</param>
    /// <returns>The sum.</returns>
    int Scale(int a, int count);

    /// <summary>
    /// Returns the multiplicative inverse of an element.
    /// </summary>
    /// <param name="a">The element, not zero.</param>
    /// <returns>The inverse.</returns>
    int Inverse(int a);

    /// <summary>
    /// Returns the primitive element raised to a power.
    /// </summary>
    /// <param name="power">The power, 0 or more.</param>
    /// <returns>The element.</returns>
    int Exp(int power);
}
