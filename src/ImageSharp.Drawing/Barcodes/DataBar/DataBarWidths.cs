// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The element width algorithm every GS1 DataBar symbol character uses. A set of k elements over n modules has a fixed number of width combinations, and a
/// value selects one of them in order: the widths are chosen element by element, each as narrow as the count of combinations still ahead of the value allows.
/// </summary>
internal static class DataBarWidths
{
    /// <summary>
    /// Writes the element widths that encode a value.
    /// </summary>
    /// <param name="widths">The buffer that receives the widths, one for each element.</param>
    /// <param name="value">The value, from zero to one less than the number of combinations.</param>
    /// <param name="modules">The number of modules the elements span.</param>
    /// <param name="maxWidth">The widest an element can be, in modules.</param>
    /// <param name="noNarrow">Whether the calculation excludes the combinations without a one module element.</param>
    public static void Calculate(Span<int> widths, int value, int modules, int maxWidth, bool noNarrow)
    {
        // Each bit of narrowMask is set while the element it stands for is one module wide, so the mask is zero once no element so far is narrow.
        int elements = widths.Length;
        int narrowMask = 0;
        int bar;
        for (bar = 0; bar < elements - 1; bar++)
        {
            int elementWidth = 1;
            narrowMask |= 1 << bar;
            int subValue;
            while (true)
            {
                // The combinations of the remaining modules over the remaining elements with this element at its current width.
                subValue = Combinations(modules - elementWidth - 1, elements - bar - 2);

                // Less those with no one module element, when such combinations are excluded and no element so far is narrow.
                if (noNarrow && narrowMask == 0 && modules - elementWidth - (elements - bar - 1) >= elements - bar - 1)
                {
                    subValue -= Combinations(modules - elementWidth - (elements - bar), elements - bar - 2);
                }

                // Less those with an element wider than the maximum.
                if (elements - bar - 1 > 1)
                {
                    int lessValue = 0;
                    for (int widest = modules - elementWidth - (elements - bar - 2); widest > maxWidth; widest--)
                    {
                        lessValue += Combinations(modules - elementWidth - widest - 1, elements - bar - 3);
                    }

                    subValue -= lessValue * (elements - 1 - bar);
                }
                else if (modules - elementWidth > maxWidth)
                {
                    subValue--;
                }

                // The value lies within the combinations at this width when it falls below their count.
                value -= subValue;
                if (value < 0)
                {
                    break;
                }

                elementWidth++;
                narrowMask &= ~(1 << bar);
            }

            value += subValue;
            modules -= elementWidth;
            widths[bar] = elementWidth;
        }

        // The last element takes the modules that remain.
        widths[bar] = modules;
    }

    /// <summary>
    /// Returns the value that a set of element widths encodes, the inverse of <see cref="Calculate"/>: for each element, the combinations of every narrower
    /// width it could take come before its own width.
    /// </summary>
    /// <param name="widths">The element widths.</param>
    /// <param name="maxWidth">The widest element the set allows.</param>
    /// <param name="noNarrow">Whether the combinations without a one module element are excluded.</param>
    /// <returns>The value.</returns>
    public static int Value(ReadOnlySpan<int> widths, int maxWidth, bool noNarrow)
    {
        int elements = widths.Length;
        int modules = 0;
        foreach (int width in widths)
        {
            modules += width;
        }

        int value = 0;
        int narrowMask = 0;
        for (int bar = 0; bar < elements - 1; bar++)
        {
            int elementWidth = 1;
            narrowMask |= 1 << bar;
            for (; elementWidth < widths[bar]; elementWidth++, narrowMask &= ~(1 << bar))
            {
                // The same counts Calculate subtracts from the value as it widens the element.
                int subValue = Combinations(modules - elementWidth - 1, elements - bar - 2);
                if (noNarrow && narrowMask == 0 && modules - elementWidth - (elements - bar - 1) >= elements - bar - 1)
                {
                    subValue -= Combinations(modules - elementWidth - (elements - bar), elements - bar - 2);
                }

                if (elements - bar - 1 > 1)
                {
                    int lessValue = 0;
                    for (int widest = modules - elementWidth - (elements - bar - 2); widest > maxWidth; widest--)
                    {
                        lessValue += Combinations(modules - elementWidth - widest - 1, elements - bar - 3);
                    }

                    subValue -= lessValue * (elements - 1 - bar);
                }
                else if (modules - elementWidth > maxWidth)
                {
                    subValue--;
                }

                value += subValue;
            }

            modules -= elementWidth;
        }

        return value;
    }

    /// <summary>
    /// Returns the number of combinations of r items from n, n! / (r! (n - r)!), and 1 when r exceeds n. The product runs down from n and divides by the
    /// smaller factorial as it goes, so the intermediate values stay small and every division is exact.
    /// </summary>
    /// <param name="n">The number of items.</param>
    /// <param name="r">The number chosen.</param>
    /// <returns>The number of combinations.</returns>
    private static int Combinations(int n, int r)
    {
        int minDenominator;
        int maxDenominator;
        if (n - r > r)
        {
            minDenominator = r;
            maxDenominator = n - r;
        }
        else
        {
            minDenominator = n - r;
            maxDenominator = r;
        }

        int value = 1;
        int j = 1;
        for (int i = n; i > maxDenominator; i--)
        {
            value *= i;
            if (j <= minDenominator)
            {
                value /= j;
                j++;
            }
        }

        for (; j <= minDenominator; j++)
        {
            value /= j;
        }

        return value;
    }
}
