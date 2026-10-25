// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Reads Han Xin Code, ISO/IEC 20830, the inverse of <see cref="HanXinEncoder"/>. A symbol is a region of <see cref="SymbolRegions"/> whose corners each hold
/// a finder pattern. Along the edges from three corners the finder pattern is seven dark modules, and from the fourth, the corner finder pattern of the lower
/// left, three, so the runs give the module size and the orientation. The function information, corrected over GF(16), gives the version, the level and the
/// mask, the data cells read row by row, and the Reed-Solomon code over GF(256) corrects each block. The data reads as its Numeric and Byte segments.
/// </summary>
internal sealed class HanXinReader : MatrixReader
{
    private const int NumericMode = 0b0001;
    private const int ByteMode = 0b0011;

    /// <inheritdoc/>
    public override BarcodeFamily Families => BarcodeFamily.HanXin;

    /// <inheritdoc/>
    public override void Read(DecodeContext context)
    {
        // The separator rows and columns inside the finder patterns can be light across the whole symbol, so the regions bridge gaps of up to a module or two.
        // The quiet zone is three modules, so one of the gaps finds the symbol whole.
        BitImage bits = context.Bits;
        SymbolRegions regions = context.State<SymbolRegions>();
        foreach (int gap in (ReadOnlySpan<int>)[2, 4, 8, 16])
        {
            foreach (Rectangle region in regions.Find(context, gap))
            {
                TryRead(context, bits, region);
            }
        }
    }

