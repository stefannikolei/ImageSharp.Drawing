// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Reads DotCode, AIM ISS DotCode, the inverse of <see cref="DotCodeEncoder"/>. DotCode has no finder pattern: a symbol is a region of
/// <see cref="SymbolRegions"/> whose dark pixels are round dots of one size on a square grid, each in a cell whose column and row sum to the same parity. Every
/// edge of a symbol holds a dot, so the dots span the grid. The reader tries the four turns of the grid and their mirror images. In each, the dot stream of
/// <see cref="DotCodeEncoder.CellOrder"/> gives the mask value and the codewords, read from their patterns of five dots in nine, and the Reed-Solomon code over
/// the prime field of 113 corrects them.
/// </summary>
internal sealed class DotCodeReader : MatrixReader
{
    /// <summary>
    /// The most dots a region can hold: half the cells of the largest symbol.
    /// </summary>
    private const int MaximumDots = DotCodeEncoder.MaximumSide * DotCodeEncoder.MaximumSide / 2;

    /// <summary>
    /// The prime field of the Reed-Solomon code, whose generator roots are the powers of 3.
    /// </summary>
    private static readonly PrimeField Field = new(DotCodeEncoder.Modulus, 3);

    /// <summary>
    /// The codeword value of each pattern of nine dots, or -1, the inverse of <see cref="DotCodeEncoder.Patterns"/>.
    /// </summary>
    private static readonly short[] PatternValues = CreatePatternValues();

    /// <inheritdoc/>
    public override BarcodeFamily Families => BarcodeFamily.DotCode;

    /// <inheritdoc/>
    public override void Read(DecodeContext context)
    {
        // The dots of a row stand two modules apart, so the regions bridge gaps of a module or more. The quiet zone is three modules, so one of the gaps finds
        // the symbol whole.
        BitImage bits = context.Bits;
        SymbolRegions regions = context.State<SymbolRegions>();
        List<Dot> dots = [];
        foreach (int gap in (ReadOnlySpan<int>)[4, 8, 16])
        {
            foreach (Rectangle region in regions.Find(context, gap))
            {
                dots.Clear();
                if (FindDots(bits, region, dots))
                {
                    TryRead(context, dots);
                }
            }
        }
    }

