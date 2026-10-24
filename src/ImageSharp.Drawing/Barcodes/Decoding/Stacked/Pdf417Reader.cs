// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Reads PDF417 and Compact PDF417, ISO/IEC 15438. Every scan line that crosses a row reads its start character, its left row indicator, its codewords and
/// its right row indicator and stop character, or the single bar that ends a compact row. The cluster of the codewords and the left row indicator give the
/// row number, and the row indicators give the row count, the column count and the error correction level. The reads of all scan lines vote for every
/// codeword of the symbol matrix, the Reed-Solomon code over GF(929) corrects the matrix, and <see cref="Pdf417Text"/> decodes the data.
/// </summary>
internal sealed class Pdf417Reader : StackedReader
{
    private const float MaximumVariance = 0.25F;
    private const float MaximumIndividualVariance = 0.8F;
    private const int CharacterElements = 8;
    private const int RowsKey = 0;
    private const int LevelKey = 1;
    private const int ColumnsKey = 2;

    /// <summary>
    /// The field of the error correction: the integers modulo 929 with the primitive element 3.
    /// </summary>
    private static readonly PrimeField Field = new(929, 3);

    /// <inheritdoc/>
    public override BarcodeFamily Families => BarcodeFamily.Pdf417;

    /// <summary>
    /// Gets the element widths of the start character, Table 1.
    /// </summary>
    private static ReadOnlySpan<int> Start => [8, 1, 1, 1, 1, 1, 1, 3];

    /// <summary>
    /// Gets the element widths of the stop character, Table 1.
    /// </summary>
    private static ReadOnlySpan<int> Stop => [7, 1, 1, 3, 1, 1, 1, 2, 1];

    /// <inheritdoc/>
    public override void Read(DecodeContext context, BitImage bits, bool turned)
    {
        List<CodewordVotes>[] symbols = [[], []];
        int width = bits.Width;
        using PooledBuffer<int> runBuffer = new(width + 2);
        using PooledBuffer<int> reversedBuffer = new(width + 2);
        using PooledBuffer<int> startBuffer = new(width + 2);
        Span<int> forward = runBuffer.Span;
        Span<int> backward = reversedBuffer.Span;
        Span<int> starts = startBuffer.Span;
        List<int> codewords = [];

        int step = context.Options.TryHarder ? 1 : Math.Max(1, bits.Height / 128);
        for (int y = 0; y < bits.Height; y += step)
        {
            int count = LinearScanner.ToRuns(bits.Row(y), forward, starts);
            if (count < 20)
            {
                continue;
            }

            for (int r = 0; r < count; r++)
            {
                backward[r] = forward[count - 1 - r];
            }

            for (int direction = 0; direction < 2; direction++)
            {
                ReadOnlySpan<int> runs = direction == 0 ? forward[..count] : backward[..count];
                for (int i = 1; i + Start.Length < runs.Length; i += 2)
                {
                    if (LinearReader.Variance(runs.Slice(i, Start.Length), Start, MaximumIndividualVariance) > MaximumVariance)
                    {
                        continue;
                    }

                    float module = LinearReader.Sum(runs, i, Start.Length) / 17F;
                    if (runs[i - 1] < module * 1.5F && i > 1)
                    {
                        continue;
                    }

                    if (!TryReadRow(runs, i + Start.Length, module, codewords, out int cluster, out bool compact, out int end))
                    {
                        continue;
                    }

                    // The ends of the row in the forward direction of the scan line, for the bounds of the symbol.
                    float left = direction == 0 ? starts[i] : starts[count - end];
                    float right = direction == 0 ? (end < count ? starts[end] : width) : starts[count - i];
                    Add(symbols[direction], codewords, cluster, compact, module, left, right, y);
                    i = end;
                }
            }
        }

        for (int direction = 0; direction < 2; direction++)
        {
            foreach (CodewordVotes symbol in symbols[direction])
            {
                if (TryDecode(symbol, out string? text))
                {
                    // The corners as the symbol reads: a symbol read right to left is upside down.
                    PointF topLeft = Map(symbol.Left, symbol.Top, bits, turned);
                    PointF topRight = Map(symbol.Right, symbol.Top, bits, turned);
                    PointF bottomRight = Map(symbol.Right, symbol.Bottom + 1, bits, turned);
                    PointF bottomLeft = Map(symbol.Left, symbol.Bottom + 1, bits, turned);
                    PointF[] corners = direction == 0 ? [topLeft, topRight, bottomRight, bottomLeft] : [bottomRight, bottomLeft, topLeft, topRight];
                    context.Add(BarcodeFamily.Pdf417, text, "]L2", corners);
                }
            }
        }
    }

