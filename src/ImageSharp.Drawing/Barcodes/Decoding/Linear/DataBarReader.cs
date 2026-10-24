// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Reads GS1 DataBar Omnidirectional, Truncated, Stacked and Stacked Omnidirectional, ISO/IEC 24724. A symbol is a left pair, an outside character, a finder
/// pattern and an inside character reversed, and a right pair, an inside character, a finder pattern reversed and an outside character reversed, between the
/// guard patterns. A single row symbol carries both pairs in one row, and a stacked symbol one pair in each row. The reader collects the pairs of every scan
/// line and joins a left and a right pair when the modulo 79 check value of their characters selects their finder patterns.
/// </summary>
internal sealed class DataBarReader : LinearReader
{
    private const int FinderElements = 5;
    private const int CharacterElements = 8;

    /// <inheritdoc/>
    public override BarcodeFamily Families => BarcodeFamily.DataBar;

    /// <inheritdoc/>
    public override bool TryRead(ReadOnlySpan<int> runs, int start, DecodeContext context, out LinearMatch match)
    {
        Pairs pairs = context.State<Pairs>();
        for (int p = start - 1; p + FinderElements + CharacterElements <= runs.Length; p++)
        {
            // A left finder starts with a space, at an even run index, and a right finder, reversed, with a bar. A left pair needs the guard bar before it,
            // and a right pair the guard space and bar after it.
            bool right = (p & 1) == 1;
            if (p < CharacterElements + (right ? 0 : 1) || (right && p + FinderElements + CharacterElements + 2 > runs.Length))
            {
                continue;
            }

            if (!TryReadPair(runs, p, right, out Pair pair))
            {
                continue;
            }

            List<Pair> own = right ? pairs.Right : pairs.Left;
            if (!own.Contains(pair))
            {
                own.Add(pair);
            }

            foreach (Pair other in right ? pairs.Left : pairs.Right)
            {
                Pair left = right ? other : pair;
                Pair rightPair = right ? pair : other;
                if (TryJoin(left, rightPair, out string? text))
                {
                    int first = right ? p - CharacterElements : p - CharacterElements - 1;
                    int end = right ? p + FinderElements + CharacterElements + 2 : p + FinderElements + CharacterElements - 1;
                    match = new LinearMatch(BarcodeFamily.DataBar, text, "]e0", first, end, true);
                    return true;
                }
            }
        }

        match = default;
        return false;
    }

    /// <summary>
    /// Reads the value of an outside or inside character from its eight element widths in the order of the value.
    /// </summary>
    /// <param name="runs">The eight runs.</param>
    /// <param name="reversed">Whether the runs stand in reverse order.</param>
    /// <param name="outside">Whether the character is an outside character, of 16 modules, or an inside one, of 15.</param>
    /// <param name="widths">The eight module widths in the order of the value.</param>
    /// <returns>The character value, or -1.</returns>
    private static int ReadCharacter(ReadOnlySpan<int> runs, bool reversed, bool outside, Span<int> widths)
    {
        Span<int> ordered = stackalloc int[CharacterElements];
        runs.CopyTo(ordered);
        if (reversed)
        {
            ordered.Reverse();
        }

        if (!DataBarCharacter.TryModules(ordered, outside ? DataBarEncoder.OutsideModules : DataBarEncoder.InsideModules, widths))
        {
            return -1;
        }

        int first = outside ? 0 : DataBarEncoder.FirstInsideGroup;
        int last = outside ? DataBarEncoder.FirstInsideGroup - 1 : DataBarEncoder.GroupSum.Length - 1;
        ReadOnlySpan<int> oddModules = DataBarEncoder.OddModules;
        if (!DataBarCharacter.TryValues(widths, oddModules, DataBarEncoder.OddWidest, first, last, !outside, out int group, out int odd, out int even))
        {
            return -1;
        }

        // An outside value is the odd value times the even combinations plus the even value, and an inside value the other way round.
        int combinations = DataBarEncoder.Combinations[group];
        int value = DataBarEncoder.GroupSum[group] + (outside ? (odd * combinations) + even : (even * combinations) + odd);
        int next = group < DataBarEncoder.GroupSum.Length - 1 && DataBarEncoder.GroupSum[group + 1] > DataBarEncoder.GroupSum[group]
            ? DataBarEncoder.GroupSum[group + 1]
            : int.MaxValue;
        return value < next ? value : -1;
    }

