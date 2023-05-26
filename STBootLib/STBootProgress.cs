namespace STBootLib;

/// <summary>
/// Boot progress.
/// </summary>
public class STBootProgress
{
    /// <summary>
    /// Initializes a new instance of the <see cref="STBootProgress"/> class.
    /// </summary>
    /// <param name="bytesProcessed">The bytes processed.</param>
    /// <param name="bytesTotal">The bytes total.</param>
    public STBootProgress(int bytesProcessed, int bytesTotal)
    {
        this.BytesProcessed = bytesProcessed;
        this.BytesTotal = bytesTotal;
    }

    /// <summary>
    /// Gets the total bytes.
    /// </summary>
    public int BytesTotal { get; }

    /// <summary>
    /// Gets the bytes processed.
    /// </summary>
    public int BytesProcessed { get; }
}
