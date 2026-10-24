// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The arithmetic of a <see cref="PrimeField"/> for <see cref="ReedSolomonDecoder"/>.
/// </summary>
internal readonly struct PrimeReedSolomonField : IReedSolomonField
{
    private readonly PrimeField field;

    /// <summary>
    /// Initializes a new instance of the <see cref="PrimeReedSolomonField"/> struct.
    /// </summary>
    /// <param name="field">The field.</param>
    public PrimeReedSolomonField(PrimeField field) => this.field = field;

    /// <inheritdoc/>
    public int Order => this.field.Modulus - 1;

    /// <inheritdoc/>
    public int Add(int a, int b)
    {
        int sum = a + b;
        return sum >= this.field.Modulus ? sum - this.field.Modulus : sum;
    }

    /// <inheritdoc/>
    public int Subtract(int a, int b)
    {
        int difference = a - b;
        return difference < 0 ? difference + this.field.Modulus : difference;
    }

    /// <inheritdoc/>
    public int Multiply(int a, int b) => this.field.Multiply(a, b);

    /// <inheritdoc/>
    public int Scale(int a, int count) => (int)(((long)a * count) % this.field.Modulus);

    /// <inheritdoc/>
    public int Inverse(int a) => this.field.Inverse(a);

    /// <inheritdoc/>
    public int Exp(int power) => this.field.Exp(power);
}
