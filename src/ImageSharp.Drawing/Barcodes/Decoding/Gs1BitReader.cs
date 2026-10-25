// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Reads the bit stream of GS1 DataBar Expanded and of the GS1 Composite 2D components, packed eight bits to a byte, the first bit in the most significant bit
/// of the first byte.
/// </summary>
internal ref struct Gs1BitReader
{
    private readonly ReadOnlySpan<byte> bytes;
    private readonly int length;
    private int position;

    /// <summary>
    /// Initializes a new instance of the <see cref="Gs1BitReader"/> struct.
    /// </summary>
    /// <param name="bytes">The packed bits.</param>
    /// <param name="length">The number of bits.</param>
    public Gs1BitReader(ReadOnlySpan<byte> bytes, int length)
    {
        this.bytes = bytes;
        this.length = length;
        this.position = 0;
        this.Failed = false;
    }

    /// <summary>
    /// Gets the number of bits left.
    /// </summary>
    public readonly int Remaining => this.length - this.position;

    /// <summary>
    /// Gets a value indicating whether a read went past the end of the bits, or a field was invalid.
    /// </summary>
    public bool Failed { get; private set; }

    /// <summary>
    /// Marks the stream as invalid.
    /// </summary>
    public void Fail() => this.Failed = true;

    /// <summary>
    /// Returns the next bits without reading them. The bits past the end read as zero.
    /// </summary>
    /// <param name="count">The number of bits.</param>
    /// <returns>The bits, the first the most significant.</returns>
    public readonly int Peek(int count)
    {
        int value = 0;
        for (int i = 0; i < count; i++)
        {
            int index = this.position + i;
            int bit = index < this.length ? (this.bytes[index >> 3] >> (7 - (index & 7))) & 1 : 0;
            value = (value << 1) | bit;
        }

        return value;
    }

    /// <summary>
    /// Reads the next bits.
    /// </summary>
    /// <param name="count">The number of bits.</param>
    /// <returns>The bits, the first the most significant.</returns>
    public int Read(int count)
    {
        if (count > this.Remaining)
        {
            this.Failed = true;
        }

        int value = this.Peek(count);
        this.position += count;
        return value;
    }
}
