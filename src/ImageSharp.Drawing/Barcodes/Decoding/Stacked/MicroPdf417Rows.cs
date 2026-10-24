// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Reads the rows of MicroPDF417 and of the CC-A and CC-B 2D components of GS1 Composite symbols, which section 8.2 of ISO/IEC 24723 takes from MicroPDF417:
/// a left row address pattern, one to four codewords with a centre row address pattern after the first of three columns or the second of four, a right row
/// address pattern and a single module bar. The row address patterns of a row and the cluster of its codewords fit only some rows of some sizes, so the
/// reads of the scan lines vote for the codewords of each size they fit, and the size with the most reads decodes.
/// </summary>
internal sealed class MicroPdf417Rows
{
    private const int CharacterElements = 8;

    private readonly Dictionary<int, CodewordVotes> sizes = [];
    private readonly Dictionary<int, int> rowCounts = [];

    /// <summary>
    /// Gets the sizes the rows fit, keyed by their offset in the size table, with their votes.
    /// </summary>
    public IReadOnlyDictionary<int, CodewordVotes> Sizes => this.sizes;

    /// <summary>
    /// Returns the number of scan lines whose row fits a size.
    /// </summary>
    /// <param name="offset">The offset of the size in its table.</param>
    /// <returns>The number of reads.</returns>
    public int Reads(int offset) => this.rowCounts.TryGetValue(offset, out int count) ? count : 0;

    /// <summary>
    /// Scans the rows of one orientation of an image and collects the rows that fit a size of a table.
    /// </summary>
    /// <param name="context">The decode.</param>
    /// <param name="bits">The image in this orientation.</param>
    /// <param name="table">The sizes, <see cref="MicroPdf417Encoder.SymbolSize.Length"/> bytes each.</param>
    /// <param name="hasLeft">Whether a three column size of the table has a left row address pattern. A CC-A of three columns has none.</param>
    /// <param name="reversed">Whether the scan lines read from right to left.</param>
    /// <returns>The rows.</returns>
    public static MicroPdf417Rows Scan(DecodeContext context, BitImage bits, ReadOnlySpan<byte> table, bool hasLeft, bool reversed)
    {
        MicroPdf417Rows rows = new();
        int width = bits.Width;
        using PooledBuffer<int> runBuffer = new(width + 2);
        using PooledBuffer<int> startBuffer = new(width + 2);
        Span<int> runs = runBuffer.Span;
        Span<int> starts = startBuffer.Span;
        List<Row> candidates = [];
        Row current = new();
        int step = context.Options.TryHarder ? 1 : Math.Max(1, bits.Height / 128);
        for (int y = 0; y < bits.Height; y += step)
        {
            int count = LinearScanner.ToRuns(bits.Row(y), runs, starts);
            if (count < 12)
            {
                continue;
            }

            if (reversed)
            {
                runs[..count].Reverse();
            }

            ReadOnlySpan<int> line = runs[..count];
            for (int i = 1; i + 6 < line.Length; i += 2)
            {
                candidates.Clear();

                // A row starts with its left row address pattern, or with a codeword in a three column CC-A.
                int left = Pdf417Characters.ReadAddress(line.Slice(i, Pdf417Characters.AddressElements), MicroPdf417Encoder.SideAddressPatterns);
                if (left >= 0)
                {
                    float module = LinearReader.Sum(line, i, Pdf417Characters.AddressElements) / (float)Pdf417Characters.AddressModules;
                    if (LinearReader.IsQuiet(line, i - 1, module * 0.8F))
                    {
                        current.Reset(left);
                        Parse(line, i + Pdf417Characters.AddressElements, module, current, candidates);
                    }
                }

                if (!hasLeft)
                {
                    float module = LinearReader.Sum(line, i, CharacterElements) / (float)Pdf417Characters.CharacterModules;
                    if (LinearReader.IsQuiet(line, i - 1, module * 0.8F))
                    {
                        current.Reset(-1);
                        Parse(line, i, module, current, candidates);
                    }
                }

                foreach (Row row in candidates)
                {
                    float rowLeft = reversed ? starts[count - row.End] : starts[i];
                    float rowRight = reversed ? starts[count - i] : (row.End < count ? starts[row.End] : width);
                    if (rows.Add(row, table, hasLeft, rowLeft, rowRight, y))
                    {
                        i = row.End;
                        break;
                    }
                }
            }
        }

        return rows;
    }

