// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The element width measurement the GS1 DataBar readers share: the runs of a symbol character or a finder pattern become whole module widths that add to
/// the module count of the character.
/// </summary>
internal static class DataBarCharacter
{
    /// <summary>
    /// Rounds runs to module widths that add to a module count. Each run takes its nearest whole number of modules, at least one. When the rounded widths do
    /// not add to the count, the widths whose rounding error points the right way change by one module, the largest error first.
    /// </summary>
    /// <param name="runs">The runs in pixels.</param>
    /// <param name="modules">The module count of the runs.</param>
    /// <param name="widths">The widths to write, as many as the runs.</param>
    /// <returns><see langword="true"/> when the runs fit the module count.</returns>
    public static bool TryModules(ReadOnlySpan<int> runs, int modules, Span<int> widths)
    {
        int total = 0;
        foreach (int run in runs)
        {
            total += run;
        }

        if (total < modules)
        {
            return false;
        }

        float module = (float)total / modules;
        int sum = 0;
        for (int i = 0; i < runs.Length; i++)
        {
            widths[i] = Math.Max(1, (int)MathF.Round(runs[i] / module));
            sum += widths[i];
        }

        // A correction of more than one module for every two elements means the runs are not this character.
        int difference = modules - sum;
        if (Math.Abs(difference) * 2 > runs.Length)
        {
            return false;
        }

        while (difference != 0)
        {
            int step = Math.Sign(difference);
            int best = -1;
            float bestError = float.MinValue;
            for (int i = 0; i < runs.Length; i++)
            {
                // The error is how far the run lies beyond its width in the direction of the step.
                float error = ((runs[i] / module) - widths[i]) * step;
                if (error > bestError && widths[i] + step >= 1)
                {
                    bestError = error;
                    best = i;
                }
            }

            widths[best] += step;
            difference -= step;
        }

        return true;
    }

    /// <summary>
    /// Returns the value of a symbol character from its odd and even element widths and the tables of its group.
    /// </summary>
    /// <param name="widths">The eight or fourteen widths in the order of the value: odd set elements at the even indices.</param>
    /// <param name="oddModules">The odd set module count of each group.</param>
    /// <param name="oddWidest">The widest odd element of each group.</param>
    /// <param name="firstGroup">The first group the character can take.</param>
    /// <param name="lastGroup">The last group the character can take.</param>
    /// <param name="oddNoNarrow">Whether the odd set excludes the combinations without a one module element.</param>
    /// <param name="group">The group of the character.</param>
    /// <param name="oddValue">The value of the odd set.</param>
    /// <param name="evenValue">The value of the even set.</param>
    /// <returns><see langword="true"/> when the widths are a character of one of the groups.</returns>
    public static bool TryValues(
        ReadOnlySpan<int> widths,
        ReadOnlySpan<int> oddModules,
        ReadOnlySpan<int> oddWidest,
        int firstGroup,
        int lastGroup,
        bool oddNoNarrow,
        out int group,
        out int oddValue,
        out int evenValue)
    {
        int setElements = widths.Length / 2;
        Span<int> odd = stackalloc int[setElements];
        Span<int> even = stackalloc int[setElements];
        int oddSum = 0;
        int oddMax = 0;
        int evenMax = 0;
        for (int k = 0; k < setElements; k++)
        {
            odd[k] = widths[k * 2];
            even[k] = widths[(k * 2) + 1];
            oddSum += odd[k];
            oddMax = Math.Max(oddMax, odd[k]);
            evenMax = Math.Max(evenMax, even[k]);
        }

        oddValue = 0;
        evenValue = 0;
        for (group = firstGroup; group <= lastGroup; group++)
        {
            if (oddModules[group] != oddSum)
            {
                continue;
            }

            // The widest element of each set, and a one module element in the set that must have one.
            if (oddMax > oddWidest[group] || evenMax > 9 - oddWidest[group])
            {
                return false;
            }

            if ((oddNoNarrow && !odd.Contains(1)) || (!oddNoNarrow && !even.Contains(1)))
            {
                return false;
            }

            oddValue = DataBarWidths.Value(odd, oddWidest[group], oddNoNarrow);
            evenValue = DataBarWidths.Value(even, 9 - oddWidest[group], !oddNoNarrow);
            return true;
        }

        return false;
    }
}
