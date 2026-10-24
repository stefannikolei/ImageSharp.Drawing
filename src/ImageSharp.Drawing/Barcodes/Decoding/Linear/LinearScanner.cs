// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Scans the rows of the binarized image, and its columns when the options try harder, and passes the runs of each scan line to the linear readers, in both
/// directions. A symbol read with a check is accepted at once. A symbol without one must read the same on two scan lines.
/// </summary>
internal static class LinearScanner
{
    /// <summary>
    /// Scans the image with the readers.
    /// </summary>
    /// <param name="context">The decode.</param>
    /// <param name="readers">The readers whose families the options name.</param>
    public static void Scan(DecodeContext context, ReadOnlySpan<LinearReader> readers)
    {
        if (readers.IsEmpty)
        {
            return;
        }

        List<Candidate> candidates = [];
        ScanImage(context, context.Bits, false, readers, candidates);
        if (context.Options.TryHarder)
        {
            ScanImage(context, context.TurnedBits, true, readers, candidates);
        }

        // Unchecked symbols need a second scan line that reads the same.
        foreach (Candidate candidate in candidates)
        {
            if (!candidate.Accepted && candidate.Count >= 2)
            {
                candidate.Accept(context);
            }
        }
    }

    /// <summary>
    /// Scans the rows of one orientation of the image.
    /// </summary>
    /// <param name="context">The decode.</param>
    /// <param name="bits">The image in this orientation.</param>
    /// <param name="turned">Whether the image is the quarter turn of the source.</param>
    /// <param name="readers">The readers.</param>
    /// <param name="candidates">The symbols read so far.</param>
    private static void ScanImage(DecodeContext context, BitImage bits, bool turned, ReadOnlySpan<LinearReader> readers, List<Candidate> candidates)
    {
        int width = bits.Width;
        int height = bits.Height;

        // The rows go out from the middle, where a symbol is most likely to be, every step rows. Trying harder scans more rows.
        int step = Math.Max(1, height / (context.Options.TryHarder ? 256 : 32));
        int middle = height / 2;

        using PooledBuffer<int> forwardBuffer = new(width + 2);
        using PooledBuffer<int> backwardBuffer = new(width + 2);
        using PooledBuffer<int> startBuffer = new(width + 2);
        Span<int> forward = forwardBuffer.Span;
        Span<int> backward = backwardBuffer.Span;
        Span<int> starts = startBuffer.Span;

        for (int i = 0; ; i++)
        {
            int offset = ((i + 1) >> 1) * step;
            int y = (i & 1) == 0 ? middle + offset : middle - offset;
            if (offset > middle + step)
            {
                break;
            }

            if ((uint)y >= (uint)height)
            {
                continue;
            }

            int count = ToRuns(bits.Row(y), forward, starts);
            if (count < 3)
            {
                continue;
            }

            ReadOnlySpan<int> runs = forward[..count];
            for (int r = 0; r < count; r++)
            {
                backward[r] = runs[count - 1 - r];
            }

            ReadOnlySpan<int> reversed = backward[..count];
            foreach (LinearReader reader in readers)
            {
                if (reader.Directional && turned)
                {
                    continue;
                }

                ReadLine(context, reader, runs, starts[..count], false, y, width, turned, candidates);
                if (!reader.Directional)
                {
                    ReadLine(context, reader, reversed, starts[..count], true, y, width, turned, candidates);
                }
            }
        }
    }

    /// <summary>
    /// Reads every symbol a reader finds on one scan line.
    /// </summary>
    /// <param name="context">The decode.</param>
    /// <param name="reader">The reader.</param>
    /// <param name="runs">The runs in the reading direction.</param>
    /// <param name="starts">The start of each run in the forward direction, in pixels.</param>
    /// <param name="reversed">Whether the runs read from right to left.</param>
    /// <param name="y">The row.</param>
    /// <param name="width">The width of the row.</param>
    /// <param name="turned">Whether the image is the quarter turn of the source.</param>
    /// <param name="candidates">The symbols read so far.</param>
    private static void ReadLine(
        DecodeContext context,
        LinearReader reader,
        ReadOnlySpan<int> runs,
        ReadOnlySpan<int> starts,
        bool reversed,
        int y,
        int width,
        bool turned,
        List<Candidate> candidates)
    {
        int start = 1;
        while (start < runs.Length - 1 && reader.TryRead(runs, start, context, out LinearMatch match))
        {
            if (context.Wants(match.Family))
            {
                // The ends of the symbol in the forward direction of the row.
                int first = reversed ? runs.Length - match.EndRun : match.StartRun;
                int last = reversed ? runs.Length - match.StartRun : match.EndRun;
                float left = starts[first];
                float right = last < starts.Length ? starts[last] : width;
                Record(context, candidates, match, left, right, y, turned, width);
            }

            start = Math.Max(match.EndRun, start + 2) | 1;
        }
    }