    /// <summary>
    /// Returns the size an offset of a table names.
    /// </summary>
    /// <param name="table">The sizes.</param>
    /// <param name="offset">The offset.</param>
    /// <returns>The size.</returns>
    public static MicroPdf417Encoder.SymbolSize SizeAt(ReadOnlySpan<byte> table, int offset)
        => new(table.Slice(offset, MicroPdf417Encoder.SymbolSize.Length));

    /// <summary>
    /// Parses the rest of a row from a position, trying a codeword, the centre row address pattern and the right row address pattern with its closing bar,
    /// and adds every way the row ends to the candidates.
    /// </summary>
    /// <param name="runs">The runs.</param>
    /// <param name="position">The index of the next run.</param>
    /// <param name="module">The module width.</param>
    /// <param name="row">The row so far.</param>
    /// <param name="candidates">The rows read.</param>
    private static void Parse(ReadOnlySpan<int> runs, int position, float module, Row row, List<Row> candidates)
    {
        int elements = Pdf417Characters.AddressElements;

        // The right row address pattern, a bar of one module and the quiet zone end the row.
        if (row.Count > 0 && position + elements + 1 <= runs.Length)
        {
            int right = Pdf417Characters.ReadAddress(runs.Slice(position, elements), MicroPdf417Encoder.SideAddressPatterns);
            int bar = position + elements;
            if (right >= 0 && runs[bar] > module * 0.5F && runs[bar] < module * 1.6F && LinearReader.IsQuiet(runs, bar + 1, module * 0.8F))
            {
                Row done = row.Clone();
                done.Right = right;
                done.End = bar + 1;
                candidates.Add(done);
            }
        }

        if (row.Count < MicroPdf417Encoder.MaximumColumns && position + CharacterElements <= runs.Length)
        {
            int codeword = Pdf417Characters.Read(runs.Slice(position, CharacterElements), out int cluster);
            float characterModules = LinearReader.Sum(runs, position, CharacterElements) / module;
            if (codeword >= 0 && (row.Cluster < 0 || row.Cluster == cluster) && characterModules is > 12F and < 24F)
            {
                int previousCluster = row.Cluster;
                row.Cluster = cluster;
                row.Push(codeword);
                Parse(runs, position + CharacterElements, module, row, candidates);
                row.Pop();
                row.Cluster = previousCluster;
            }
        }

        if (row.Centre < 0 && row.Count > 0 && position + elements <= runs.Length)
        {
            int centre = Pdf417Characters.ReadAddress(runs.Slice(position, elements), MicroPdf417Encoder.CentreAddressPatterns);
            if (centre >= 0)
            {
                row.Centre = centre;
                row.CentreAfter = row.Count;
                Parse(runs, position + elements, module, row, candidates);
                row.Centre = -1;
            }
        }
    }

