// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The arithmetic of a prime field: the integers modulo a prime, with a primitive element whose powers give every element except zero.
/// </summary>
internal sealed class PrimeField
{
    private readonly int[] exp;
    private readonly int[] log;

    /// <summary>
    /// Initializes a new instance of the <see cref="PrimeField"/> class.
    /// </summary>
    /// <param name="modulus">The prime modulus.</param>
    /// <param name="generator">The primitive element.</param>
    public PrimeField(int modulus, int generator)
    {
        this.Modulus = modulus;
        this.exp = new int[modulus - 1];
        this.log = new int[modulus];
        int value = 1;
        for (int i = 0; i < modulus - 1; i++)
        {
            this.exp[i] = value;
            this.log[value] = i;
            value = (value * generator) % modulus;
        }
    }

    /// <summary>
    /// Gets the prime modulus.
    /// </summary>
    public int Modulus { get; }

    /// <summary>
    /// Returns the primitive element raised to a power.
    /// </summary>
    /// <param name="power">The power, 0 or more.</param>
    /// <returns>The element.</returns>
    public int Exp(int power) => this.exp[power % (this.Modulus - 1)];

    /// <summary>
    /// Returns the product of two elements.
    /// </summary>
    /// <param name="a">The first element.</param>
    /// <param name="b">The second element.</param>
    /// <returns>The product.</returns>
    public int Multiply(int a, int b) => (a * b) % this.Modulus;

    /// <summary>
    /// Returns the multiplicative inverse of an element.
    /// </summary>
    /// <param name="a">The element, not zero.</param>
    /// <returns>The inverse.</returns>
    public int Inverse(int a) => this.exp[(this.Modulus - 1 - this.log[a]) % (this.Modulus - 1)];
}