    /// <summary>
    /// Reads a pair around a finder pattern.
    /// </summary>
    /// <param name="runs">The runs.</param>
    /// <param name="finder">The index of the first run of the finder pattern.</param>
    /// <param name="right">Whether the pair is a right pair, whose finder pattern stands reversed.</param>
    /// <param name="pair">The pair.</param>
    /// <returns><see langword="true"/> when a pair reads.</returns>
    private static bool TryReadPair(ReadOnlySpan<int> runs, int finder, bool right, out Pair pair)
    {
        pair = default;

        // The finder pattern: 15 modules whose last two elements, as the left finder reads them, are one module each.
        Span<int> finderRuns = stackalloc int[FinderElements];
        runs.Slice(finder, FinderElements).CopyTo(finderRuns);
        if (right)
        {
            finderRuns.Reverse();
        }

        Span<int> widths = stackalloc int[FinderElements];
        if (!DataBarCharacter.TryModules(finderRuns, 15, widths))
        {
            return false;
        }

        int finderValue = -1;
        ReadOnlySpan<byte> patterns = DataBarEncoder.FinderPatterns;
        for (int v = 0; v < DataBarEncoder.FinderValues; v++)
        {
            if (SameWidths(patterns.Slice(v * FinderElements, FinderElements), widths))
            {
                finderValue = v;
                break;
            }
        }

        if (finderValue < 0)
        {
            return false;
        }

        float module = Sum(runs, finder, FinderElements) / 15F;

        // The guard pattern: a one module bar before the outside character of a left pair, a one module space and bar after the outside character of a right
        // pair.
        int before = finder - CharacterElements;
        int after = finder + FinderElements;
        if (right
            ? !IsModule(runs[after + CharacterElements], module) || !IsModule(runs[after + CharacterElements + 1], module)
            : !IsModule(runs[before - 1], module))
        {
            return false;
        }

        // A left pair is the outside character, then the inside character reversed. A right pair is the inside character, then the outside character
        // reversed.
        Span<int> outsideWidths = stackalloc int[CharacterElements];
        Span<int> insideWidths = stackalloc int[CharacterElements];
        int outsideValue = right
            ? ReadCharacter(runs.Slice(after, CharacterElements), true, true, outsideWidths)
            : ReadCharacter(runs.Slice(before, CharacterElements), false, true, outsideWidths);
        int insideValue = right
            ? ReadCharacter(runs.Slice(before, CharacterElements), false, false, insideWidths)
            : ReadCharacter(runs.Slice(after, CharacterElements), true, false, insideWidths);
        if (outsideValue < 0 || insideValue < 0)
        {
            return false;
        }

        // The check weights run over the first, second, third and fourth character: the outside and inside character of the left pair, then the outside and
        // inside character of the right pair.
        ReadOnlySpan<byte> weights = DataBarEncoder.CheckWeights;
        int outsideIndex = right ? 2 : 0;
        int check = 0;
        for (int k = 0; k < CharacterElements; k++)
        {
            check += weights[(outsideIndex * CharacterElements) + k] * outsideWidths[k];
            check += weights[((outsideIndex + 1) * CharacterElements) + k] * insideWidths[k];
        }

        pair = new Pair((outsideValue * DataBarEncoder.CharacterModulus) + insideValue, finderValue, check);
        return true;
    }

