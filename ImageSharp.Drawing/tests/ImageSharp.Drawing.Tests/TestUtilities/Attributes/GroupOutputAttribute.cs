// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Tests;

/// <summary>
/// The output produced by this test class should be grouped into the specified subfolder.
/// </summary>
public class GroupOutputAttribute : Attribute
{
    public GroupOutputAttribute(string subfolder)
    {
        this.Subfolder = subfolder;
    }

    public string Subfolder { get; }
}
