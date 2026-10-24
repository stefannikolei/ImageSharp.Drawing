// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Finds the connected dark areas of a binarized image, four-connected, with the bounding box and the pixel count of each. The labeling works on the dark runs
/// of the rows: each run joins the runs of the row above that it touches, through a union-find forest of the runs, so the image is read once.
/// </summary>
internal static class Components
{
    /// <summary>
    /// Finds the connected dark areas of an image.
    /// </summary>
    /// <param name="bits">The binarized image.</param>
    /// <returns>The areas, in the order of their first pixel.</returns>
    public static List<Component> Find(BitImage bits)
    {
        // The runs of every row: their start, their end (exclusive) and their parent in the forest.
        List<int> starts = [];
        List<int> ends = [];
        List<int> rows = [];
        List<int> parents = [];
        int previousFirst = 0;
        int previousCount = 0;
        for (int y = 0; y < bits.Height; y++)
        {
            ReadOnlySpan<byte> row = bits.Row(y);
            int first = starts.Count;
            int x = 0;
            while (x < row.Length)
            {
                int start = row[x..].IndexOf((byte)1);
                if (start < 0)
                {
                    break;
                }

                start += x;
                int end = row[start..].IndexOf((byte)0);
                end = end < 0 ? row.Length : start + end;
                int index = starts.Count;
                starts.Add(start);
                ends.Add(end);
                rows.Add(y);
                parents.Add(index);

                // A run of the row above touches this one when their columns overlap.
                for (int p = previousFirst; p < previousFirst + previousCount; p++)
                {
                    if (starts[p] < end && ends[p] > start)
                    {
                        Union(parents, index, p);
                    }
                    else if (starts[p] >= end)
                    {
                        break;
                    }
                }

                x = end;
            }

            previousFirst = first;
            previousCount = starts.Count - first;
        }

        // Every root becomes one component, which takes the bounds and the pixels of its runs.
        Dictionary<int, int> componentOf = [];
        List<Component> components = [];
        for (int i = 0; i < starts.Count; i++)
        {
            int root = Find(parents, i);
            if (!componentOf.TryGetValue(root, out int c))
            {
                c = components.Count;
                componentOf.Add(root, c);
                components.Add(new Component(starts[i], rows[i], ends[i] - 1, rows[i], 0));
            }

            Component component = components[c];
            components[c] = new Component(
                Math.Min(component.Left, starts[i]),
                Math.Min(component.Top, rows[i]),
                Math.Max(component.Right, ends[i] - 1),
                Math.Max(component.Bottom, rows[i]),
                component.Pixels + (ends[i] - starts[i]));
        }

        return components;
    }

    /// <summary>
    /// Returns the root of a run in the forest, halving the path on the way.
    /// </summary>
    /// <param name="parents">The parent of every run.</param>
    /// <param name="i">The run.</param>
    /// <returns>The root.</returns>
    private static int Find(List<int> parents, int i)
    {
        while (parents[i] != i)
        {
            parents[i] = parents[parents[i]];
            i = parents[i];
        }

        return i;
    }

    /// <summary>
    /// Joins the trees of two runs.
    /// </summary>
    /// <param name="parents">The parent of every run.</param>
    /// <param name="a">The first run.</param>
    /// <param name="b">The second run.</param>
    private static void Union(List<int> parents, int a, int b)
    {
        int rootA = Find(parents, a);
        int rootB = Find(parents, b);
        if (rootA != rootB)
        {
            parents[Math.Max(rootA, rootB)] = Math.Min(rootA, rootB);
        }
    }
}
