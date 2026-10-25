// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.InteropServices;
using System.Text;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The data a symbol decodes to: text, and bytes that read through <see cref="ByteText"/> in the character set of the current Extended Channel Interpretation
/// when text follows them, the ECI changes or the data ends.
/// </summary>
internal sealed class DecodedText
{
    private readonly StringBuilder text = new();
    private readonly List<byte> bytes = [];
    private int eci = -1;

    /// <summary>
    /// Gets the bytes that wait for the text to end or the ECI to change.
    /// </summary>
    public List<byte> PendingBytes => this.bytes;

    /// <summary>
    /// Gets the number of characters so far, the pending bytes not counted.
    /// </summary>
    public int TextLength => this.text.Length;

    /// <summary>
    /// Gets a value indicating whether nothing has been appended.
    /// </summary>
    public bool IsEmpty => this.text.Length == 0 && this.bytes.Count == 0;

    /// <summary>
    /// Appends text.
    /// </summary>
    /// <param name="value">The text.</param>
    public void AppendText(ReadOnlySpan<char> value)
    {
        this.Flush();
        this.text.Append(value);
    }

    /// <summary>
    /// Appends a character.
    /// </summary>
    /// <param name="value">The character.</param>
    public void AppendChar(char value)
    {
        this.Flush();
        this.text.Append(value);
    }

    /// <summary>
    /// Appends a byte.
    /// </summary>
    /// <param name="value">The byte.</param>
    public void AppendByte(byte value) => this.bytes.Add(value);

    /// <summary>
    /// Sets the ECI the bytes after it read in.
    /// </summary>
    /// <param name="value">The ECI number.</param>
    public void SetEci(int value)
    {
        this.Flush();
        this.eci = value;
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        this.Flush();
        return this.text.ToString();
    }

    /// <summary>
    /// Converts the pending bytes to text.
    /// </summary>
    private void Flush()
    {
        if (this.bytes.Count == 0)
        {
            return;
        }

        this.text.Append(ByteText.Decode(CollectionsMarshal.AsSpan(this.bytes), this.eci));
        this.bytes.Clear();
    }
}