    /// <summary>
    /// Adds a row read to every size of the table it fits.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <param name="table">The sizes.</param>
    /// <param name="hasLeft">Whether a three column size has a left row address pattern.</param>
    /// <param name="left">The left end of the row.</param>
    /// <param name="right">The right end of the row.</param>
    /// <param name="y">The scan line.</param>
    /// <returns><see langword="true"/> when the row fits a size.</returns>
    private bool Add(Row row, ReadOnlySpan<byte> table, bool hasLeft, float left, float right, int y)
    {
        int count = MicroPdf417Encoder.AddressPatternCount;
        int columns = row.Count;
        bool fits = false;
        for (int offset = 0; offset < table.Length; offset += MicroPdf417Encoder.SymbolSize.Length)
        {
            MicroPdf417Encoder.SymbolSize size = SizeAt(table, offset);
            bool sizeHasLeft = hasLeft || columns != 3;
            if (size.Columns != columns || (row.Left >= 0) != sizeHasLeft)
            {
                continue;
            }

            // The right row address pattern gives the row, and the left and centre patterns and the cluster must agree with it.
            int rowIndex = (((row.Right - (size.RightPattern - 1)) % count) + count) % count;
            if (rowIndex >= size.Rows
                || (row.Left >= 0 && (rowIndex + size.LeftPattern - 1) % count != row.Left)
                || (rowIndex + size.LeftPattern - 1) % 3 != row.Cluster
                || (size.CentrePattern > 0) != (row.Centre >= 0)
                || (row.Centre >= 0 && ((rowIndex + size.CentrePattern - 1) % count != row.Centre || row.CentreAfter != columns - 2)))
            {
                continue;
            }

            if (!this.sizes.TryGetValue(offset, out CodewordVotes? votes))
            {
                votes = new CodewordVotes();
                this.sizes.Add(offset, votes);
            }

            for (int column = 0; column < columns; column++)
            {
                votes.AddCodeword(rowIndex, column, row.Codewords[column]);
            }

            votes.Extend(left, right, y);
            this.rowCounts[offset] = this.Reads(offset) + 1;
            fits = true;
        }

        return fits;
    }

    /// <summary>
    /// One row of codewords and row address patterns.
    /// </summary>
    private sealed class Row
    {
        private readonly int[] codewords = new int[MicroPdf417Encoder.MaximumColumns];

        /// <summary>
        /// Gets or sets the left row address pattern index, or -1.
        /// </summary>
        public int Left { get; set; } = -1;

        /// <summary>
        /// Gets or sets the centre row address pattern index, or -1.
        /// </summary>
        public int Centre { get; set; } = -1;

        /// <summary>
        /// Gets or sets the number of codewords before the centre row address pattern.
        /// </summary>
        public int CentreAfter { get; set; }

        /// <summary>
        /// Gets or sets the right row address pattern index.
        /// </summary>
        public int Right { get; set; } = -1;

        /// <summary>
        /// Gets or sets the cluster of the codewords, or -1.
        /// </summary>
        public int Cluster { get; set; } = -1;

        /// <summary>
        /// Gets or sets the index of the light run after the row.
        /// </summary>
        public int End { get; set; }

        /// <summary>
        /// Gets the number of codewords.
        /// </summary>
        public int Count { get; private set; }

        /// <summary>
        /// Gets the codewords.
        /// </summary>
        public ReadOnlySpan<int> Codewords => this.codewords.AsSpan(0, this.Count);

        /// <summary>
        /// Starts a new row.
        /// </summary>
        /// <param name="left">The left row address pattern index, or -1.</param>
        public void Reset(int left)
        {
            this.Left = left;
            this.Centre = -1;
            this.Right = -1;
            this.Cluster = -1;
            this.Count = 0;
        }

        /// <summary>
        /// Adds a codeword.
        /// </summary>
        /// <param name="codeword">The codeword.</param>
        public void Push(int codeword) => this.codewords[this.Count++] = codeword;

        /// <summary>
        /// Removes the last codeword.
        /// </summary>
        public void Pop() => this.Count--;

        /// <summary>
        /// Copies the row.
        /// </summary>
        /// <returns>The copy.</returns>
        public Row Clone()
        {
            Row copy = new()
            {
                Left = this.Left,
                Centre = this.Centre,
                CentreAfter = this.CentreAfter,
                Right = this.Right,
                Cluster = this.Cluster,
                End = this.End,
            };

            this.codewords.AsSpan(0, this.Count).CopyTo(copy.codewords);
            copy.Count = this.Count;
            return copy;
        }
    }
}
