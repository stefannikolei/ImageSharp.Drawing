// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Reads GS1 DataBar Limited, ISO/IEC 24724: a narrow space and bar, a left character of 14 elements over 26 modules, a check character, a right character
/// and a narrow space, a narrow bar and a space of five modules. The check character encodes the modulo 89 check value of the two characters.
/// </summary>
internal sealed class DataBarLimitedReader : LinearReader
{
    private const int CharacterElements = 14;
    private const int CheckSetElements = 6;

    /// <summary>
    /// The odd set module count of each group, widened for <see cref="DataBarCharacter.TryValues"/>.
    /// </summary>
    private static readonly int[] OddModules = Widen(DataBarLimitedEncoder.OddModules);

    /// <summary>
    /// The widest odd element of each group, widened for <see cref="DataBarCharacter.TryValues"/>.
    /// </summary>
    private static readonly int[] OddWidest = Widen(DataBarLimitedEncoder.OddWidest);

    /// <inheritdoc/>
    public override BarcodeFamily Families => BarcodeFamily.DataBarLimited;

    /// <inheritdoc/>
    public override bool TryRead(ReadOnlySpan<int> runs, int start, DecodeContext context, out LinearMatch match)
    {
        Span<int> left = stackalloc int[CharacterElements];
        Span<int> check = stackalloc int[CharacterElements];
        Span<int> right = stackalloc int[CharacterElements];
        Span<int> first = stackalloc int[CheckSetElements];
        Span<int> second = stackalloc int[CheckSetElements];
        for (int b = start; b + 45 < runs.Length; b += 2)
        {
            // The runs from the guard bar to the last narrow bar are 73 modules.
            float module = Sum(runs, b, 45) / 73F;
            if (!IsModule(runs[b], module) || !IsModule(runs[b + 43], module) || !IsModule(runs[b + 44], module) || !IsQuiet(runs, b + 45, module * 4))
            {
                continue;
            }

            if (!DataBarCharacter.TryModules(runs.Slice(b + 1, CharacterElements), DataBarLimitedEncoder.CharacterModules, left)
                || !DataBarCharacter.TryModules(runs.Slice(b + 15, CharacterElements - 2), DataBarLimitedEncoder.CheckSetModules * 2, check)
                || !DataBarCharacter.TryModules(runs.Slice(b + 29, CharacterElements), DataBarLimitedEncoder.CharacterModules, right))
            {
                continue;
            }

            int leftValue = CharacterValue(left);
            int rightValue = CharacterValue(right);
            if (leftValue < 0 || rightValue < 0)
            {
                continue;
            }

            // The check value is the weighted sum of the element widths of both characters, and the check character carries its place in CheckSequence.
            ReadOnlySpan<byte> weights = DataBarLimitedEncoder.CheckWeights;
            int sum = 0;
            for (int k = 0; k < CharacterElements; k++)
            {
                sum += (weights[k] * left[k]) + (weights[CharacterElements + k] * right[k]);
            }

            for (int k = 0; k < CheckSetElements; k++)
            {
                first[k] = check[k * 2];
                second[k] = check[(k * 2) + 1];
            }

            if (!IsCheckSet(first) || !IsCheckSet(second))
            {
                continue;
            }

            int sequence = (DataBarWidths.Value(first, DataBarLimitedEncoder.CheckSetWidest, false) * DataBarLimitedEncoder.CheckSetPatterns)
                + DataBarWidths.Value(second, DataBarLimitedEncoder.CheckSetWidest, false);
            if (DataBarLimitedEncoder.CheckSequence[sum % DataBarLimitedEncoder.CheckModulus] != sequence)
            {
                continue;
            }

            // A linked symbol adds LinkedOffset to the 13 digits before the check digit, whose indicator digit is 0 or 1.
            long value = ((long)leftValue * DataBarLimitedEncoder.CharacterModulus) + rightValue;
            if (value >= DataBarLimitedEncoder.LinkedOffset)
            {
                value -= DataBarLimitedEncoder.LinkedOffset;
            }

            if (value >= 2_000_000_000_000)
            {
                continue;
            }

            match = new LinearMatch(BarcodeFamily.DataBarLimited, DataBarReader.Gtin(value), "]e0", b, b + 45, true);
            return true;
        }

        match = default;
        return false;
    }

    /// <summary>
    /// Returns the value of a character of 14 elements.
    /// </summary>
    /// <param name="widths">The module widths, odd set elements at the even indices.</param>
    /// <returns>The value, or -1.</returns>
    private static int CharacterValue(ReadOnlySpan<int> widths)
    {
        ReadOnlySpan<int> groupSum = DataBarLimitedEncoder.GroupSum;

        // Only the even set excludes the combinations without a one module element.
        if (!DataBarCharacter.TryValues(widths, OddModules, OddWidest, 0, groupSum.Length - 1, false, out int group, out int odd, out int even))
        {
            return -1;
        }

        int value = groupSum[group] + (odd * DataBarLimitedEncoder.EvenCombinations[group]) + even;
        int next = int.MaxValue;
        foreach (int sum in groupSum)
        {
            if (sum > groupSum[group])
            {
                next = Math.Min(next, sum);
            }
        }

        return value < Math.Min(next, DataBarLimitedEncoder.CharacterModulus) ? value : -1;
    }

    /// <summary>
    /// Returns a value indicating whether a set of the check character is six elements over eight modules with none wider than three.
    /// </summary>
    /// <param name="widths">The widths of the set.</param>
    /// <returns><see langword="true"/> for a valid set.</returns>
    private static bool IsCheckSet(ReadOnlySpan<int> widths)
    {
        int sum = 0;
        foreach (int width in widths)
        {
            if (width > DataBarLimitedEncoder.CheckSetWidest)
            {
                return false;
            }

            sum += width;
        }

        return sum == DataBarLimitedEncoder.CheckSetModules;
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
}
