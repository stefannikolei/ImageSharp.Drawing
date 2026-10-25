// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Reads Ultracode, AIM ISS Ultracode, the inverse of <see cref="UltracodeEncoder"/>. A symbol is a region of <see cref="SymbolRegions"/> framed by black
/// edges. Its height is one of four counts of tile rows, and the timing patterns and the finder at the left, as
/// <see cref="UltracodeEncoder.DrawStructure"/> draws them, give the turn. The tiles, five modules of cyan, magenta, yellow or green in a column, read in the
/// order of <see cref="UltracodeEncoder.NextTile"/>, and the Reed-Solomon code over the prime field of 283 corrects the message.
/// </summary>
internal sealed class UltracodeReader : MatrixReader
{
    /// <summary>
    /// The value of a module that the structure does not draw.
    /// </summary>
    private const BarcodeModuleColor Unset = (BarcodeModuleColor)0xFF;

    /// <summary>
    /// The prime field of the Reed-Solomon code, whose generator roots are the powers of 3.
    /// </summary>
    private static readonly PrimeField Field = new(UltracodeEncoder.Modulus, 3);

    /// <summary>
    /// The codeword of each tile, the five 2-bit colour codes from the top, or -1, the inverse of <see cref="UltracodeEncoder.Tiles"/>.
    /// </summary>
    private static readonly short[] TileValues = CreateTileValues();

    /// <inheritdoc/>
    public override BarcodeFamily Families => BarcodeFamily.Ultracode;

    /// <inheritdoc/>
    public override void Read(DecodeContext context)
    {
        // The tiles differ by hue, which an image of grey levels does not hold.
        if ((context.Source.PixelType.ColorType & PixelColorType.Luminance) != 0)
        {
            return;
        }

        // The black edges join the whole symbol, so the smallest gap finds it, inside its quiet zone of one module.
        BitImage bits = context.Bits;
        Rgb24[]? colors = null;
        foreach (Rectangle region in context.State<SymbolRegions>().Find(context, 2))
        {
            if (HasFrame(bits, region))
            {
                TryRead(context, bits, region, ref colors);
            }
        }
    }

    /// <summary>
    /// Returns whether the edges of a region are dark nearly all along, as the black edges of a symbol are.
    /// </summary>
    /// <param name="bits">The image.</param>
    /// <param name="region">The region.</param>
    /// <returns><see langword="true"/> when the edges are dark.</returns>
    private static bool HasFrame(BitImage bits, Rectangle region)
    {
        int light = 0;
        for (int x = region.Left; x < region.Right; x++)
        {
            light += (bits[x, region.Top] ? 0 : 1) + (bits[x, region.Bottom - 1] ? 0 : 1);
        }

        for (int y = region.Top; y < region.Bottom; y++)
        {
            light += (bits[region.Left, y] ? 0 : 1) + (bits[region.Right - 1, y] ? 0 : 1);
        }

        return light * 20 <= 2 * (region.Width + region.Height);
    }

