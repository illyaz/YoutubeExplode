using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using YoutubeExplode.Common;

namespace YoutubeExplode.Channels;

/// <summary>
/// Metadata associated with a YouTube channel.
/// </summary>
public class Channel : IChannel
{
    /// <inheritdoc />
    public ChannelId Id { get; }

    /// <inheritdoc />
    public ChannelHandle? Handle { get; init; }

    /// <inheritdoc />
    public string Description { get; init; }

    /// <inheritdoc />
    public long? VideoCount { get; init; }

    /// <inheritdoc />
    public long? SubscriberCount { get; init; }

    /// <inheritdoc />
    public IReadOnlyList<Thumbnail> Banners { get; init; }

    /// <inheritdoc />
    public string Url => $"https://www.youtube.com/channel/{Id}";

    /// <inheritdoc />
    public string Title { get; }

    /// <inheritdoc />
    public IReadOnlyList<Thumbnail> Thumbnails { get; }

    /// <summary>
    /// Initializes an instance of <see cref="Channel" />.
    /// </summary>
    public Channel(ChannelId id, string title, IReadOnlyList<Thumbnail> thumbnails)
    {
        Id = id;
        Title = title;
        Thumbnails = thumbnails;
    }

    /// <inheritdoc />
    [ExcludeFromCodeCoverage]
    public override string ToString() => $"Channel ({Title})";
}
