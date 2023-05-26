using System;

namespace STBootLib;

/// <summary>
/// Boot Exception.
/// </summary>
public class STBootException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="STBootException"/> class.
    /// </summary>
    public STBootException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="STBootException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    public STBootException(string message)
        : base(message)
    {
    }
}
