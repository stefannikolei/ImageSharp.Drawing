// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The codewords that the scan lines read at each row and column of a stacked symbol, and the counts of each value. Every row of the symbol is crossed by
/// several scan lines, so the value read most often stands for the codeword, and a cell no scan line read is an erasure for the error correction.
/// </summary>
internal sealed class CodewordVotes
{
    private readonly Dictionary<long, Dictionary<int, int>> cells = [];
    private readonly Dictionary<int, Dictionary<int, int>> values = [];

    /// <summary>
    /// Gets or sets the leftmost pixel of the symbol in the scanned orientation.
    /// </summary>
    public float Left { get; set; } = float.MaxValue;

    /// <summary>
    /// Gets or sets the rightmost pixel of the symbol in the scanned orientation.
    /// </summary>
    public float Right { get; set; } = float.MinValue;

    /// <summary>
    /// Gets or sets the top scan line of the symbol.
    /// </summary>
    public int Top { get; set; } = int.MaxValue;

    /// <summary>
    /// Gets or sets the bottom scan line of the symbol.
    /// </summary>
    public int Bottom { get; set; } = int.MinValue;

    /// <summary>
    /// Gets or sets the module width of the symbol in pixels.
    /// </summary>
    public float Module { get; set; }

    /// <summary>
    /// Adds a read of a codeword.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <param name="column">The column.</param>
    /// <param name="codeword">The codeword.</param>
    public void AddCodeword(int row, int column, int codeword) => Count(this.cells, ((long)row << 32) | (uint)column, codeword);

    /// <summary>
    /// Adds a read of a value of the symbol, such as the row count a row indicator carries.
    /// </summary>
    /// <param name="key">The value the read is of.</param>
    /// <param name="value">The value read.</param>
    public void AddValue(int key, int value) => Count(this.values, key, value);

    /// <summary>
    /// Adds a scan line to the bounds of the symbol.
    /// </summary>
    /// <param name="left">The left end in the scanned orientation.</param>
    /// <param name="right">The right end in the scanned orientation.</param>
    /// <param name="y">The scan line.</param>
    public void Extend(float left, float right, int y)
    {
        this.Left = Math.Min(this.Left, left);
        this.Right = Math.Max(this.Right, right);
        this.Top = Math.Min(this.Top, y);
        this.Bottom = Math.Max(this.Bottom, y);
    }

    /// <summary>
    /// Returns the value of a cell read most often.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <param name="column">The column.</param>
    /// <returns>The codeword, or -1 when no scan line read the cell.</returns>
    public int Codeword(int row, int column) => Best(this.cells, ((long)row << 32) | (uint)column);

    /// <summary>
    /// Returns the value of the symbol read most often.
    /// </summary>
    /// <param name="key">The value the reads are of.</param>
    /// <returns>The value, or -1 when no scan line read it.</returns>
    public int Value(int key) => Best(this.values, key);

    /// <summary>
    /// Counts a read in a table of reads.
    /// </summary>
    /// <typeparam name="TKey">The type of the key.</typeparam>
    /// <param name="table">The table.</param>
    /// <param name="key">The key.</param>
    /// <param name="value">The value read.</param>
    private static void Count<TKey>(Dictionary<TKey, Dictionary<int, int>> table, TKey key, int value)
        where TKey : notnull
    {
        if (!table.TryGetValue(key, out Dictionary<int, int>? counts))
        {
            counts = [];
            table.Add(key, counts);
        }

        counts[value] = counts.TryGetValue(value, out int count) ? count + 1 : 1;
    }

    /// <summary>
    /// Returns the value read most often for a key.
    /// </summary>
    /// <typeparam name="TKey">The type of the key.</typeparam>
    /// <param name="table">The table.</param>
    /// <param name="key">The key.</param>
    /// <returns>The value, or -1.</returns>
    private static int Best<TKey>(Dictionary<TKey, Dictionary<int, int>> table, TKey key)
        where TKey : notnull
    {
        if (!table.TryGetValue(key, out Dictionary<int, int>? counts))
        {
            return -1;
        }

        int best = -1;
        int bestCount = 0;
        foreach (KeyValuePair<int, int> pair in counts)
        {
            if (pair.Value > bestCount)
            {
                best = pair.Key;
                bestCount = pair.Value;
            }
        }

        return best;
    }
}
