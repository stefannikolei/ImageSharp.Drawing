// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Reads the symbol characters of PDF417 and MicroPDF417, Annex A of ISO/IEC 15438, and the row address patterns of MicroPDF417. The runs of a character are
/// rounded to whole modules, and their widths look up the cluster and the codeword in a table made from <see cref="Pdf417Patterns.Modules"/>.
/// </summary>
internal static class Pdf417Characters
{
    /// <summary>
    /// The number of modules of a symbol character.
    /// </summary>
    public const int CharacterModules = 17;

    /// <summary>
    /// The number of modules of a row address pattern.
    /// </summary>
    public const int AddressModules = 10;

    /// <summary>
    /// The number of elements of a row address pattern.
    /// </summary>
    public const int AddressElements = 6;

    /// <summary>
    /// The cluster times 1024 plus the codeword of every set of element widths, keyed by the widths packed three bits each.
    /// </summary>
    private static readonly Dictionary<int, int> Codewords = CreateCodewords();

    /// <summary>
    /// Reads a symbol character.
    /// </summary>
    /// <param name="runs">The eight runs, bar first.</param>
    /// <param name="cluster">The cluster index, 0, 1 or 2, for the clusters 0, 3 and 6.</param>
    /// <returns>The codeword, or -1.</returns>
    public static int Read(ReadOnlySpan<int> runs, out int cluster)
    {
        cluster = -1;
        Span<int> widths = stackalloc int[Pdf417Patterns.ElementCount];
        if (!DataBarCharacter.TryModules(runs, CharacterModules, widths))
        {
            return -1;
        }

        int key = Pack(widths);
        if (key < 0 || !Codewords.TryGetValue(key, out int entry))
        {
            return -1;
        }

        cluster = entry >> 10;
        return entry & 1023;
    }

    /// <summary>
    /// Reads a row address pattern of MicroPDF417: three bars and three spaces over ten modules.
    /// </summary>
    /// <param name="runs">The six runs, bar first.</param>
    /// <param name="patterns">The table of the patterns, the first module of each in bit 9.</param>
    /// <returns>The index of the pattern in the table, or -1.</returns>
    public static int ReadAddress(ReadOnlySpan<int> runs, ReadOnlySpan<short> patterns)
    {
        Span<int> widths = stackalloc int[AddressElements];
        if (!DataBarCharacter.TryModules(runs, AddressModules, widths))
        {
            return -1;
        }

        int modules = 0;
        for (int e = 0; e < AddressElements; e++)
        {
            for (int m = 0; m < widths[e]; m++)
            {
                modules = (modules << 1) | ((e & 1) == 0 ? 1 : 0);
            }
        }

        return patterns.IndexOf((short)modules);
    }

    /// <summary>
    /// Packs element widths of one to six modules into three bits each.
    /// </summary>
    /// <param name="widths">The widths.</param>
    /// <returns>The key, or -1 when a width is wider than six.</returns>
    private static int Pack(ReadOnlySpan<int> widths)
    {
        int key = 0;
        foreach (int width in widths)
        {
            if (width is < 1 or > 6)
            {
                return -1;
            }

            key = (key << 3) | width;
        }

        return key;
    }

    /// <summary>
    /// Turns the module patterns of every cluster into element widths.
    /// </summary>
    /// <returns>The table.</returns>
    private static Dictionary<int, int> CreateCodewords()
    {
        ReadOnlySpan<int> modules = Pdf417Patterns.Modules;
        Dictionary<int, int> codewords = new(modules.Length);
        Span<int> widths = stackalloc int[Pdf417Patterns.ElementCount];
        for (int cluster = 0; cluster < 3; cluster++)
        {
            for (int codeword = 0; codeword < Pdf417Patterns.CodewordCount; codeword++)
            {
                int index = 0;
                Pdf417Patterns.AppendCharacter(widths, ref index, modules[(cluster * Pdf417Patterns.CodewordCount) + codeword]);
                codewords[Pack(widths)] = (cluster << 10) | codeword;
            }
        }

        return codewords;
    }
}