    /// <summary>
    /// Records a read: a new candidate, or one more scan line of a known one.
    /// </summary>
    /// <param name="context">The decode.</param>
    /// <param name="candidates">The symbols read so far.</param>
    /// <param name="match">The read.</param>
    /// <param name="left">The left end in the row.</param>
    /// <param name="right">The right end in the row.</param>
    /// <param name="y">The row.</param>
    /// <param name="turned">Whether the image is the quarter turn of the source.</param>
    /// <param name="width">The width of the image in this orientation.</param>
    private static void Record(DecodeContext context, List<Candidate> candidates, in LinearMatch match, float left, float right, int y, bool turned, int width)
    {
        foreach (Candidate candidate in candidates)
        {
            if (candidate.Family == match.Family && candidate.Text == match.Text)
            {
                candidate.Extend(left, right, y);
                if (!candidate.Accepted && candidate.Checked)
                {
                    candidate.Accept(context);
                }

                return;
            }
        }

        Candidate created = new(match, left, right, y, turned, width);
        candidates.Add(created);
        if (match.CheckedData)
        {
            created.Accept(context);
        }
    }

    /// <summary>
    /// Writes the runs of a row of pixels, light first and light last, and the start of each run.
    /// </summary>
    /// <param name="row">The pixels, 1 for dark.</param>
    /// <param name="runs">The runs to write, at least the row width plus two.</param>
    /// <param name="starts">The starts to write.</param>
    /// <returns>The number of runs, an odd number.</returns>
    private static int ToRuns(ReadOnlySpan<byte> row, Span<int> runs, Span<int> starts)
    {
        int count = 0;
        int current = 0;
        int length = 0;
        int position = 0;
        starts[0] = 0;
        for (int x = 0; x < row.Length; x++)
        {
            if (row[x] == current)
            {
                length++;
                continue;
            }

            runs[count] = length;
            position += length;
            count++;
            starts[count] = position;
            current = row[x];
            length = 1;
        }

        runs[count++] = length;
        if (current == 1)
        {
            starts[count] = row.Length;
            runs[count++] = 0;
        }

        return count;
    }

    /// <summary>
    /// A symbol read on one or more scan lines.
    /// </summary>
    private sealed class Candidate
    {
        private readonly string identifier;
        private readonly bool turned;
        private readonly int width;
        private float left;
        private float right;
        private int top;
        private int bottom;

        /// <summary>
        /// Initializes a new instance of the <see cref="Candidate"/> class.
        /// </summary>
        /// <param name="match">The first read.</param>
        /// <param name="left">The left end in the row.</param>
        /// <param name="right">The right end in the row.</param>
        /// <param name="y">The row.</param>
        /// <param name="turned">Whether the image is the quarter turn of the source.</param>
        /// <param name="width">The width of the image in this orientation, the height of the source when it is turned.</param>
        public Candidate(in LinearMatch match, float left, float right, int y, bool turned, int width)
        {
            this.Family = match.Family;
            this.Text = match.Text;
            this.identifier = match.Identifier;
            this.Checked = match.CheckedData;
            this.left = left;
            this.right = right;
            this.top = y;
            this.bottom = y;
            this.turned = turned;
            this.width = width;
            this.Count = 1;
        }

        /// <summary>
        /// Gets the family.
        /// </summary>
        public BarcodeFamily Family { get; }

        /// <summary>
        /// Gets the text.
        /// </summary>
        public string Text { get; }

        /// <summary>
        /// Gets a value indicating whether a check proves the data.
        /// </summary>
        public bool Checked { get; }

        /// <summary>
        /// Gets the number of scan lines that read the symbol.
        /// </summary>
        public int Count { get; private set; }

        /// <summary>
        /// Gets a value indicating whether the symbol is in the results.
        /// </summary>
        public bool Accepted { get; private set; }

        /// <summary>
        /// Adds a scan line that read the symbol.
        /// </summary>
        /// <param name="left">The left end in the row.</param>
        /// <param name="right">The right end in the row.</param>
        /// <param name="y">The row.</param>
        public void Extend(float left, float right, int y)
        {
            this.Count++;
            this.left = Math.Min(this.left, left);
            this.right = Math.Max(this.right, right);
            this.top = Math.Min(this.top, y);
            this.bottom = Math.Max(this.bottom, y);
        }

        /// <summary>
        /// Adds the symbol to the results.
        /// </summary>
        /// <param name="context">The decode.</param>
        public void Accept(DecodeContext context)
        {
            this.Accepted = true;
            PointF[] corners =
            [
                this.Map(this.left, this.top),
                this.Map(this.right, this.top),
                this.Map(this.right, this.bottom + 1),
                this.Map(this.left, this.bottom + 1),
            ];

            context.Add(this.Family, this.Text, this.identifier, corners);
        }

        /// <summary>
        /// Maps a point of the scanned orientation to the source image. The quarter turn takes the source pixel (x, y) to (h - 1 - y, x), where h is the
        /// height of the source, the width of the turned image. On pixel edges that is (h - y, x).
        /// </summary>
        /// <param name="x">The column in the scanned orientation.</param>
        /// <param name="y">The row in the scanned orientation.</param>
        /// <returns>The point in the source image.</returns>
        private PointF Map(float x, float y)
            => this.turned ? new PointF(y, this.width - x) : new PointF(x, y);
    }
}