    /// <summary>
    /// Finds the dots of a region: its groups of dark pixels, which must be round and of one size.
    /// </summary>
    /// <param name="bits">The image.</param>
    /// <param name="region">The region.</param>
    /// <param name="dots">The dots found.</param>
    /// <returns><see langword="true"/> when the region holds nothing but dots.</returns>
    private static bool FindDots(BitImage bits, Rectangle region, List<Dot> dots)
    {
        int width = region.Width;
        int height = region.Height;
        bool[] seen = new bool[width * height];
        Stack<int> pending = new();
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (seen[(y * width) + x] || !bits[region.Left + x, region.Top + y])
                {
                    continue;
                }

                // The four-connected group of dark pixels from this one.
                long sumX = 0;
                long sumY = 0;
                int count = 0;
                int left = x;
                int right = x;
                int top = y;
                int bottom = y;
                seen[(y * width) + x] = true;
                pending.Push((y * width) + x);
                while (pending.Count > 0)
                {
                    int index = pending.Pop();
                    int px = index % width;
                    int py = index / width;
                    sumX += px;
                    sumY += py;
                    count++;
                    left = Math.Min(left, px);
                    right = Math.Max(right, px);
                    top = Math.Min(top, py);
                    bottom = Math.Max(bottom, py);
                    Visit(bits, region, seen, pending, px - 1, py);
                    Visit(bits, region, seen, pending, px + 1, py);
                    Visit(bits, region, seen, pending, px, py - 1);
                    Visit(bits, region, seen, pending, px, py + 1);
                }

                dots.Add(new Dot(
                    region.Left + ((float)sumX / count) + 0.5F,
                    region.Top + ((float)sumY / count) + 0.5F,
                    right - left + 1,
                    bottom - top + 1));

                if (dots.Count > MaximumDots)
                {
                    return false;
                }
            }
        }

        if (dots.Count < 8)
        {
            return false;
        }

        // A dot is about as wide as it is high and as wide as the others. Specks may be noise, but a large group is a bar or a module of another symbology.
        Span<int> sizes = dots.Count <= 1024 ? stackalloc int[dots.Count] : new int[dots.Count];
        for (int i = 0; i < dots.Count; i++)
        {
            sizes[i] = Math.Max(dots[i].Width, dots[i].Height);
        }

        sizes.Sort();
        int size = sizes[sizes.Length / 2];
        int odd = 0;
        for (int i = dots.Count - 1; i >= 0; i--)
        {
            Dot dot = dots[i];
            if (dot.Width > (size * 3 / 2) + 1 || dot.Height > (size * 3 / 2) + 1)
            {
                return false;
            }

            if (dot.Width < (size / 2) - 1 || dot.Height < (size / 2) - 1)
            {
                dots.RemoveAt(i);
                odd++;
            }
        }

        return odd * 20 <= dots.Count && dots.Count >= 8;
    }

    /// <summary>
    /// Adds a dark pixel of a region not yet seen to the pixels of a group.
    /// </summary>
    /// <param name="bits">The image.</param>
    /// <param name="region">The region.</param>
    /// <param name="seen">The pixels already seen, row by row in the region.</param>
    /// <param name="pending">The pixels of the group still to visit.</param>
    /// <param name="x">The x coordinate in the region.</param>
    /// <param name="y">The y coordinate in the region.</param>
    private static void Visit(BitImage bits, Rectangle region, bool[] seen, Stack<int> pending, int x, int y)
    {
        int index = (y * region.Width) + x;
        if ((uint)x < (uint)region.Width && (uint)y < (uint)region.Height && !seen[index] && bits[region.Left + x, region.Top + y])
        {
            seen[index] = true;
            pending.Push(index);
        }
    }

    /// <summary>
    /// Fits the grid to the dots of a region and decodes it in every turn.
    /// </summary>
    /// <param name="context">The decode.</param>
    /// <param name="dots">The dots.</param>
    private static void TryRead(DecodeContext context, List<Dot> dots)
    {
        float size = 0;
        foreach (Dot dot in dots)
        {
            size += Math.Max(dot.Width, dot.Height);
        }

        size /= dots.Count;
        if (!TryFitAxis(dots, false, size, out float originX, out float moduleX, out int width)
            || !TryFitAxis(dots, true, size, out float originY, out float moduleY, out int height)
            || MathF.Abs(moduleX - moduleY) > (0.1F * MathF.Max(moduleX, moduleY)) + 0.5F
            || width is < DotCodeEncoder.MinimumSide or > DotCodeEncoder.MaximumSide
            || height is < DotCodeEncoder.MinimumSide or > DotCodeEncoder.MaximumSide
            || ((width + height) & 1) == 0)
        {
            return;
        }

        // Each dot takes the cell nearest its centre. The dots stand where the column and the row sum to one parity, and a dot of the other parity is an error.
        bool[] grid = new bool[width * height];
        Span<int> parities = stackalloc int[2];
        int off = 0;
        foreach (Dot dot in dots)
        {
            float u = (dot.X - originX) / moduleX;
            float v = (dot.Y - originY) / moduleY;
            int i = (int)MathF.Round(u);
            int j = (int)MathF.Round(v);
            if (MathF.Abs(u - i) > 0.3F || MathF.Abs(v - j) > 0.3F || (uint)i >= (uint)width || (uint)j >= (uint)height)
            {
                off++;
                continue;
            }

            grid[(j * width) + i] = true;
            parities[(i + j) & 1]++;
        }

        int parity = parities[0] >= parities[1] ? 0 : 1;
        if ((off + parities[1 - parity]) * 20 > dots.Count)
        {
            return;
        }

        for (int k = 0; k < grid.Length; k++)
        {
            if ((((k % width) + (k / width)) & 1) != parity)
            {
                grid[k] = false;
            }
        }

        // The cells span the dots and half a module beyond them.
        float left = originX - (moduleX / 2);
        float top = originY - (moduleY / 2);
        float right = originX + ((width - 0.5F) * moduleX);
        float bottom = originY + ((height - 0.5F) * moduleY);

        // The four turns and the four mirror images: the symbol's columns run along the image's columns or down its rows, either way. The upper left cell of the
        // symbol always holds a dot cell, so it settles half of them.
        for (int orientation = 0; orientation < 8; orientation++)
        {
            bool swap = orientation >= 4;
            bool flipX = (orientation & 1) != 0;
            bool flipY = (orientation & 2) != 0;
            int columns = swap ? height : width;
            int rows = swap ? width : height;
            if (((ImageX(0, 0) + ImageY(0, 0)) & 1) != parity)
            {
                continue;
            }

            bool[] cells = new bool[columns * rows];
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < columns; x++)
                {
                    cells[(y * columns) + x] = grid[(ImageY(x, y) * width) + ImageX(x, y)];
                }
            }

            if (TryDecode(cells, columns, rows, out string text, out string identifier))
            {
                PointF[] corners =
                [
                    Corner(0, 0),
                    Corner(columns - 1, 0),
                    Corner(columns - 1, rows - 1),
                    Corner(0, rows - 1),
                ];

                context.Add(BarcodeFamily.DotCode, text, identifier, corners);
                return;
            }

            int ImageX(int x, int y)
            {
                int i = swap ? y : x;
                return flipX ? width - 1 - i : i;
            }

            int ImageY(int x, int y)
            {
                int j = swap ? x : y;
                return flipY ? height - 1 - j : j;
            }

            PointF Corner(int x, int y) => new(ImageX(x, y) == 0 ? left : right, ImageY(x, y) == 0 ? top : bottom);
        }
    }

    /// <summary>
    /// Fits the grid lines of one axis to the dot centres: the centres group into lines, most lines one module apart, and the module is the span of the lines
    /// over the whole number of modules nearest it.
    /// </summary>
    /// <param name="dots">The dots.</param>
    /// <param name="vertical">Whether the axis runs down the image, so the lines are rows.</param>
    /// <param name="size">The mean size of a dot in pixels.</param>
    /// <param name="origin">The position of the first line.</param>
    /// <param name="module">The distance between two lines.</param>
    /// <param name="count">The number of lines.</param>
    /// <returns><see langword="true"/> when every group of centres lies on a line.</returns>
    private static bool TryFitAxis(List<Dot> dots, bool vertical, float size, out float origin, out float module, out int count)
    {
        origin = 0;
        module = 0;
        count = 0;
        float[] values = new float[dots.Count];
        for (int i = 0; i < dots.Count; i++)
        {
            values[i] = vertical ? dots[i].Y : dots[i].X;
        }

        Array.Sort(values);

        // A new line starts where the centres move on by more than half a dot.
        List<float> lines = [];
        float sum = values[0];
        int members = 1;
        for (int i = 1; i < values.Length; i++)
        {
            if (values[i] - values[i - 1] > size / 2)
            {
                lines.Add(sum / members);
                sum = 0;
                members = 0;
            }

            sum += values[i];
            members++;
        }

        lines.Add(sum / members);
        if (lines.Count < 3)
        {
            return false;
        }

        float[] gaps = new float[lines.Count - 1];
        for (int i = 0; i < gaps.Length; i++)
        {
            gaps[i] = lines[i + 1] - lines[i];
        }

        Array.Sort(gaps);
        float span = lines[^1] - lines[0];
        count = (int)MathF.Round(span / gaps[gaps.Length / 2]) + 1;
        module = span / (count - 1);
        origin = lines[0];
        foreach (float line in lines)
        {
            float position = (line - origin) / module;
            if (MathF.Abs(position - MathF.Round(position)) > 0.25F)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Reads, corrects and decodes the cells of a symbol in one orientation.
    /// </summary>
    /// <param name="cells">The cells, row by row, dark for a dot.</param>
    /// <param name="columns">The number of columns.</param>
    /// <param name="rows">The number of rows.</param>
    /// <param name="text">The data.</param>
    /// <param name="identifier">The symbology identifier.</param>
    /// <returns><see langword="true"/> when the symbol decodes.</returns>
    private static bool TryDecode(bool[] cells, int columns, int rows, out string text, out string identifier)
    {
        text = string.Empty;
        identifier = string.Empty;
        int dataCount = DotCodeEncoder.DataCodewords(columns, rows);
        if (dataCount < 1)
        {
            return false;
        }

        // The stream is the two bits of the mask value, then nine dots for each codeword.
        int[] order = new int[rows * columns / 2];
        DotCodeEncoder.CellOrder(columns, rows, order);
        int total = DotCodeEncoder.CodewordCount(dataCount) + 1;
        int[] codewords = new int[total];
        codewords[0] = (cells[order[0]] ? 2 : 0) | (cells[order[1]] ? 1 : 0);
        int bit = DotCodeEncoder.MaskDots;
        for (int k = 1; k < total; k++)
        {
            int pattern = 0;
            for (int d = 0; d < DotCodeEncoder.DotsPerCodeword; d++)
            {
                pattern = (pattern << 1) | (cells[order[bit++]] ? 1 : 0);
            }

            codewords[k] = Math.Max(0, (int)PatternValues[pattern]);
        }

        // Block s of the step holds every step-th codeword from s, its data first and then its check codewords.
        PrimeReedSolomonField field = new(Field);
        int step = DotCodeEncoder.BlockCount(total);
        Span<int> block = stackalloc int[Field.Modulus - 1];
        for (int start = 0; start < step; start++)
        {
            DotCodeEncoder.BlockSize(total, dataCount + 1, step, start, out int blockData, out int checkCount);
            int length = blockData + checkCount;
            for (int k = 0; k < length; k++)
            {
                block[k] = codewords[start + (k * step)];
            }

            if (ReedSolomonDecoder.Correct(field, block[..length], checkCount, 1) < 0)
            {
                return false;
            }

            for (int k = 0; k < length; k++)
            {
                codewords[start + (k * step)] = block[k];
            }
        }

        // Mask m added successive multiples of its factor to the data codewords.
        int mask = codewords[0];
        if (mask >= DotCodeEncoder.MaskFactors.Length)
        {
            return false;
        }

        int factor = DotCodeEncoder.MaskFactors[mask];
        int[] data = new int[dataCount];
        int weight = 0;
        for (int k = 0; k < dataCount; k++)
        {
            data[k] = (codewords[k + 1] - weight + DotCodeEncoder.Modulus) % DotCodeEncoder.Modulus;
            weight = (weight + factor) % DotCodeEncoder.Modulus;
        }

        return DotCodeBitstream.TryDecode(data, out text, out identifier);
    }

    /// <summary>
    /// Inverts the dot patterns of the codeword values.
    /// </summary>
    /// <returns>The value of each pattern of nine dots, or -1.</returns>
    private static short[] CreatePatternValues()
    {
        short[] values = new short[1 << DotCodeEncoder.DotsPerCodeword];
        values.AsSpan().Fill(-1);
        ReadOnlySpan<ushort> patterns = DotCodeEncoder.Patterns;
        for (int value = 0; value < patterns.Length; value++)
        {
            values[patterns[value]] = (short)value;
        }

        return values;
    }

    /// <summary>
    /// A dot: the centre and the size of a group of dark pixels.
    /// </summary>
    private readonly struct Dot
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Dot"/> struct.
        /// </summary>
        /// <param name="x">The x coordinate of the centre.</param>
        /// <param name="y">The y coordinate of the centre.</param>
        /// <param name="width">The width in pixels.</param>
        /// <param name="height">The height in pixels.</param>
        public Dot(float x, float y, int width, int height)
        {
            this.X = x;
            this.Y = y;
            this.Width = width;
            this.Height = height;
        }

        /// <summary>
        /// Gets the x coordinate of the centre.
        /// </summary>
        public float X { get; }

        /// <summary>
        /// Gets the y coordinate of the centre.
        /// </summary>
        public float Y { get; }

        /// <summary>
        /// Gets the width in pixels.
        /// </summary>
        public int Width { get; }

        /// <summary>
        /// Gets the height in pixels.
        /// </summary>
        public int Height { get; }
    }
}
