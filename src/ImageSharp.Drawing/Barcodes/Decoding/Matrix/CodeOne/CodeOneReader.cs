// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Globalization;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Reads Code One, AIM USS Code One, the inverse of <see cref="CodeOneEncoder"/>. A symbol is a region of <see cref="SymbolRegions"/> whose proportions fit a
/// version in one of its four turns. The fixed cells of the version, the centre pattern, the risers and the black dots, must read as the version has them.
/// The bricks of <see cref="CodeOneEncoder.CodewordPositions(CodeOneVersion, Span{int})"/> read the codewords, the Reed-Solomon code corrects the interleaved blocks, and the data reads
/// through the ASCII, C40, Text, X12, Decimal and Byte modes, or as the number of an S version.
/// </summary>
internal sealed class CodeOneReader : MatrixReader
{
    private const int Fnc1 = 232;

    /// <inheritdoc/>
    public override BarcodeFamily Families => BarcodeFamily.CodeOne;

    /// <inheritdoc/>
    public override void Read(DecodeContext context)
    {
        // The centre pattern has light rows across the whole symbol, so the regions bridge gaps of up to a module or two.
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
    /// Tries every version and turn whose proportions fit a region.
    /// </summary>
    /// <param name="context">The decode.</param>
    /// <param name="bits">The image.</param>
    /// <param name="region">The region.</param>
    /// <returns><see langword="true"/> when a symbol decodes.</returns>
    private static bool TryRead(DecodeContext context, BitImage bits, Rectangle region)
    {
        ReadOnlySpan<PointF> box =
        [
            new(region.Left, region.Top),
            new(region.Right, region.Top),
            new(region.Right, region.Bottom),
            new(region.Left, region.Bottom),
        ];

        for (CodeOneVersion version = CodeOneVersion.A; version <= CodeOneVersion.T48; version++)
        {
            CodeOneEncoder.Dimensions(version, out int rows, out int columns, out _, out _, out _);
            for (int turn = 0; turn < 4; turn++)
            {
                // A quarter turn puts the columns of the symbol down the image.
                bool across = (turn & 1) == 0;
                float moduleX = region.Width / (float)(across ? columns : rows);
                float moduleY = region.Height / (float)(across ? rows : columns);
                if (moduleX < 1 || MathF.Abs(moduleX - moduleY) > 0.2F * moduleX)
                {
                    continue;
                }

                for (int mirror = 0; mirror < 2; mirror++)
                {
                    // Corner k of the symbol, clockwise from the upper left, lies at corner k + turn of the region, or counterclockwise when mirrored.
                    PointF topLeft = box[Corner(0, turn, mirror)];
                    PointF topRight = box[Corner(1, turn, mirror)];
                    PointF bottomRight = box[Corner(2, turn, mirror)];
                    PointF bottomLeft = box[Corner(3, turn, mirror)];
                    ReadOnlySpan<PointF> from = [new(0, 0), new(columns, 0), new(columns, rows), new(0, rows)];
                    ReadOnlySpan<PointF> to = [topLeft, topRight, bottomRight, bottomLeft];
                    PerspectiveTransform transform = PerspectiveTransform.QuadrilateralToQuadrilateral(from, to);
                    if (ModuleMatrix.TrySample(bits, transform, rows, columns, out ModuleMatrix matrix)
                        && HasFixedCells(matrix, version)
                        && TryDecode(matrix, version, out string text, out string identifier))
                    {
                        context.Add(BarcodeFamily.CodeOne, text, identifier, [topLeft, topRight, bottomRight, bottomLeft]);
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Returns the corner of the region that a corner of the symbol takes in a turn.
    /// </summary>
    /// <param name="corner">The corner of the symbol, clockwise from the upper left.</param>
    /// <param name="turn">The quarter turns clockwise.</param>
    /// <param name="mirror">1 when the symbol is mirrored.</param>
    /// <returns>The corner of the region, clockwise from the upper left.</returns>
    private static int Corner(int corner, int turn, int mirror) => mirror == 0 ? (corner + turn) % 4 : (4 - corner + turn) % 4;

    /// <summary>
    /// Returns whether nine in ten of the fixed cells of a version read as the version has them.
    /// </summary>
    /// <param name="matrix">The modules.</param>
    /// <param name="version">The version.</param>
    /// <returns><see langword="true"/> when the fixed cells match.</returns>
    private static bool HasFixedCells(ModuleMatrix matrix, CodeOneVersion version)
    {
        sbyte[] cells = new sbyte[matrix.Rows * matrix.Columns];
        CodeOneEncoder.FixedCells(version, cells);
        int count = 0;
        int wrong = 0;
        for (int i = 0; i < cells.Length; i++)
        {
            if (cells[i] >= 0)
            {
                count++;
                wrong += (cells[i] == 1) != matrix.At(i) ? 1 : 0;
            }
        }

        return wrong * 10 <= count;
    }

    /// <summary>
    /// Reads, corrects and decodes the codewords of a sampled symbol.
    /// </summary>
    /// <param name="matrix">The modules.</param>
    /// <param name="version">The version.</param>
    /// <param name="text">The data.</param>
    /// <param name="identifier">The symbology identifier.</param>
    /// <returns><see langword="true"/> when the symbol decodes.</returns>
    private static bool TryDecode(ModuleMatrix matrix, CodeOneVersion version, out string text, out string identifier)
    {
        text = string.Empty;
        identifier = "]o0";
        CodeOneEncoder.Dimensions(version, out _, out _, out int dataCount, out int checkCount, out int blocks);
        bool numeric = version is CodeOneVersion.S10 or CodeOneVersion.S20 or CodeOneVersion.S30;
        int size = numeric ? 5 : 8;
        int total = dataCount + checkCount;
        int[] positions = new int[total * size];
        CodeOneEncoder.CodewordPositions(version, positions);
        int[] codewords = new int[total];
        for (int k = 0; k < total; k++)
        {
            for (int b = 0; b < size; b++)
            {
                codewords[k] = (codewords[k] << 1) | (matrix.At(positions[(k * size) + b]) ? 1 : 0);
            }
        }

        // Data codeword i belongs to block i mod b, and check codeword j of block k is at the data count plus j x b + k.
        BinaryReedSolomonField field = new(numeric ? CodeOneEncoder.Field5 : CodeOneEncoder.Field8);
        int perBlock = checkCount / blocks;
        int dataPerBlock = dataCount / blocks;
        int[] data = new int[dataCount];
        Span<int> word = stackalloc int[dataPerBlock + perBlock];
        for (int block = 0; block < blocks; block++)
        {
            for (int i = 0; i < dataPerBlock; i++)
            {
                word[i] = codewords[block + (i * blocks)];
            }

            for (int j = 0; j < perBlock; j++)
            {
                word[dataPerBlock + j] = codewords[dataCount + (j * blocks) + block];
            }

            if (ReedSolomonDecoder.Correct(field, word, perBlock, 0) < 0)
            {
                return false;
            }

            for (int i = 0; i < dataPerBlock; i++)
            {
                data[block + (i * blocks)] = word[i];
            }
        }

        if (numeric)
        {
            // An S version carries one number plus one in its 5-bit codewords, the most significant first.
            ulong value = 0;
            foreach (int codeword in data)
            {
                value = (value << 5) | (uint)codeword;
            }

            if (value == 0)
            {
                return false;
            }

            text = (value - 1).ToString(CultureInfo.InvariantCulture);
            return true;
        }

        return TryDecodeData(data, out text, out identifier);
    }

    /// <summary>
    /// Decodes the data codewords of versions A to H and T.
    /// </summary>
    /// <param name="codewords">The data codewords.</param>
    /// <param name="text">The data.</param>
    /// <param name="identifier">The symbology identifier.</param>
    /// <returns><see langword="true"/> when the codewords are valid.</returns>
    private static bool TryDecodeData(ReadOnlySpan<int> codewords, out string text, out string identifier)
    {
        text = string.Empty;
        identifier = "]o0";
        DecodedText decoded = new();
        bool gs1 = false;
        bool upperShift = false;
        int i = 0;
        while (i < codewords.Length)
        {
            int codeword = codewords[i++];
            if (codeword == CodeOneEncoder.Pad)
            {
                break;
            }

            if (codeword == 0)
            {
                return false;
            }

            if (codeword <= 128)
            {
                decoded.AppendByte((byte)(codeword - 1 + (upperShift ? 128 : 0)));
                upperShift = false;
                continue;
            }

            if (codeword < CodeOneEncoder.LatchC40)
            {
                int pair = codeword - CodeOneEncoder.DigitPairBase;
                decoded.AppendByte((byte)('0' + (pair / 10)));
                decoded.AppendByte((byte)('0' + (pair % 10)));
                continue;
            }

            // A codeword whose first four bits are 1111 starts Decimal mode, and its last four bits are the first of the decimal bits.
            if ((codeword >> 4) == CodeOneEncoder.DecimalLatch)
            {
                if (!TryDecodeDecimal(codewords, ref i, decoded))
                {
                    return false;
                }

                continue;
            }

            switch (codeword)
            {
                case CodeOneEncoder.LatchC40:
                case CodeOneEncoder.LatchText:
                case CodeOneEncoder.LatchX12:
                    DataMatrixBitstream.Triplets triplets = codeword switch
                    {
                        CodeOneEncoder.LatchC40 => DataMatrixBitstream.Triplets.C40,
                        CodeOneEncoder.LatchText => DataMatrixBitstream.Triplets.Text,
                        _ => DataMatrixBitstream.Triplets.X12,
                    };

                    if (!DataMatrixBitstream.TryDecodeTriplets(codewords, ref i, triplets, CodeOneEncoder.Unlatch, decoded, ref gs1))
                    {
                        return false;
                    }

                    break;
                case CodeOneEncoder.LatchByte:
                    if (!TryDecodeBytes(codewords, ref i, decoded))
                    {
                        return false;
                    }

                    break;
                case CodeOneEncoder.UpperShift:
                    upperShift = true;
                    break;
                case Fnc1:
                    // FNC1 in the first position marks GS1 data, and any later one is the group separator.
                    if (i == 1)
                    {
                        gs1 = true;
                    }
                    else
                    {
                        decoded.AppendChar(Gs1Data.Separator);
                    }

                    break;
                default:
                    return false;
            }
        }

        text = decoded.ToString();
        identifier = gs1 ? "]o1" : "]o0";
        return text.Length > 0;
    }

    /// <summary>
    /// Decodes Decimal mode: three digits as their value plus one in 10 bits, until the unlatch 111111. The bits to the end of the codeword after the unlatch
    /// hold one more digit plus one in 4 bits, or 1111, and then 01. Decimal mode ends without an unlatch at the end of the data, and when one codeword is left,
    /// which holds the last digits in ASCII mode.
    /// </summary>
    /// <param name="codewords">The data codewords.</param>
    /// <param name="i">The index of the codeword after the one that latched, moved past Decimal mode.</param>
    /// <param name="decoded">The data.</param>
    /// <returns><see langword="true"/> when the bits are valid.</returns>
    private static bool TryDecodeDecimal(ReadOnlySpan<int> codewords, ref int i, DecodedText decoded)
    {
        // The decimal bits start with the last four bits of the codeword that latched.
        int bit = (i * 8) - 4;
        int end = codewords.Length * 8;
        Span<char> digits = stackalloc char[3];
        while (true)
        {
            if (end - bit >= 6 && Peek(codewords, bit, 6) == CodeOneEncoder.DecimalUnlatch)
            {
                bit += 6;
                ReadTail(codewords, ref bit, decoded);
                break;
            }

            // One codeword left at a codeword boundary reads in ASCII mode.
            if ((bit & 7) == 0 && end - bit == 8)
            {
                break;
            }

            if (end - bit < 10)
            {
                // The free bits of the last codeword.
                ReadTail(codewords, ref bit, decoded);
                break;
            }

            int value = Peek(codewords, bit, 10);
            bit += 10;
            if (value is < 1 or > 1000)
            {
                return false;
            }

            (value - 1).TryFormat(digits, out _, "D3", CultureInfo.InvariantCulture);
            decoded.AppendText(digits);
        }

        i = (bit + 7) / 8;
        return true;
    }

    /// <summary>
    /// Reads the bits to the end of the codeword after Decimal mode: a digit plus one in 4 bits, or 1111, when 4 or 6 bits are left, then 01 when 2 are.
    /// </summary>
    /// <param name="codewords">The data codewords.</param>
    /// <param name="bit">The position of the first bit, moved to the end of the codeword.</param>
    /// <param name="decoded">The data.</param>
    private static void ReadTail(ReadOnlySpan<int> codewords, ref int bit, DecodedText decoded)
    {
        int free = (8 - (bit & 7)) & 7;
        if (free >= 4)
        {
            int digit = Peek(codewords, bit, 4);
            if (digit is >= 1 and <= 10)
            {
                decoded.AppendByte((byte)('0' + digit - 1));
            }

            free -= 4;
            bit += 4;
        }

        bit += free;
    }

    /// <summary>
    /// Reads bits of the codewords as a number, the most significant first.
    /// </summary>
    /// <param name="codewords">The 8-bit codewords.</param>
    /// <param name="bit">The position of the first bit.</param>
    /// <param name="count">The number of bits.</param>
    /// <returns>The number.</returns>
    private static int Peek(ReadOnlySpan<int> codewords, int bit, int count)
    {
        int value = 0;
        for (int k = 0; k < count; k++, bit++)
        {
            value = (value << 1) | ((codewords[bit >> 3] >> (7 - (bit & 7))) & 1);
        }

        return value;
    }

    /// <summary>
    /// Decodes Byte mode: a length of one codeword below 250, two from 250, or 0 for the rest of the data, then the bytes.
    /// </summary>
    /// <param name="codewords">The data codewords.</param>
    /// <param name="i">The index of the length, moved past the bytes.</param>
    /// <param name="decoded">The data.</param>
    /// <returns><see langword="true"/> when the bytes are there.</returns>
    private static bool TryDecodeBytes(ReadOnlySpan<int> codewords, ref int i, DecodedText decoded)
    {
        if (i >= codewords.Length)
        {
            return false;
        }

        int first = codewords[i++];
        int length;
        if (first == 0)
        {
            length = codewords.Length - i;
        }
        else if (first < 250)
        {
            length = first;
        }
        else
        {
            if (i >= codewords.Length)
            {
                return false;
            }

            length = ((first - 249) * 250) + codewords[i++];
        }

        if (length > codewords.Length - i)
        {
            return false;
        }

        for (int k = 0; k < length; k++)
        {
            decoded.AppendByte((byte)codewords[i++]);
        }

        return true;
    }
}
