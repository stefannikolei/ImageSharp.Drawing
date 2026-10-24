// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Reads GS1 DataBar Expanded and Expanded Stacked, ISO/IEC 24724. A row is a guard pattern, pairs of a symbol character, a finder pattern and a symbol
/// character reversed, and a guard pattern. The first character of a symbol is its check character. The reader reads every row it crosses into a segment of
/// pairs, joins segments whose finder patterns make one of the sequences of <see cref="DataBarExpandedEncoder.FinderSequences"/>, checks the modulo 211 check
/// value and decodes the bits of the data characters with <see cref="DataBarExpandedBits"/>.
/// </summary>
internal sealed class DataBarExpandedReader : LinearReader
{
    private const int CharacterElements = 8;
    private const int FinderElements = 5;
    private const int FinderModules = 15;

    /// <summary>
    /// The odd set module count of each group, widened for <see cref="DataBarCharacter.TryValues"/>.
    /// </summary>
    private static readonly int[] OddModules = Widen(DataBarExpandedEncoder.OddModules);

    /// <summary>
    /// The widest odd element of each group, widened for <see cref="DataBarCharacter.TryValues"/>.
    /// </summary>
    private static readonly int[] OddWidest = Widen(DataBarExpandedEncoder.OddWidest);

    /// <inheritdoc/>
    public override BarcodeFamily Families => BarcodeFamily.DataBarExpanded;

    /// <inheritdoc/>
    public override bool TryRead(ReadOnlySpan<int> runs, int start, DecodeContext context, out LinearMatch match)
    {
        Segments segments = context.State<Segments>();
        List<Pair> pairs = [];
        for (int g = start; g + 1 + CharacterElements + FinderElements < runs.Length; g += 2)
        {
            // The guard pattern is a narrow space and a narrow bar. The space can be part of the light area before the row, or the row can start with the
            // bar and the space follows it.
            foreach (int offset in (ReadOnlySpan<int>)[1, 2])
            {
                int first = g + offset;
                if (!TryReadSegment(runs, first, pairs, out int end))
                {
                    continue;
                }

                float module = Sum(runs, first, CharacterElements) / (float)DataBarExpandedEncoder.CharacterModules;
                if (!IsModule(runs[g], module) || (offset == 2 && !IsModule(runs[g + 1], module)) || !IsQuiet(runs, g - 1, module))
                {
                    continue;
                }

                Segment segment = new([.. pairs]);
                if (!segments.List.Exists(s => s.SameAs(segment)))
                {
                    segments.List.Add(segment);
                }

                if (TryAssemble(segments.List, out string? text))
                {
                    match = new LinearMatch(BarcodeFamily.DataBarExpanded, text, "]e0", g, end, true);
                    return true;
                }

                break;
            }
        }

        match = default;
        return false;
    }

    /// <summary>
    /// Reads the pairs of a row from its first character to the guard pattern at its end.
    /// </summary>
    /// <param name="runs">The runs.</param>
    /// <param name="position">The index of the first run of the first character.</param>
    /// <param name="pairs">The pairs read.</param>
    /// <param name="end">The index of the light run after the row.</param>
    /// <returns><see langword="true"/> when the row reads from guard to guard.</returns>
    private static bool TryReadSegment(ReadOnlySpan<int> runs, int position, List<Pair> pairs, out int end)
    {
        pairs.Clear();
        end = 0;
        Span<int> leftWidths = stackalloc int[CharacterElements];
        Span<int> rightWidths = stackalloc int[CharacterElements];
        float module = 0;
        while (position + CharacterElements + FinderElements < runs.Length)
        {
            int left = ReadCharacter(runs.Slice(position, CharacterElements), false, leftWidths);
            int finder = left < 0 ? -1 : ReadFinder(runs.Slice(position + CharacterElements, FinderElements));
            if (finder < 0)
            {
                // A row ends with a whole pair, so the guard pattern stands where the next left character would.
                return pairs.Count > 0 && IsEnd(runs, position, module, out end);
            }

            module = Sum(runs, position, CharacterElements) / (float)DataBarExpandedEncoder.CharacterModules;

            int after = position + CharacterElements + FinderElements;
            int right = after + CharacterElements <= runs.Length ? ReadCharacter(runs.Slice(after, CharacterElements), true, rightWidths) : -1;
            if (right < 0)
            {
                // A symbol of an odd number of characters ends with a pair that has no right character.
                pairs.Add(new Pair(left, leftWidths, finder, -1, default));
                return IsEnd(runs, after, module, out end);
            }

            pairs.Add(new Pair(left, leftWidths, finder, right, rightWidths));
            position = after + CharacterElements;
        }

        // The row ends too close to the end of the runs for another pair.
        return pairs.Count > 0 && IsEnd(runs, position, module, out end);
    }

