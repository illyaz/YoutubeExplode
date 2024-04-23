namespace YoutubeExplode.Exceptions;

/// <summary>
/// Exception thrown when the requested channel is unavailable.
/// </summary>
public class ChannelUnavailableException : YoutubeExplodeException
{
    /// <summary>
    /// Initializes an instance of <see cref="ChannelUnavailableException" />.
    /// </summary>
    public ChannelUnavailableException(string message)
        : base(message) { }
}