    /// <summary>
    /// Tries every turn and height whose proportions fit a region.
    /// </summary>
    /// <param name="context">The decode.</param>
    /// <param name="bits">The image.</param>
    /// <param name="region">The region.</param>
    /// <param name="colors">The colours of the image, read on the first region whose structure matches.</param>
    private static void TryRead(DecodeContext context, BitImage bits, Rectangle region, ref Rgb24[]? colors)
    {
        ReadOnlySpan<PointF> box =
        [
            new(region.Left, region.Top),
            new(region.Right, region.Top),
            new(region.Right, region.Bottom),
            new(region.Left, region.Bottom),
        ];

        for (int turn = 0; turn < 4; turn++)
        {
            // A quarter turn puts the rows of the symbol across the image.
            int along = (turn & 1) == 0 ? region.Height : region.Width;
            int across = (turn & 1) == 0 ? region.Width : region.Height;
            for (int tileRows = 2; tileRows <= 5; tileRows++)
            {
                int rows = (tileRows * UltracodeEncoder.RowPitch) + 1;
                float module = along / (float)rows;
                float exact = across / module;
                int columns = (int)MathF.Round(exact);
                int tileColumns = columns - UltracodeEncoder.LeftColumns - 1;
                int fewest = UltracodeEncoder.FewestColumns(tileRows);
                if (module < 1.5F
                    || MathF.Abs(exact - columns) > 0.35F
                    || tileColumns < fewest
                    || tileColumns > Math.Min(fewest + 31, UltracodeEncoder.MaximumColumns))
                {
                    continue;
                }

                // Corner k of the symbol, clockwise from the upper left, lies at corner k + turn of the region.
                PointF topLeft = box[turn];
                PointF topRight = box[(turn + 1) % 4];
                PointF bottomRight = box[(turn + 2) % 4];
                PointF bottomLeft = box[(turn + 3) % 4];
                ReadOnlySpan<PointF> from = [new(0, 0), new(columns, 0), new(columns, rows), new(0, rows)];
                ReadOnlySpan<PointF> to = [topLeft, topRight, bottomRight, bottomLeft];
                PerspectiveTransform transform = PerspectiveTransform.QuadrilateralToQuadrilateral(from, to);
                if (!ModuleMatrix.TrySample(bits, transform, rows, columns, out ModuleMatrix matrix) || !HasStructure(matrix))
                {
                    continue;
                }

                colors ??= ReadColors(context.Source);
                if (TryDecode(colors, context.Source.Width, context.Source.Height, transform, columns, rows, out string text))
                {
                    context.Add(BarcodeFamily.Ultracode, text, "]X0", [topLeft, topRight, bottomRight, bottomLeft]);
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Returns whether nineteen in twenty of the modules of the structure read dark or light as the structure has them. The column of the data column count
    /// code does not count.
    /// </summary>
    /// <param name="matrix">The modules.</param>
    /// <returns><see langword="true"/> when the structure matches.</returns>
    private static bool HasStructure(ModuleMatrix matrix)
    {
        int columns = matrix.Columns;
        BarcodeModuleColor[] structure = new BarcodeModuleColor[columns * matrix.Rows];
        structure.AsSpan().Fill(Unset);
        UltracodeEncoder.DrawStructure(structure, columns, matrix.Rows);
        int count = 0;
        int wrong = 0;
        for (int i = 0; i < structure.Length; i++)
        {
            if (structure[i] == Unset || i % columns == UltracodeEncoder.CountColumn)
            {
                continue;
            }

            count++;
            wrong += (structure[i] == BarcodeModuleColor.Dark) != matrix.At(i) ? 1 : 0;
        }

        return wrong * 20 <= count;
    }

    /// <summary>
    /// Reads the colours of an image.
    /// </summary>
    /// <param name="image">The image.</param>
    /// <returns>The colours, row by row.</returns>
    private static Rgb24[] ReadColors(Image image)
    {
        Rgb24[] colors = new Rgb24[image.Width * image.Height];
        if (image is Image<Rgb24> rgb)
        {
            rgb.CopyPixelDataTo(colors);
        }
        else
        {
            using Image<Rgb24> converted = image.CloneAs<Rgb24>();
            converted.CopyPixelDataTo(colors);
        }

        return colors;
    }

    /// <summary>
    /// Reads the tiles of a symbol and decodes them.
    /// </summary>
    /// <param name="colors">The colours of the image, row by row.</param>
    /// <param name="width">The image width.</param>
    /// <param name="height">The image height.</param>
    /// <param name="transform">The transform from module coordinates to pixels.</param>
    /// <param name="columns">The number of columns.</param>
    /// <param name="rows">The number of rows.</param>
    /// <param name="text">The data.</param>
    /// <returns><see langword="true"/> when the symbol decodes.</returns>
    private static bool TryDecode(Rgb24[] colors, int width, int height, PerspectiveTransform transform, int columns, int rows, out string text)
    {
        // The four data colours, then black and white, which no tile module has.
        Span<Rgb24> references =
        [
            MatrixEmitter.ModuleBrushes.Cyan.Color.ToPixel<Rgb24>(),
            MatrixEmitter.ModuleBrushes.Magenta.Color.ToPixel<Rgb24>(),
            MatrixEmitter.ModuleBrushes.Yellow.Color.ToPixel<Rgb24>(),
            MatrixEmitter.ModuleBrushes.Green.Color.ToPixel<Rgb24>(),
            new(0, 0, 0),
            new(255, 255, 255),
        ];

        int tileRows = (rows - 1) / UltracodeEncoder.RowPitch;
        int[] tiles = new int[UltracodeEncoder.DataColumns(columns - UltracodeEncoder.LeftColumns - 1) * tileRows];
        int x = UltracodeEncoder.LeftColumns;
        int y = 1;
        for (int t = 0; t < tiles.Length; t++)
        {
            int tile = 0;
            for (int k = 0; k < UltracodeEncoder.TileHeight; k++)
            {
                int code = Classify(colors, width, height, transform.Transform(x + 0.5, y + k + 0.5), references);
                tile = code < 0 || tile < 0 ? -1 : (tile << UltracodeEncoder.ColorBits) | code;
            }

            tiles[t] = tile < 0 ? -1 : TileValues[tile];
            UltracodeEncoder.NextTile(ref x, ref y, rows);
        }

        return TryDecodeTiles(tiles, out text);
    }

    /// <summary>
    /// Returns the colour code of the pixel nearest a point: 0 cyan, 1 magenta, 2 yellow and 3 green, or -1 for black, white or a point outside the image.
    /// </summary>
    /// <param name="colors">The colours of the image, row by row.</param>
    /// <param name="width">The image width.</param>
    /// <param name="height">The image height.</param>
    /// <param name="point">The point.</param>
    /// <param name="references">The data colours, then black and white.</param>
    /// <returns>The colour code, or -1.</returns>
    private static int Classify(Rgb24[] colors, int width, int height, PointF point, ReadOnlySpan<Rgb24> references)
    {
        int px = (int)MathF.Floor(point.X);
        int py = (int)MathF.Floor(point.Y);
        if ((uint)px >= (uint)width || (uint)py >= (uint)height)
        {
            return -1;
        }

        Rgb24 color = colors[(py * width) + px];
        int nearest = -1;
        int best = int.MaxValue;
        for (int i = 0; i < references.Length; i++)
        {
            int dr = color.R - references[i].R;
            int dg = color.G - references[i].G;
            int db = color.B - references[i].B;
            int distance = (dr * dr) + (dg * dg) + (db * db);
            if (distance < best)
            {
                best = distance;
                nearest = i;
            }
        }

        return nearest < 4 ? nearest : -1;
    }

    /// <summary>
    /// Decodes the tiles in their order: the start codeword, the message codeword count, the check codewords, the tile count, the separator, the auxiliary
    /// codeword count, the data, the pads and the check codeword count. The message is the start codeword, the two counts and the data, and the check codewords
    /// follow it in the Reed-Solomon code word.
    /// </summary>
    /// <param name="tiles">The codewords of the tiles, -1 for a tile that does not read.</param>
    /// <param name="text">The data.</param>
    /// <returns><see langword="true"/> when the tiles decode.</returns>
    private static bool TryDecodeTiles(ReadOnlySpan<int> tiles, out string text)
    {
        text = string.Empty;

        // Only the separator has its value, so it gives the number of check codewords before it.
        int separator = tiles.IndexOf(UltracodeEncoder.Separator);
        int checkCount = separator - 3;
        if (checkCount < 5)
        {
            return false;
        }

        // The data runs to the first pad, or up to the last tile.
        int dataStart = separator + 2;
        int end = dataStart;
        while (end < tiles.Length - 1 && tiles[end] != UltracodeEncoder.Pad)
        {
            end++;
        }

        int messageCount = UltracodeEncoder.MessageHeader + end - dataStart;
        if (end <= dataStart || messageCount + checkCount > Field.Modulus - 1)
        {
            return false;
        }

        int[] codeword = new int[messageCount + checkCount];
        codeword[0] = tiles[0];
        codeword[1] = tiles[1];
        codeword[2] = tiles[separator + 1];
        tiles[dataStart..end].CopyTo(codeword.AsSpan(UltracodeEncoder.MessageHeader));
        tiles.Slice(2, checkCount).CopyTo(codeword.AsSpan(messageCount));
        for (int i = 0; i < codeword.Length; i++)
        {
            codeword[i] = (uint)codeword[i] < (uint)Field.Modulus ? codeword[i] : 0;
        }

        if (ReedSolomonDecoder.Correct(new PrimeReedSolomonField(Field), codeword, checkCount, 1) < 0
            || codeword[0] != UltracodeEncoder.StartLatin1
            || codeword[1] != messageCount
            || codeword[2] != checkCount - 3)
        {
            return false;
        }

        // Each data codeword is a character of ISO/IEC 8859-1.
        Span<int> data = codeword.AsSpan(UltracodeEncoder.MessageHeader, messageCount - UltracodeEncoder.MessageHeader);
        char[] characters = new char[data.Length];
        for (int i = 0; i < data.Length; i++)
        {
            if (data[i] > 255)
            {
                return false;
            }

            characters[i] = (char)data[i];
        }

        text = new string(characters);
        return true;
    }

    /// <summary>
    /// Inverts the tiles of the codewords.
    /// </summary>
    /// <returns>The codeword of each tile, or -1.</returns>
    private static short[] CreateTileValues()
    {
        short[] values = new short[1 << (UltracodeEncoder.TileHeight * UltracodeEncoder.ColorBits)];
        values.AsSpan().Fill(-1);
        ReadOnlySpan<ushort> tiles = UltracodeEncoder.Tiles;
        for (int codeword = 0; codeword < tiles.Length; codeword++)
        {
            values[tiles[codeword]] = (short)codeword;
        }

        return values;
    }
}
