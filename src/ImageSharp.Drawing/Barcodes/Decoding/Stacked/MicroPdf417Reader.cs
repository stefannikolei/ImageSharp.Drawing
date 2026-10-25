// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Reads MicroPDF417, ISO/IEC 24728. <see cref="MicroPdf417Rows"/> collects the rows the scan lines read into the sizes of
/// <see cref="MicroPdf417Encoder.Sizes"/> they fit, the size with the most reads builds its symbol matrix, the Reed-Solomon code over GF(929) corrects it, and
/// <see cref="Pdf417Text"/> decodes its data codewords, which have no Symbol Length Descriptor. The CC-A and CC-B 2D components of GS1 Composite symbols read
/// through the same rows and correction.
/// </summary>
internal sealed class MicroPdf417Reader : StackedReader
{
    /// <summary>
    /// The field of the error correction: the integers modulo 929 with the primitive element 3.
    /// </summary>
    private static readonly PrimeField Field = new(929, 3);

    /// <inheritdoc/>
    public override BarcodeFamily Families => BarcodeFamily.MicroPdf417;

    /// <inheritdoc/>
    public override void Read(DecodeContext context, BitImage bits, bool turned)
    {
        List<CorrectedSymbol> found = [];
        Collect(context, bits, turned, MicroPdf417Encoder.Sizes, true, found);
        foreach (CorrectedSymbol symbol in found)
        {
            // A symbol whose data starts with the codeword 920 is the CC-B 2D component of a GS1 Composite symbol, which its own reader reads.
            if (!symbol.IsComposite && Pdf417Text.TryDecode(symbol.Codewords, Pdf417Text.StartMode.Byte, out string? text))
            {
                context.Add(BarcodeFamily.MicroPdf417, text, "]L2", symbol.Corners);
            }
        }
    }

    /// <summary>
    /// Scans one orientation of an image in both directions for symbols of the sizes of a table and collects the ones the Reed-Solomon code corrects.
    /// </summary>
    /// <param name="context">The decode.</param>
    /// <param name="bits">The image in this orientation.</param>
    /// <param name="turned">Whether the image is the turned image.</param>
    /// <param name="table">The sizes.</param>
    /// <param name="hasLeft">Whether a three column size of the table has a left row address pattern.</param>
    /// <param name="found">The symbols, with their data codewords.</param>
    public static void Collect(DecodeContext context, BitImage bits, bool turned, ReadOnlySpan<byte> table, bool hasLeft, List<CorrectedSymbol> found)
    {
        for (int direction = 0; direction < 2; direction++)
        {
            MicroPdf417Rows rows = MicroPdf417Rows.Scan(context, bits, table, hasLeft, direction == 1);
            if (TryCorrect(rows, table, out int[] codewords, out CodewordVotes votes, out _))
            {
                // The corners as the symbol reads: a symbol read right to left is upside down.
                PointF topLeft = Map(votes.Left, votes.Top, bits, turned);
                PointF topRight = Map(votes.Right, votes.Top, bits, turned);
                PointF bottomRight = Map(votes.Right, votes.Bottom + 1, bits, turned);
                PointF bottomLeft = Map(votes.Left, votes.Bottom + 1, bits, turned);
                PointF[] corners = direction == 0 ? [topLeft, topRight, bottomRight, bottomLeft] : [bottomRight, bottomLeft, topLeft, topRight];
                found.Add(new CorrectedSymbol(codewords, corners));
            }
        }
    }

    /// <summary>
    /// Builds the symbol matrix of the size that the most rows fit, corrects it and returns its codewords.
    /// </summary>
    /// <param name="rows">The rows.</param>
    /// <param name="table">The sizes.</param>
    /// <param name="codewords">The codewords of the symbol matrix, corrected.</param>
    /// <param name="votes">The votes of the size.</param>
    /// <param name="size">The offset of the size in the table.</param>
    /// <returns><see langword="true"/> when a size corrects.</returns>
    public static bool TryCorrect(MicroPdf417Rows rows, ReadOnlySpan<byte> table, out int[] codewords, out CodewordVotes votes, out int size)
    {
        codewords = [];
        votes = null!;
        size = -1;

        // The sizes in the order of their reads, the most first.
        List<int> offsets = [.. rows.Sizes.Keys];
        offsets.Sort((a, b) => rows.Reads(b).CompareTo(rows.Reads(a)));
        foreach (int offset in offsets)
        {
            MicroPdf417Encoder.SymbolSize symbolSize = MicroPdf417Rows.SizeAt(table, offset);
            CodewordVotes candidate = rows.Sizes[offset];
            int columns = symbolSize.Columns;
            int total = columns * symbolSize.Rows;
            int checkCount = symbolSize.ErrorCorrection;
            int[] matrix = new int[total];
            int missing = 0;
            for (int row = 0; row < symbolSize.Rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    int codeword = candidate.Codeword(row, column);
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
                continue;
            }

            codewords = matrix[..(total - checkCount)];
            votes = candidate;
            size = offset;
            return true;
        }

        return false;
    }
}
