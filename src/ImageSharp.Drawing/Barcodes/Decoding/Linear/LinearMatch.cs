// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// A symbol that a <see cref="LinearReader"/> found on one scan line.
/// </summary>
internal readonly struct LinearMatch
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LinearMatch"/> struct.
    /// </summary>
    /// <param name="family">The family.</param>
    /// <param name="text">The transmitted data.</param>
    /// <param name="identifier">The symbology identifier.</param>
    /// <param name="startRun">The index of the first bar of the symbol.</param>
    /// <param name="endRun">The index of the light run after the last bar of the symbol.</param>
    /// <param name="checkedData">Whether a check character or a fixed structure proves the data.</param>
    public LinearMatch(BarcodeFamily family, string text, string identifier, int startRun, int endRun, bool checkedData)
    {
        this.Family = family;
        this.Text = text;
        this.Identifier = identifier;
        this.StartRun = startRun;
        this.EndRun = endRun;
        this.CheckedData = checkedData;
    }

    /// <summary>
    /// Gets the family.
    /// </summary>
    public BarcodeFamily Family { get; }

    /// <summary>
    /// Gets the transmitted data.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Gets the symbology identifier.
    /// </summary>
    public string Identifier { get; }

    /// <summary>
    /// Gets the index of the first bar of the symbol.
    /// </summary>
    public int StartRun { get; }

    /// <summary>
    /// Gets the index of the light run after the last bar of the symbol.
    /// </summary>
    public int EndRun { get; }

    /// <summary>
    /// Gets a value indicating whether a check character or a fixed structure proves the data, so one scan line is enough to accept it. A symbol without
    /// such a proof must read the same on two scan lines.
    /// </summary>
    public bool CheckedData { get; }
}