    /// <summary>
    /// Joins a left and a right pair when their check value selects their finder patterns, and writes the element string of Application Identifier (01).
    /// </summary>
    /// <param name="left">The left pair.</param>
    /// <param name="right">The right pair.</param>
    /// <param name="text">The transmitted data.</param>
    /// <returns><see langword="true"/> when the pairs belong together.</returns>
    private static bool TryJoin(Pair left, Pair right, out string text)
    {
        text = string.Empty;

        // The check value skips the finder pairs (0, 8) and (8, 0).
        int check = (left.Check + right.Check) % DataBarEncoder.CheckModulus;
        if (check >= DataBarEncoder.FinderValues - 1)
        {
            check++;
        }

        if (check >= (DataBarEncoder.FinderValues - 1) * DataBarEncoder.FinderValues)
        {
            check++;
        }

        if (check / DataBarEncoder.FinderValues != left.Finder || check % DataBarEncoder.FinderValues != right.Finder)
        {
            return false;
        }

        // The data value is the 13 digits before the check digit, with the linkage flag of a composite symbol as a fourteenth digit in front.
        long value = ((long)left.Value * DataBarEncoder.PairModulus) + right.Value;
        if (value >= 20_000_000_000_000)
        {
            return false;
        }

        value %= 10_000_000_000_000;
        text = Gtin(value);
        return true;
    }

    /// <summary>
    /// Writes the element string of a GTIN: Application Identifier (01), the 13 digits and the check digit of section 7.9 of the GS1 General Specifications.
    /// </summary>
    /// <param name="value">The 13 digits as a number.</param>
    /// <returns>The element string.</returns>
    public static string Gtin(long value)
    {
        Span<char> digits = stackalloc char[16];
        digits[0] = '0';
        digits[1] = '1';
        for (int i = 14; i >= 2; i--)
        {
            digits[i] = (char)('0' + (value % 10));
            value /= 10;
        }

        digits[15] = (char)('0' + Gs1Data.ComputeCheckDigit(digits[2..15]));
        return digits.ToString();
    }

    /// <summary>
    /// Returns a value indicating whether a run is about one module.
    /// </summary>
    /// <param name="run">The run.</param>
    /// <param name="module">The module width.</param>
    /// <returns><see langword="true"/> for a run of one module.</returns>
    private static bool IsModule(int run, float module) => run >= module * 0.5F && run <= module * 1.6F;

    /// <summary>
    /// Returns a value indicating whether module widths equal a pattern.
    /// </summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="widths">The widths.</param>
    /// <returns><see langword="true"/> when every width equals its pattern element.</returns>
    public static bool SameWidths(ReadOnlySpan<byte> pattern, ReadOnlySpan<int> widths)
    {
        for (int i = 0; i < pattern.Length; i++)
        {
            if (pattern[i] != widths[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// One pair: the value of its two characters, its finder pattern and its part of the check value.
    /// </summary>
    private readonly struct Pair : IEquatable<Pair>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Pair"/> struct.
        /// </summary>
        /// <param name="value">The pair value, the outside character times 1597 plus the inside character.</param>
        /// <param name="finder">The finder pattern value.</param>
        /// <param name="check">The weighted sum of the element widths of the two characters.</param>
        public Pair(int value, int finder, int check)
        {
            this.Value = value;
            this.Finder = finder;
            this.Check = check;
        }

        /// <summary>
        /// Gets the pair value.
        /// </summary>
        public int Value { get; }

        /// <summary>
        /// Gets the finder pattern value.
        /// </summary>
        public int Finder { get; }

        /// <summary>
        /// Gets the weighted sum of the element widths.
        /// </summary>
        public int Check { get; }

        /// <inheritdoc/>
        public bool Equals(Pair other) => this.Value == other.Value && this.Finder == other.Finder && this.Check == other.Check;

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is Pair other && this.Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(this.Value, this.Finder, this.Check);
    }

    /// <summary>
    /// The pairs that the scan lines of one decode have read.
    /// </summary>
    private sealed class Pairs
    {
        /// <summary>
        /// Gets the left pairs.
        /// </summary>
        public List<Pair> Left { get; } = [];

        /// <summary>
        /// Gets the right pairs.
        /// </summary>
        public List<Pair> Right { get; } = [];
    }
}