    /// <summary>
    /// Reads the codewords of a row after its start character, up to the stop character or the single bar of a compact row.
    /// </summary>
    /// <param name="runs">The runs.</param>
    /// <param name="position">The index of the first bar after the start character.</param>
    /// <param name="module">The module width from the start character.</param>
    /// <param name="codewords">The codewords read, the row indicators included.</param>
    /// <param name="cluster">The cluster of the row.</param>
    /// <param name="compact">Whether the row ends without a right row indicator and a stop character.</param>
    /// <param name="end">The index of the light run after the row.</param>
    /// <returns><see langword="true"/> when the row reads.</returns>
    private static bool TryReadRow(ReadOnlySpan<int> runs, int position, float module, List<int> codewords, out int cluster, out bool compact, out int end)
    {
        codewords.Clear();
        cluster = -1;
        compact = false;
        end = 0;
        while (position + 1 < runs.Length)
        {
            // A codeword first: the first eight elements of the stop character have an element of seven modules, which no codeword has.
            if (position + CharacterElements <= runs.Length)
            {
                int codeword = Pdf417Characters.Read(runs.Slice(position, CharacterElements), out int codewordCluster);

                // Every codeword of the row is about as wide as the start character, and of the cluster of the row.
                float characterModules = LinearReader.Sum(runs, position, CharacterElements) / module;
                if (codeword >= 0 && (cluster < 0 || codewordCluster == cluster) && characterModules is > 12F and < 24F)
                {
                    cluster = codewordCluster;
                    codewords.Add(codeword);
                    position += CharacterElements;
                    continue;
                }
            }

            if (position + Stop.Length <= runs.Length
                && LinearReader.Variance(runs.Slice(position, Stop.Length), Stop, MaximumIndividualVariance) <= MaximumVariance)
            {
                end = position + Stop.Length;
                return codewords.Count >= 3;
            }

            // A compact row ends with a bar of one module.
            float bar = runs[position] / module;
            if (bar is > 0.5F and < 1.6F && (position + 1 == runs.Length - 1 || runs[position + 1] >= module * 1.5F))
            {
                compact = true;
                end = position + 1;
                return codewords.Count >= 2;
            }

            return false;
        }

        return false;
    }

    /// <summary>
    /// Adds the codewords of a row to the symbol it belongs to: the symbol whose rows start where this one does at the same module width, or a new one.
    /// </summary>
    /// <param name="symbols">The symbols of the direction.</param>
    /// <param name="codewords">The codewords of the row, the row indicators included.</param>
    /// <param name="cluster">The cluster of the row.</param>
    /// <param name="compact">Whether the row is compact.</param>
    /// <param name="module">The module width.</param>
    /// <param name="left">The left end of the row.</param>
    /// <param name="right">The right end of the row.</param>
    /// <param name="y">The scan line.</param>
    private static void Add(List<CodewordVotes> symbols, List<int> codewords, int cluster, bool compact, float module, float left, float right, int y)
    {
        CodewordVotes? symbol = null;
        foreach (CodewordVotes candidate in symbols)
        {
            if (MathF.Abs(candidate.Left - left) < module * 4 && MathF.Abs(candidate.Module - module) < module * 0.3F && y - candidate.Bottom < module * 30)
            {
                symbol = candidate;
                break;
            }
        }

        if (symbol is null)
        {
            symbol = new CodewordVotes { Module = module };
            symbols.Add(symbol);
        }

        symbol.Extend(left, right, y);

        // Section 5.11.3: the row indicators of a row of cluster 0 carry (r - 1) div 3 and c - 1, of cluster 3 the level and (r - 1) mod 3 and (r - 1) div 3,
        // and of cluster 6 c - 1 and the level and (r - 1) mod 3, each plus 30 times the row group. The keys follow the left indicator of each cluster.
        int leftIndicator = codewords[0];
        int row = (3 * (leftIndicator / 30)) + cluster;
        ReadOnlySpan<int> leftKeys = [RowsKey, LevelKey, ColumnsKey];
        ReadOnlySpan<int> rightKeys = [ColumnsKey, RowsKey, LevelKey];
        symbol.AddValue(leftKeys[cluster], leftIndicator % 30);
        int dataEnd = codewords.Count;
        if (!compact)
        {
            int rightIndicator = codewords[^1];
            if (rightIndicator / 30 != leftIndicator / 30)
            {
                return;
            }

            symbol.AddValue(rightKeys[cluster], rightIndicator % 30);
            dataEnd--;
        }

        // The data columns of the row also count the columns of the symbol.
        symbol.AddValue(ColumnsKey + 1, dataEnd - 2);
        for (int column = 1; column < dataEnd; column++)
        {
            symbol.AddCodeword(row, column - 1, codewords[column]);
        }
    }

    /// <summary>
    /// Builds the symbol matrix from the votes, corrects it and decodes it.
    /// </summary>
    /// <param name="symbol">The votes of the symbol.</param>
    /// <param name="text">The data.</param>
    /// <returns><see langword="true"/> when the symbol decodes.</returns>
    private static bool TryDecode(CodewordVotes symbol, out string text)
    {
        text = string.Empty;
        int rowsDiv3 = symbol.Value(RowsKey);
        int levelValue = symbol.Value(LevelKey);
        int columns = symbol.Value(ColumnsKey);
        if (columns < 0)
        {
            columns = symbol.Value(ColumnsKey + 1);
        }

        if (rowsDiv3 < 0 || levelValue < 0 || columns < 0)
        {
            return false;
        }

        columns++;
        int rows = (rowsDiv3 * 3) + (levelValue % 3) + 1;
        int level = levelValue / 3;
        int checkCount = 2 << level;
        int total = rows * columns;
        if (rows < 3 || level > Pdf417Encoder.MaximumErrorCorrectionLevel || columns > Pdf417Encoder.MaximumColumns || total > 928 || total <= checkCount)
        {
            return false;
        }

        using PooledBuffer<int> buffer = new(total);
        Span<int> matrix = buffer.Span;
        int missing = 0;
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                int codeword = symbol.Codeword(row, column);
                if (codeword < 0)
                {
                    codeword = 0;
                    missing++;
                }

                matrix[(row * columns) + column] = codeword;
            }
        }

        if (missing * 2 > checkCount || ReedSolomonDecoder.Correct(new PrimeReedSolomonField(Field), matrix, checkCount, 1) < 0)
        {
            return false;
        }

        // Section 5.9.2: the Symbol Length Descriptor counts itself, the data and the pad codewords.
        int length = matrix[0];
        if (length < 1 || length > total - checkCount)
        {
            return false;
        }

        return Pdf417Text.TryDecode(matrix[1..length], Pdf417Text.StartMode.Text, out text);
    }
}
