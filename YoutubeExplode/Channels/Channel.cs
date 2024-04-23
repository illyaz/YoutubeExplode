using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using YoutubeExplode.Common;

namespace YoutubeExplode.Channels;

/// <summary>
/// Metadata associated with a YouTube channel.
/// </summary>
public class Channel(ChannelId id, string title, IReadOnlyList<Thumbnail> thumbnails) : IChannel
{
    /// <inheritdoc />
    public ChannelId Id { get; } = id;

    /// <inheritdoc />
    public string Url => $"https://www.youtube.com/channel/{Id}";

    /// <inheritdoc />
    public string Title { get; } = title;

    /// <inheritdoc />
    public IReadOnlyList<Thumbnail> Thumbnails { get; } = thumbnails;

    /// <inheritdoc />
    [ExcludeFromCodeCoverage]
    public override string ToString() => $"Channel ({Title})";
}

/// <inheritdoc />
public class ExtendedChannel : Channel
{
    /// <inheritdoc />
    public ChannelHandle? Handle { get; }

    /// <inheritdoc />
    public string Description { get; }

    /// <inheritdoc />
    public long? VideoCount { get; }

    /// <inheritdoc />
    public long? SubscriberCount { get; }

    /// <inheritdoc />
    public IReadOnlyList<Thumbnail> Banners { get; }

    /// <inheritdoc />
    public ExtendedChannel(ChannelId id, ChannelHandle? handle, string title,
        string description, long videoCount, long? subscriberCount, 
        IReadOnlyList<Thumbnail> thumbnails, 
        IReadOnlyList<Thumbnail> banners) 
        : base(id, title, thumbnails)
    {
        Handle = handle;
        Description = description;
        VideoCount = videoCount;
        SubscriberCount = subscriberCount;
        Banners = banners;
    }
}
