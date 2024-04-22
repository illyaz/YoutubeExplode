using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using YoutubeExplode.Bridge;
using YoutubeExplode.Common;
using YoutubeExplode.Exceptions;
using YoutubeExplode.Playlists;
using YoutubeExplode.Utils;
using YoutubeExplode.Utils.Extensions;

namespace YoutubeExplode.Channels;

/// <summary>
/// Operations related to YouTube channels.
/// </summary>
public class ChannelClient
{
    private readonly HttpClient _http;
    private readonly ChannelController _controller;

    /// <summary>
    /// Initializes an instance of <see cref="ChannelClient" />.
    /// </summary>
    public ChannelClient(HttpClient http)
    {
        _http = http;
        _controller = new ChannelController(http);
    }

    private Channel Get(ChannelPage channelPage)
    {
        var channelId =
            channelPage.Id ??
            throw new YoutubeExplodeException("Could not extract channel ID.");

        var title =
            channelPage.Title ??
            throw new YoutubeExplodeException("Could not extract channel title.");

        var logoUrl =
            channelPage.LogoUrl ??
            throw new YoutubeExplodeException("Could not extract channel logo URL.");

        var logoSize = Regex
            .Matches(logoUrl, @"\bs(\d+)\b")
            .ToArray()
            .LastOrDefault()?
            .Groups[1]
            .Value
            .NullIfWhiteSpace()?
            .ParseIntOrNull() ?? 100;

        var thumbnails = new[]
        {
            new Thumbnail(logoUrl, new Resolution(logoSize, logoSize))
        };

        return new Channel(channelId, title, thumbnails);
    }

    /// <summary>
    /// Gets the metadata associated with the specified channel.
    /// </summary>
    public async ValueTask<Channel> GetAsync(
        ChannelId channelId,
        CancellationToken cancellationToken = default) =>
        Get(await _controller.GetChannelPageAsync(channelId, cancellationToken));

    /// <summary>
    /// Gets the metadata associated with the channel of the specified user.
    /// </summary>
    public async ValueTask<Channel> GetByUserAsync(
        UserName userName,
        CancellationToken cancellationToken = default) =>
        Get(await _controller.GetChannelPageAsync(userName, cancellationToken));

    /// <summary>
    /// Gets the metadata associated with the channel identified by the specified slug or legacy custom URL.
    /// </summary>
    public async ValueTask<Channel> GetBySlugAsync(
        ChannelSlug channelSlug,
        CancellationToken cancellationToken = default) =>
        Get(await _controller.GetChannelPageAsync(channelSlug, cancellationToken));

    /// <summary>
    /// Gets the metadata associated with the channel identified by the specified handle or custom URL.
    /// </summary>
    public async ValueTask<Channel> GetByHandleAsync(
        ChannelHandle channelHandle,
        CancellationToken cancellationToken = default) =>
        Get(await _controller.GetChannelPageAsync(channelHandle, cancellationToken));

    /// <summary>
    /// Enumerates videos uploaded by the specified channel.
    /// </summary>
    // TODO: should return <IVideo> sequence instead (breaking change)
    public IAsyncEnumerable<PlaylistVideo> GetUploadsAsync(
        ChannelId channelId,
        CancellationToken cancellationToken = default)
    {
        // Replace 'UC' in the channel ID with 'UU'
        var playlistId = "UU" + channelId.Value[2..];
        return new PlaylistClient(_http).GetVideosAsync(playlistId, cancellationToken);
    }

    public async ValueTask<ExtendedChannel> GetInnertubeAsync(
        ChannelId channelId,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://www.youtube.com/youtubei/v1/browse?fields=metadata.channelMetadataRenderer(title,description,externalId,avatar,vanityChannelUrl),header.c4TabbedHeaderRenderer(subscriberCountText.runs.text,videosCountText.runs.text,banner.thumbnails),alerts")
        {
            Content = new StringContent(
                $$"""
                {
                    "browseId": "{{channelId}}",
                    "params": "EgVhYm91dPIGBAoCEgA%3D",
                    "context": {
                        "client": {
                            "clientName": "MWEB",
                            "clientVersion": "2.20230420.05.00",
                            "hl": "en",
                            "gl": "US",
                            "utcOffsetMinutes": 0
                        }
                    }
                }
                """
            )
        };

        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var content = Json.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var alertRenderer = content
            .GetPropertyOrNull("alerts")?
            .EnumerateArrayOrNull()?
            .Select(x => x.GetPropertyOrNull("alertRenderer"))
            .WhereNotNull()
            .FirstOrNull()?
            .GetPropertyOrNull("text")?
            .GetPropertyOrNull("simpleText")?
            .GetString();

        if (alertRenderer != null)
            throw new ChannelUnavailableException(alertRenderer);

        var channelMetadataRenderer = content
            .GetPropertyOrNull("metadata")?
            .GetPropertyOrNull("channelMetadataRenderer")
            ?? throw new YoutubeExplodeException("Could not extract channelMetadataRenderer");

        var c4TabbedHeaderRenderer = content
            .GetPropertyOrNull("header")?
            .GetPropertyOrNull("c4TabbedHeaderRenderer")
            ?? throw new YoutubeExplodeException("Could not extract c4TabbedHeaderRenderer");

        return new ExtendedChannel(
            channelMetadataRenderer.GetProperty("externalId").GetString() ?? throw new YoutubeExplodeException("Could not extract channel ID."),
            ChannelHandle.TryParse(channelMetadataRenderer.GetProperty("vanityChannelUrl").GetString()
                ?? throw new YoutubeExplodeException("Could not extract channel vanity.")),
            channelMetadataRenderer.GetProperty("title").GetString() ?? throw new YoutubeExplodeException("Could not extract channel title."),
            channelMetadataRenderer.GetProperty("description").GetString()
                ?? throw new YoutubeExplodeException("Could not extract channel description."),
            c4TabbedHeaderRenderer.GetPropertyOrNull("videosCountText")?
                .GetProperty("runs")
                .EnumerateArray()
                .First()
                .GetProperty("text")
                .GetString()!
                .Pipe(StringExtensions.StripNonDigit)
                .Pipe(x => string.IsNullOrEmpty(x) ? "0" : x)
                .Pipe(long.Parse) ?? 0,
            c4TabbedHeaderRenderer.GetPropertyOrNull("subscriberCountText")?
                .GetProperty("runs")
                .EnumerateArray()
                .First()
                .GetProperty("text")
                .GetString()!
                .Pipe(x => x.SubstringUntil(" "))
                .Pipe(StringExtensions.ParseLongWithSizeSuffix),
            channelMetadataRenderer
                .GetProperty("avatar")
                .GetProperty("thumbnails")
                .EnumerateArrayOrEmpty()
                .Select(x => new Thumbnail(
                    x.GetPropertyOrNull("url")?.GetString() ?? throw new YoutubeExplodeException("Could not extract thumbnail url."),
                    new Resolution(
                        x.GetProperty("width").GetInt32(),
                        x.GetProperty("height").GetInt32()))).ToArray(),
             c4TabbedHeaderRenderer
                .GetPropertyOrNull("banner")?
                .GetPropertyOrNull("thumbnails")?
                .EnumerateArrayOrEmpty()
                .Select(x => new Thumbnail(
                    x.GetPropertyOrNull("url")?.GetString() ?? throw new YoutubeExplodeException("Could not extract thumbnail url."),
                    new Resolution(
                        x.GetProperty("width").GetInt32(),
                        x.GetProperty("height").GetInt32()))).ToArray() ?? Array.Empty<Thumbnail>());
    }
}