    /// <summary>
    /// Finds and decodes the symbol of a region.
    /// </summary>
    /// <param name="context">The decode.</param>
    /// <param name="bits">The image.</param>
    /// <param name="region">The region.</param>
    /// <returns><see langword="true"/> when a symbol decodes.</returns>
    private static bool TryRead(DecodeContext context, BitImage bits, Rectangle region)
    {
        // The dark runs along the two edges from each corner of the region, clockwise from the upper left.
        Span<float> runs = stackalloc float[4];
        for (int corner = 0; corner < 4; corner++)
        {
            runs[corner] = CornerRun(bits, region, corner);
        }

        // Three corners run seven modules and the corner finder pattern three.
        int special = 0;
        for (int corner = 1; corner < 4; corner++)
        {
            if (runs[corner] < runs[special])
            {
                special = corner;
            }
        }

        // A finder pattern runs seven modules along each edge from its corner, and the corner finder pattern three.
        float module = 0;
        for (int corner = 0; corner < 4; corner++)
        {
            if (corner != special)
            {
                module += runs[corner] / 14F / 3F;
            }
        }

        if (module < 1 || MathF.Abs((runs[special] / module) - 6) > 2)
        {
            return false;
        }

        // The side is 21 modules and 2 for each version.
        int version = (int)MathF.Round(((region.Width / module) - HanXinEncoder.BaseSide) / 2);
        if (version is < 1 or > HanXinEncoder.VersionCount)
        {
            return false;
        }

        int size = HanXinEncoder.BaseSide + (2 * version);
        ReadOnlySpan<PointF> boxCorners =
        [
            new(region.Left, region.Top),
            new(region.Right, region.Top),
            new(region.Right, region.Bottom),
            new(region.Left, region.Bottom),
        ];

        for (int mirror = 0; mirror < 2; mirror++)
        {
            // The corner finder pattern is the lower left of the symbol, and the upper left follows it clockwise when the symbol is not mirrored.
            int step = mirror == 0 ? 1 : 3;
            PointF bottomLeft = boxCorners[special];
            PointF topLeft = boxCorners[(special + step) % 4];
            PointF topRight = boxCorners[(special + (2 * step)) % 4];
            PointF bottomRight = boxCorners[(special + (3 * step)) % 4];
            ReadOnlySpan<PointF> from = [new(0, 0), new(size, 0), new(size, size), new(0, size)];
            ReadOnlySpan<PointF> to = [topLeft, topRight, bottomRight, bottomLeft];
            PerspectiveTransform transform = PerspectiveTransform.QuadrilateralToQuadrilateral(from, to);
            if (ModuleMatrix.TrySample(bits, transform, size, size, out ModuleMatrix matrix) && TryDecode(matrix, out string text))
            {
                context.Add(BarcodeFamily.HanXin, text, "]h0", [topLeft, topRight, bottomRight, bottomLeft]);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns the dark run along the two edges of a region from one of its corners, added together.
    /// </summary>
    /// <param name="bits">The image.</param>
    /// <param name="region">The region.</param>
    /// <param name="corner">The corner: 0 upper left, 1 upper right, 2 lower right, 3 lower left.</param>
    /// <returns>The run in pixels: 14 modules for a finder pattern and 6 for the corner finder pattern.</returns>
    private static float CornerRun(BitImage bits, Rectangle region, int corner)
    {
        int x = corner is 1 or 2 ? region.Right - 1 : region.Left;
        int y = corner is 2 or 3 ? region.Bottom - 1 : region.Top;
        int dx = corner is 1 or 2 ? -1 : 1;
        int dy = corner is 2 or 3 ? -1 : 1;
        int along = 0;
        while (along < region.Width && bits[x + (dx * along), y])
        {
            along++;
        }

        int down = 0;
        while (down < region.Height && bits[x, y + (dy * down)])
        {
            down++;
        }

        return along + down;
    }

    /// <summary>
    /// Decodes a module matrix: the function information, the data cells under the mask, the blocks and the data.
    /// </summary>
    /// <param name="matrix">The modules.</param>
    /// <param name="text">The data.</param>
    /// <returns><see langword="true"/> when the symbol decodes.</returns>
    private static bool TryDecode(ModuleMatrix matrix, out string text)
    {
        text = string.Empty;
        int size = matrix.Columns;
        if (!TryReadFunctionInformation(matrix, out int version, out int level, out int mask) || HanXinEncoder.BaseSide + (2 * version) != size)
        {
            return false;
        }

        // The codewords fill the data cells row by row, the most significant bit first, under the mask.
        int[] order = new int[size * size];
        int cells = HanXinEncoder.DataCellOrder(version, order);
        ReadOnlySpan<byte> groups = HanXinEncoder.BlockGroups(version, level);
        int total = 0;
        for (int g = 0; g < groups.Length; g += 3)
        {
            total += groups[g] * (groups[g + 1] + groups[g + 2]);
        }

        if (total * 8 > cells)
        {
            return false;
        }

        int[] placed = new int[total];
        for (int bit = 0; bit < total * 8; bit++)
        {
            int index = order[bit];
            bool dark = matrix.At(index) ^ (((HanXinEncoder.MaskLayer((index % size) + 1, (index / size) + 1) >> mask) & 1) != 0);
            placed[bit >> 3] |= (dark ? 1 : 0) << (7 - (bit & 7));
        }

        // The placement takes codewords 0, 13, 26 and so on, then 1, 14, 27 and so on.
        int[] codewords = new int[total];
        int next = 0;
        for (int start = 0; start < HanXinEncoder.InterleaveStep; start++)
        {
            for (int c = start; c < total; c += HanXinEncoder.InterleaveStep)
            {
                codewords[c] = placed[next++];
            }
        }

        BinaryReedSolomonField field = new(HanXinEncoder.Field);
        List<byte> data = [];
        int position = 0;
        for (int g = 0; g < groups.Length; g += 3)
        {
            int dataLength = groups[g + 1];
            int checkLength = groups[g + 2];
            for (int b = 0; b < groups[g]; b++)
            {
                Span<int> block = codewords.AsSpan(position, dataLength + checkLength);
                if (ReedSolomonDecoder.Correct(field, block, checkLength, 1) < 0)
                {
                    return false;
                }

                foreach (int codeword in block[..dataLength])
                {
                    data.Add((byte)codeword);
                }

                position += dataLength + checkLength;
            }
        }

        return TryDecodeData(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(data), out text);
    }

    /// <summary>
    /// Reads the function information, either copy: three 4-bit words of the version plus 20, the level and the mask, and four check words over GF(16).
    /// </summary>
    /// <param name="matrix">The modules.</param>
    /// <param name="version">The version.</param>
    /// <param name="level">The level, 0 for L1 to 3 for L4.</param>
    /// <param name="mask">The mask, 0 to 3.</param>
    /// <returns><see langword="true"/> when a copy corrects.</returns>
    private static bool TryReadFunctionInformation(ModuleMatrix matrix, out int version, out int level, out int mask)
    {
        int size = matrix.Columns;
        Span<int> words = stackalloc int[7];
        BinaryReedSolomonField field = new(HanXinEncoder.FunctionField);
        for (int copy = 0; copy < 2; copy++)
        {
            words.Clear();
            for (int i = 0; i < 28; i++)
            {
                HanXinEncoder.FunctionPositions(i, size, out int x1, out int y1, out int x2, out int y2);
                bool dark = copy == 0 ? matrix[y1, x1] : matrix[y2, x2];
                words[i >> 2] |= (dark ? 1 : 0) << (3 - (i & 3));
            }

            if (ReedSolomonDecoder.Correct(field, words, 4, 1) >= 0)
            {
                int value = (words[0] << 8) | (words[1] << 4) | words[2];
                version = (value >> 4) - 20;
                level = (value >> 2) & 3;
                mask = value & 3;
                if (version is >= 1 and <= HanXinEncoder.VersionCount)
                {
                    return true;
                }
            }
        }

        version = 0;
        level = 0;
        mask = 0;
        return false;
    }

    /// <summary>
    /// Decodes the data bits: Numeric segments of three digits in 10 bits whose terminator gives the digits of the last group, and Byte segments of a 13-bit
    /// count and the bytes, up to the mode indicator 0000.
    /// </summary>
    /// <param name="data">The data codewords.</param>
    /// <param name="text">The data.</param>
    /// <returns><see langword="true"/> when the segments are valid.</returns>
    private static bool TryDecodeData(ReadOnlySpan<byte> data, out string text)
    {
        text = string.Empty;
        BitStreamReader bits = new(data, data.Length * 8);
        DecodedText decoded = new();
        while (bits.Remaining >= 4)
        {
            int mode = bits.Read(4);
            if (mode == 0)
            {
                break;
            }

            if (mode == NumericMode)
            {
                // Each group is three digits, and the terminator 1111111101 to 1111111111 gives the digits of the last group, one to three.
                int pending = -1;
                while (true)
                {
                    if (bits.Remaining < 10)
                    {
                        return false;
                    }

                    int value = bits.Read(10);
                    if (value >= HanXinEncoder.NumericTerminator)
                    {
                        int digits = value - HanXinEncoder.NumericTerminator + 1;
                        if (pending < 0 || (digits < 3 && pending >= (digits == 1 ? 10 : 100)))
                        {
                            return false;
                        }

                        AppendDigits(decoded, pending, digits);
                        break;
                    }

                    if (value >= 1000)
                    {
                        return false;
                    }

                    if (pending >= 0)
                    {
                        AppendDigits(decoded, pending, 3);
                    }

                    pending = value;
                }

                continue;
            }

            if (mode != ByteMode || bits.Remaining < HanXinEncoder.ByteCountBits)
            {
                return false;
            }

            int count = bits.Read(HanXinEncoder.ByteCountBits);
            if (count * 8 > bits.Remaining)
            {
                return false;
            }

            for (int i = 0; i < count; i++)
            {
                decoded.AppendByte((byte)bits.Read(8));
            }
        }

        text = decoded.ToString();
        return text.Length > 0;
    }

    /// <summary>
    /// Appends a number as a count of digits with leading zeros.
    /// </summary>
    /// <param name="decoded">The data.</param>
    /// <param name="value">The number.</param>
    /// <param name="digits">The number of digits.</param>
    private static void AppendDigits(DecodedText decoded, int value, int digits)
    {
        Span<char> characters = stackalloc char[3];
        for (int i = digits - 1; i >= 0; i--)
        {
            characters[i] = (char)('0' + (value % 10));
            value /= 10;
        }

        decoded.AppendText(characters[..digits]);
    }
}