    /// <summary>
    /// Returns a value indicating whether the guard pattern ends the row at a run: a narrow space and a narrow bar, or a narrow bar whose space joins the
    /// light area after the row.
    /// </summary>
    /// <param name="runs">The runs.</param>
    /// <param name="position">The index of the first run of the guard pattern.</param>
    /// <param name="module">The module width.</param>
    /// <param name="end">The index of the light run after the row.</param>
    /// <returns><see langword="true"/> when the guard pattern is there.</returns>
    private static bool IsEnd(ReadOnlySpan<int> runs, int position, float module, out int end)
    {
        end = 0;
        if (position + 1 >= runs.Length)
        {
            return false;
        }

        if ((position & 1) == 1)
        {
            end = position + 1;
            return IsModule(runs[position], module) && runs[position + 1] >= module;
        }

        end = position + 2;
        return position + 2 < runs.Length && IsModule(runs[position], module) && IsModule(runs[position + 1], module) && runs[position + 2] >= module;
    }

    /// <summary>
    /// Joins segments into a symbol whose finder patterns make the sequence of its length, and decodes it when its check character holds.
    /// </summary>
    /// <param name="segments">The segments read so far.</param>
    /// <param name="text">The element strings.</param>
    /// <returns><see langword="true"/> when the segments make a symbol.</returns>
    private static bool TryAssemble(List<Segment> segments, out string text)
    {
        text = string.Empty;
        List<Pair> pairs = [];
        Span<bool> used = stackalloc bool[segments.Count];
        for (int s = 0; s < segments.Count; s++)
        {
            // The check character is the left character of the first pair, whose finder pattern is A1 in every sequence.
            if (segments[s].Pairs[0].Finder != 0)
            {
                continue;
            }

            pairs.Clear();
            used.Clear();
            if (Extend(segments, s, pairs, used, out text))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Adds a segment to the pairs of a symbol and tries every unused segment that can follow it. A segment joins only while the finder patterns start one of
    /// the sequences, so the search stays small.
    /// </summary>
    /// <param name="segments">The segments read so far.</param>
    /// <param name="index">The index of the segment to add.</param>
    /// <param name="pairs">The pairs of the symbol so far.</param>
    /// <param name="used">The segments in the pairs so far.</param>
    /// <param name="text">The element strings.</param>
    /// <returns><see langword="true"/> when the pairs make a symbol.</returns>
    private static bool Extend(List<Segment> segments, int index, List<Pair> pairs, Span<bool> used, out string text)
    {
        text = string.Empty;
        int count = pairs.Count;
        pairs.AddRange(segments[index].Pairs);
        used[index] = true;
        if (StartsSequence(pairs, out bool whole))
        {
            if (whole && TryDecode(pairs, out text))
            {
                return true;
            }

            // A segment that ends with a pair without a right character ends the symbol.
            if (pairs[^1].Right >= 0)
            {
                for (int next = 0; next < segments.Count; next++)
                {
                    if (!used[next] && segments[next].Pairs[0].Finder != 0 && Extend(segments, next, pairs, used, out text))
                    {
                        return true;
                    }
                }
            }
        }

        used[index] = false;
        pairs.RemoveRange(count, pairs.Count - count);
        return false;
    }

    /// <summary>
    /// Returns a value indicating whether the finder patterns of the pairs start one of the sequences.
    /// </summary>
    /// <param name="pairs">The pairs.</param>
    /// <param name="whole">Whether the pairs make the whole sequence of their symbol length.</param>
    /// <returns><see langword="true"/> when the finders start a sequence.</returns>
    private static bool StartsSequence(List<Pair> pairs, out bool whole)
    {
        whole = false;
        bool starts = false;

        // Two symbol lengths share each sequence, so every second length gives every sequence once.
        const int first = DataBarExpandedEncoder.MinimumSymbolCharacters;
        for (int symbolCharacters = first; symbolCharacters <= DataBarExpandedEncoder.MaximumSymbolCharacters; symbolCharacters += 2)
        {
            ReadOnlySpan<byte> sequence = DataBarExpandedEncoder.Finders(symbolCharacters);
            if (sequence.Length < pairs.Count)
            {
                continue;
            }

            bool prefix = true;
            for (int i = 0; i < pairs.Count && prefix; i++)
            {
                prefix = sequence[i] == pairs[i].Finder;
            }

            if (prefix)
            {
                starts = true;
                whole |= sequence.Length == pairs.Count;
            }
        }

        return starts;
    }

    /// <summary>
    /// Checks the check character of a symbol and decodes its data characters.
    /// </summary>
    /// <param name="pairs">The pairs of the symbol.</param>
    /// <param name="text">The element strings.</param>
    /// <returns><see langword="true"/> when the check character holds and the bits decode.</returns>
    private static bool TryDecode(List<Pair> pairs, out string text)
    {
        text = string.Empty;
        int symbolCharacters = (pairs.Count * 2) - (pairs[^1].Right < 0 ? 1 : 0);
        Span<int> values = stackalloc int[symbolCharacters - 1];

        // Each data character weighs its widths, in the order the symbol carries them, with the weights of its side of its finder pattern.
        ReadOnlySpan<byte> weights = DataBarExpandedEncoder.CheckWeights;
        int check = 0;
        for (int c = 1; c < symbolCharacters; c++)
        {
            Pair pair = pairs[c / 2];
            bool right = (c & 1) == 1;
            values[c - 1] = right ? pair.Right : pair.Left;
            ReadOnlySpan<int> widths = right ? pair.RightWidths : pair.LeftWidths;
            ReadOnlySpan<byte> sideWeights = weights.Slice((pair.Finder * 2 * CharacterElements) + (right ? CharacterElements : 0), CharacterElements);
            for (int k = 0; k < CharacterElements; k++)
            {
                check += widths[k] * sideWeights[k];
            }
        }

        int expected = (DataBarExpandedEncoder.CheckModulus * (symbolCharacters - DataBarExpandedEncoder.MinimumSymbolCharacters))
            + (check % DataBarExpandedEncoder.CheckModulus);
        return pairs[0].Left == expected && DataBarExpandedBits.TryDecode(values, out text);
    }

    /// <summary>
    /// Reads a symbol character of 8 elements over 17 modules.
    /// </summary>
    /// <param name="runs">The eight runs.</param>
    /// <param name="reversed">Whether the character stands to the right of its finder pattern, in reverse order.</param>
    /// <param name="carried">The module widths in the order the symbol carries them, which the check weights take.</param>
    /// <returns>The character value, or -1.</returns>
    private static int ReadCharacter(ReadOnlySpan<int> runs, bool reversed, Span<int> carried)
    {
        if (!DataBarCharacter.TryModules(runs, DataBarExpandedEncoder.CharacterModules, carried))
        {
            return -1;
        }

        Span<int> widths = stackalloc int[CharacterElements];
        carried.CopyTo(widths);
        if (reversed)
        {
            widths.Reverse();
        }

        // Only the odd set excludes the combinations without a one module element.
        ReadOnlySpan<int> groupSum = DataBarExpandedEncoder.GroupSum;
        if (!DataBarCharacter.TryValues(widths, OddModules, OddWidest, 0, groupSum.Length - 1, true, out int group, out int odd, out int even))
        {
            return -1;
        }

        int value = groupSum[group] + (odd * DataBarExpandedEncoder.EvenCombinations[group]) + even;
        int next = group + 1 < groupSum.Length ? groupSum[group + 1] : 4096;
        return value < next ? value : -1;
    }

    /// <summary>
    /// Reads a finder pattern: five elements over 15 modules in one of the widths of <see cref="DataBarExpandedEncoder.FinderWidths"/>.
    /// </summary>
    /// <param name="runs">The five runs.</param>
    /// <returns>The finder value, or -1.</returns>
    private static int ReadFinder(ReadOnlySpan<int> runs)
    {
        Span<int> widths = stackalloc int[FinderElements];
        if (!DataBarCharacter.TryModules(runs, FinderModules, widths))
        {
            return -1;
        }

        ReadOnlySpan<byte> patterns = DataBarExpandedEncoder.FinderWidths;
        for (int v = 0; v < patterns.Length / FinderElements; v++)
        {
            if (DataBarReader.SameWidths(patterns.Slice(v * FinderElements, FinderElements), widths))
            {
                return v;
            }
        }

        return -1;
    }

    /// <summary>
    /// Returns a value indicating whether a run is about one module.
    /// </summary>
    /// <param name="run">The run.</param>
    /// <param name="module">The module width.</param>
    /// <returns><see langword="true"/> for a run of one module.</returns>
    private static bool IsModule(int run, float module) => run >= module * 0.5F && run <= module * 1.6F;

    /// <summary>
    /// Widens a byte table.
    /// </summary>
    /// <param name="values">The table.</param>
    /// <returns>The table as integers.</returns>
    private static int[] Widen(ReadOnlySpan<byte> values)
    {
        int[] widened = new int[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            widened[i] = values[i];
        }

        return widened;
    }

    /// <summary>
    /// A pair: a left character, a finder pattern and a right character, which the last pair of a symbol can lack.
    /// </summary>
    private readonly struct Pair
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Pair"/> struct.
        /// </summary>
        /// <param name="left">The value of the left character.</param>
        /// <param name="leftWidths">The widths of the left character as the symbol carries them.</param>
        /// <param name="finder">The finder value.</param>
        /// <param name="right">The value of the right character, or -1.</param>
        /// <param name="rightWidths">The widths of the right character as the symbol carries them.</param>
        public Pair(int left, ReadOnlySpan<int> leftWidths, int finder, int right, ReadOnlySpan<int> rightWidths)
        {
            this.Left = left;
            this.LeftWidths = leftWidths.ToArray();
            this.Finder = finder;
            this.Right = right;
            this.RightWidths = right < 0 ? [] : rightWidths.ToArray();
        }

        /// <summary>
        /// Gets the value of the left character.
        /// </summary>
        public int Left { get; }

        /// <summary>
        /// Gets the widths of the left character.
        /// </summary>
        public int[] LeftWidths { get; }

        /// <summary>
        /// Gets the finder value.
        /// </summary>
        public int Finder { get; }

        /// <summary>
        /// Gets the value of the right character, or -1.
        /// </summary>
        public int Right { get; }

        /// <summary>
        /// Gets the widths of the right character.
        /// </summary>
        public int[] RightWidths { get; }

        /// <summary>
        /// Returns a value indicating whether another pair has the same characters and finder.
        /// </summary>
        /// <param name="other">The other pair.</param>
        /// <returns><see langword="true"/> when the pairs are the same.</returns>
        public bool SameAs(Pair other) => this.Left == other.Left && this.Finder == other.Finder && this.Right == other.Right;
    }

    /// <summary>
    /// The segments that the scan lines of one decode have read.
    /// </summary>
    private sealed class Segments
    {
        /// <summary>
        /// Gets the segments.
        /// </summary>
        public List<Segment> List { get; } = [];
    }

    /// <summary>
    /// The pairs of one row.
    /// </summary>
    private sealed class Segment
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Segment"/> class.
        /// </summary>
        /// <param name="pairs">The pairs.</param>
        public Segment(Pair[] pairs) => this.Pairs = pairs;

        /// <summary>
        /// Gets the pairs.
        /// </summary>
        public Pair[] Pairs { get; }

        /// <summary>
        /// Returns a value indicating whether another segment has the same pairs.
        /// </summary>
        /// <param name="other">The other segment.</param>
        /// <returns><see langword="true"/> when the pairs are the same.</returns>
        public bool SameAs(Segment other)
        {
            if (other.Pairs.Length != this.Pairs.Length)
            {
                return false;
            }

            for (int i = 0; i < this.Pairs.Length; i++)
            {
                if (!this.Pairs[i].SameAs(other.Pairs[i]))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
