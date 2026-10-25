// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Decodes the module matrix of a QR Code, Micro QR Code or Rectangular Micro QR Code symbol: the format information and the version information, the data
/// mask, the codewords in the module order of <see cref="QrEncoder.DataModuleOrder"/>, the blocks of Table 9 and their Reed-Solomon correction, and the bit
/// stream through <see cref="QrBitstream"/>.
/// </summary>
internal static class QrSymbolDecoder
{
    /// <summary>
    /// The most bit errors the format and version information correct: their codes have a distance of at least 7, section 6.9 and 6.10.
    /// </summary>
    private const int MaximumBitErrors = 3;

    /// <summary>
    /// Returns the version of a QR Code module matrix: from its size up to version 6, and from the version information, Annex D, from version 7.
    /// </summary>
    /// <param name="matrix">The modules.</param>
    /// <returns>The version, or -1 when the version information does not read.</returns>
    public static int ReadVersion(ModuleMatrix matrix)
    {
        int n = matrix.Columns;
        int version = (n - 17) / 4;
        if (version < 7)
        {
            return version;
        }

        // Figure 28: bit 17 - i at row 5 - i / 3 and column n - 9 - i mod 3 of the upper right block, transposed in the lower left.
        int upper = 0;
        int lower = 0;
        for (int i = 0; i < 18; i++)
        {
            int a = i / 3;
            int b = i % 3;
            upper = (upper << 1) | (matrix[5 - a, n - 9 - b] ? 1 : 0);
            lower = (lower << 1) | (matrix[n - 9 - b, 5 - a] ? 1 : 0);
        }

        int best = -1;
        int bestDistance = MaximumBitErrors + 1;
        for (int candidate = 7; candidate <= 40; candidate++)
        {
            int code = QrEncoder.BchCode(candidate, 12, QrEncoder.VersionGenerator);
            int distance = Math.Min(BitOperations.PopCount((uint)(code ^ upper)), BitOperations.PopCount((uint)(code ^ lower)));
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>
    /// Decodes a module matrix of a symbol size.
    /// </summary>
    /// <param name="matrix">The modules, as many rows and columns as the size.</param>
    /// <param name="size">The symbol size.</param>
    /// <param name="text">The data.</param>
    /// <param name="identifier">The symbology identifier.</param>
    /// <returns><see langword="true"/> when the symbol decodes.</returns>
    public static bool TryDecode(ModuleMatrix matrix, QrEncoder.SymbolSize size, out string text, out string identifier)
    {
        text = string.Empty;
        identifier = string.Empty;
        if (!TryReadFormat(matrix, size, out QrErrorCorrectionLevel level, out int mask))
        {
            return false;
        }

        int errorCount = size.ErrorCodewords(level);
        if (errorCount < 0)
        {
            return false;
        }

        // The bits of the data modules in their order, unmasked. The codeword stream is the data codewords, the short last one of M1 and M3 in 4 bits, and
        // then the error correction codewords.
        int cells = size.Rows * size.Columns;
        int[] order = new int[cells];
        int modules = QrEncoder.DataModuleOrder(size, order);
        int total = size.TotalCodewords;
        int dataCount = total - errorCount;
        int[] stream = new int[total];
        int bit = 0;
        for (int i = 0; i < total && bit < modules; i++)
        {
            int length = size.ShortLastCodeword && i == dataCount - 1 ? 4 : 8;
            int codeword = 0;
            for (int k = 0; k < length; k++, bit++)
            {
                int index = order[bit];
                int column = index % size.Columns;
                int row = index / size.Columns;
                bool dark = matrix.At(index) ^ (((QrEncoder.MaskLayer(row, column) >> mask) & 1) != 0);
                codeword = (codeword << 1) | (dark ? 1 : 0);
            }

            stream[i] = codeword << (8 - length);
        }

        // Section 6.6: the data codewords of the blocks in turn, the shorter blocks first, then their error correction codewords in turn.
        int blocks = size.Blocks(level);
        int shortData = dataCount / blocks;
        int longBlocks = dataCount % blocks;
        int shortBlocks = blocks - longBlocks;
        int errorPerBlock = errorCount / blocks;
        int[][] blockCodewords = new int[blocks][];
        for (int b = 0; b < blocks; b++)
        {
            blockCodewords[b] = new int[(b < shortBlocks ? shortData : shortData + 1) + errorPerBlock];
        }

        int position = 0;
        for (int i = 0; i <= shortData; i++)
        {
            for (int b = 0; b < blocks; b++)
            {
                int blockData = b < shortBlocks ? shortData : shortData + 1;
                if (i < blockData)
                {
                    blockCodewords[b][i] = stream[position++];
                }
            }
        }

        for (int i = 0; i < errorPerBlock; i++)
        {
            for (int b = 0; b < blocks; b++)
            {
                int blockData = b < shortBlocks ? shortData : shortData + 1;
                blockCodewords[b][blockData + i] = stream[position++];
            }
        }

        BinaryReedSolomonField field = new(QrEncoder.Field);
        byte[] data = new byte[dataCount];
        int written = 0;
        for (int b = 0; b < blocks; b++)
        {
            int[] codewords = blockCodewords[b];
            if (ReedSolomonDecoder.Correct(field, codewords, errorPerBlock, 0) < 0)
            {
                return false;
            }

            for (int i = 0; i < codewords.Length - errorPerBlock; i++)
            {
                data[written++] = (byte)codewords[i];
            }
        }

        return QrBitstream.TryDecode(data, size.DataBits(level), size, out text, out identifier);
    }

    /// <summary>
    /// Reads the format information: the error correction level and the data mask of QR Code, section 6.9.1, the symbol number and the data mask of Micro QR
    /// Code, section 6.9.2, and the level and the version of Rectangular Micro QR Code, each the nearest code word of its BCH or Golay code.
    /// </summary>
    /// <param name="matrix">The modules.</param>
    /// <param name="size">The symbol size, whose version a Micro QR Code or Rectangular Micro QR Code format must name.</param>
    /// <param name="level">The error correction level.</param>
    /// <param name="mask">The data mask pattern reference of Table 10.</param>
    /// <returns><see langword="true"/> when a copy of the format information reads.</returns>
    private static bool TryReadFormat(ModuleMatrix matrix, QrEncoder.SymbolSize size, out QrErrorCorrectionLevel level, out int mask)
    {
        level = QrErrorCorrectionLevel.L;
        mask = QrEncoder.RectangularMask;
        int bitCount = size.Format == QrFormat.Rectangular ? 18 : 15;
        int copies = size.Format == QrFormat.Micro ? 1 : 2;
        Span<int> read = stackalloc int[2];
        for (int copy = 0; copy < copies; copy++)
        {
            for (int i = 0; i < bitCount; i++)
            {
                QrEncoder.FormatPosition(size, i, copy, out int row, out int column);
                read[copy] = (read[copy] << 1) | (matrix[row, column] ? 1 : 0);
            }
        }

        int bestDistance = MaximumBitErrors + 1;
        if (size.Format == QrFormat.Full)
        {
            // The level indicator of Table 12 and the mask reference.
            for (int candidate = 0; candidate < 32; candidate++)
            {
                int code = QrEncoder.BchCode(candidate, 10, QrEncoder.FormatGenerator) ^ QrEncoder.FormatMask;
                int distance = Math.Min(BitOperations.PopCount((uint)(code ^ read[0])), BitOperations.PopCount((uint)(code ^ read[1])));
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    level = (QrErrorCorrectionLevel)QrEncoder.LevelIndicators.IndexOf((byte)(candidate >> 3));
                    mask = candidate & 7;
                }
            }
        }
        else if (size.Format == QrFormat.Micro)
        {
            // The symbol number of Table 13 and the two-bit mask reference.
            for (int candidate = 0; candidate < 32; candidate++)
            {
                int code = QrEncoder.BchCode(candidate, 10, QrEncoder.FormatGenerator) ^ QrEncoder.MicroFormatMask;
                int distance = BitOperations.PopCount((uint)(code ^ read[0]));
                int symbolNumber = QrEncoder.MicroSymbolNumbers.IndexOf((sbyte)(candidate >> 2));
                if (distance < bestDistance && symbolNumber >= 0 && (symbolNumber / 3) + 1 == size.Version)
                {
                    bestDistance = distance;
                    level = (QrErrorCorrectionLevel)(symbolNumber % 3);
                    mask = QrEncoder.MicroMasks[candidate & 3];
                }
            }
        }
        else
        {
            // The level, 0 for M and 1 for H, and the version indicator, each copy under its own mask.
            for (int candidate = 0; candidate < 64; candidate++)
            {
                if ((candidate & 31) != size.Version - 1)
                {
                    continue;
                }

                int code = QrEncoder.BchCode(candidate, 12, QrEncoder.VersionGenerator);
                int distance = Math.Min(
                    BitOperations.PopCount((uint)(code ^ QrEncoder.RectangularFormatMaskLeft ^ read[0])),
                    BitOperations.PopCount((uint)(code ^ QrEncoder.RectangularFormatMaskRight ^ read[1])));
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    level = (candidate >> 5) == 1 ? QrErrorCorrectionLevel.H : QrErrorCorrectionLevel.M;
                }
            }
        }

        return bestDistance <= MaximumBitErrors;
    }
}
