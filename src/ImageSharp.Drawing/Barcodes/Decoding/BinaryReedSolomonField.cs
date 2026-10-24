// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The arithmetic of a binary field GF(2^m) from its <see cref="GaloisField"/>. Addition and subtraction are both exclusive or.
/// </summary>
internal readonly struct BinaryReedSolomonField : IReedSolomonField
{
    private readonly GaloisField field;

    /// <summary>
    /// Initializes a new instance of the <see cref="BinaryReedSolomonField"/> struct.
    /// </summary>
    /// <param name="field">The field.</param>
    public BinaryReedSolomonField(GaloisField field) => this.field = field;

    /// <inheritdoc/>
    public int Order => this.field.Size - 1;

    /// <inheritdoc/>
    public int Add(int a, int b) => a ^ b;

    /// <inheritdoc/>
    public int Subtract(int a, int b) => a ^ b;

    /// <inheritdoc/>
    public int Multiply(int a, int b) => this.field.Multiply(a, b);

    /// <inheritdoc/>
    public int Scale(int a, int count) => (count & 1) == 0 ? 0 : a;

    /// <inheritdoc/>
    public int Inverse(int a) => this.field.Inverse(a);

    /// <inheritdoc/>
    public int Exp(int power) => this.field.Exp(power);
}
