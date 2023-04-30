using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using YoutubeExplode.Bridge;
using YoutubeExplode.Exceptions;
using YoutubeExplode.Utils;
using YoutubeExplode.Utils.Extensions;

namespace YoutubeExplode.Videos;

internal class VideoController
{
    protected HttpClient Http { get; }

    public VideoController(HttpClient http) => Http = http;

    public async ValueTask<VideoWatchPage> GetVideoWatchPageAsync(
        VideoId videoId,
        CancellationToken cancellationToken = default)
    {
        for (var retriesRemaining = 5; ; retriesRemaining--)
        {
            var watchPage = VideoWatchPage.TryParse(
                await Http.GetStringAsync($"https://www.youtube.com/watch?v={videoId}&bpctr=9999999999", cancellationToken)
            );

            if (watchPage is null)
            {
                if (retriesRemaining > 0)
                    continue;

                throw new YoutubeExplodeException(
                    "Video watch page is broken. " +
                    "Please try again in a few minutes."
                );
            }

            if (!watchPage.IsAvailable)
                throw new VideoUnavailableException($"Video '{videoId}' is not available.");

            return watchPage;
        }
    }

    public async ValueTask<PlayerResponse> GetPlayerResponseAsync(
        VideoId videoId,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://www.youtube.com/youtubei/v1/player?fields=videoDetails,microformat")
        {
            Content = new StringContent(
                $$"""
                {
                    "videoId": "{{videoId}}",
                    "context": {
                        "client": {
                            "clientName": "MWEB",
                            "clientVersion": "2.20230420.05.00",
                            "androidSdkVersion": 30,
                            "hl": "en",
                            "gl": "US",
                            "utcOffsetMinutes": 0
                        }
                    }
                }
                """
            )
        };

        // User agent appears to be sometimes required when impersonating Android
        // https://github.com/iv-org/invidious/issues/3230#issuecomment-1226887639
        request.Headers.Add(
            "User-Agent",
            "Mozilla/5.0 (Linux; Android 10; SM-G981B) gzip"
        );

        using var response = await Http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var playerResponse = PlayerResponse.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken)
        );

        if (!playerResponse.IsAvailable)
            throw new VideoUnavailableException($"Video '{videoId}' is not available.");

        return playerResponse;
    }

    public async ValueTask<PlayerResponse> GetPlayerResponseAsync(
        VideoId videoId,
        string? signatureTimestamp,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://www.youtube.com/youtubei/v1/player")
        {
            Content = new StringContent(
                $$"""
                {
                    "videoId": "{{videoId}}",
                    "context": {
                        "client": {
                            "clientName": "TVHTML5_SIMPLY_EMBEDDED_PLAYER",
                            "clientVersion": "2.0",
                            "hl": "en",
                            "gl": "US",
                            "utcOffsetMinutes": 0
                        },
                        "thirdParty": {
                            "embedUrl": "https://www.youtube.com"
                        }
                    },
                    "playbackContext": {
                        "contentPlaybackContext": {
                            "signatureTimestamp": "{{signatureTimestamp}}"
                        }
                    }
                }
                """
            )
        };

        using var response = await Http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var playerResponse = PlayerResponse.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken)
        );

        if (!playerResponse.IsAvailable)
            throw new VideoUnavailableException($"Video '{videoId}' is not available.");

        return playerResponse;
    }

    public async ValueTask<string?> GetCommentTokenAsync(
        VideoId videoId,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://www.youtube.com/youtubei/v1/next?fields=engagementPanels.engagementPanelSectionListRenderer.content.sectionListRenderer.contents.itemSectionRenderer(sectionIdentifier,contents.continuationItemRenderer.continuationEndpoint.continuationCommand)")
        {
            Content = new StringContent(
                $$"""
                {
                    "videoId": "{{videoId}}",
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

        // User agent appears to be sometimes required when impersonating Android
        // https://github.com/iv-org/invidious/issues/3230#issuecomment-1226887639
        request.Headers.Add(
            "User-Agent",
            "Mozilla/5.0 (Linux; Android 10; SM-G981B) gzip"
        );

        using var response = await Http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var content = Json.Parse(await response.Content.ReadAsStringAsync(cancellationToken));

        return content
            .GetPropertyOrNull("engagementPanels")?
            .EnumerateArrayOrEmpty()
            .Select(x => x
                .GetPropertyOrNull("engagementPanelSectionListRenderer")?
                .GetPropertyOrNull("content")?
                .GetPropertyOrNull("sectionListRenderer")?
                .GetPropertyOrNull("contents")?
                .EnumerateArrayOrNull()?
                .Select(content => content.GetPropertyOrNull("itemSectionRenderer"))
            .WhereNotNull())
            .WhereNotNull()
            .SelectMany(x => x)
            .Where(x => x.GetPropertyOrNull("sectionIdentifier")?.GetString() == "comment-item-section")
            .FirstOrNull()?
            .GetPropertyOrNull("contents")?
            .EnumerateArrayOrNull()?
            .Select(content => content
                .GetPropertyOrNull("continuationItemRenderer")?
                .GetPropertyOrNull("continuationEndpoint")?
                .GetPropertyOrNull("continuationCommand")?
                .GetPropertyOrNull("token")?
                .GetString())
            .WhereNotNull()
            .FirstOrDefault();
    }

    public async ValueTask<CommentBatch> GetCommentBatchAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://www.youtube.com/youtubei/v1/next?fields=onResponseReceivedEndpoints.*.continuationItems(continuationItemRenderer.continuationEndpoint.continuationCommand.token,commentThreadRenderer(comment.commentRenderer(commentId,contentText(runs.text),actionButtons.commentActionButtonsRenderer.likeButton.toggleButtonRenderer.accessibilityData.accessibilityData.label),replies.commentRepliesRenderer.contents.continuationItemRenderer.continuationEndpoint.continuationCommand.token))")
        {
            Content = new StringContent(
                $$"""
                {
                    "continuation": "{{token}}",
                    "context": {
                        "client": {
                            "clientName": "WEB",
                            "clientVersion": "2.20230421.01.00",
                            "hl": "en",
                            "gl": "US",
                            "utcOffsetMinutes": 0
                        }
                    }
                }
                """
            )
        };

        using var response = await Http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var content = Json.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var continuationItems = content
            .GetProperty("onResponseReceivedEndpoints")
            .EnumerateArray()
            .Select(x => x.GetPropertyOrNull("reloadContinuationItemsCommand") ?? x.GetProperty("appendContinuationItemsAction"))
            .Select(x => x.GetPropertyOrNull("continuationItems")?.EnumerateArrayOrNull())
            .WhereNotNull()
            .SelectMany(x => x);

        var comments = new List<Comment>();
        var continuation = null as string;

        foreach (var item in continuationItems)
        {
            if (item.GetPropertyOrNull("commentThreadRenderer")
                is JsonElement commentThreadRenderer)
            {
                var commentRenderer = commentThreadRenderer
                    .GetProperty("comment")
                    .GetProperty("commentRenderer");

                var commentId = commentRenderer
                    .GetProperty("commentId")
                    .GetString() ?? throw new InvalidOperationException("`commentId` null");

                var texts = commentRenderer
                    .GetProperty("contentText")
                    .GetPropertyOrNull("runs")?
                    .EnumerateArray()
                    .Select(run => run
                        .GetProperty("text")
                        .GetString() ?? throw new System.InvalidOperationException("`runs.text` null"))
                    .ToArray();

                var repliesId = commentThreadRenderer
                    .GetPropertyOrNull("replies")?
                    .GetPropertyOrNull("commentRepliesRenderer")?
                    .GetPropertyOrNull("contents")?
                    .EnumerateArrayOrNull()?
                    .Select(content => content
                        .GetPropertyOrNull("continuationItemRenderer")?
                        .GetPropertyOrNull("continuationEndpoint")?
                        .GetPropertyOrNull("continuationCommand")?
                        .GetProperty("token")
                        .GetString())
                    .WhereNotNull()
                    .FirstOrDefault();

                var likeCount = commentRenderer
                    .GetProperty("actionButtons")
                    .GetProperty("commentActionButtonsRenderer")
                    .GetProperty("likeButton")
                    .GetProperty("toggleButtonRenderer")
                    .GetProperty("accessibilityData")
                    .GetProperty("accessibilityData")
                    .GetProperty("label")
                    .GetString()!
                    .Pipe(x => string.Join("", x.Where(char.IsNumber)))
                    .Pipe(int.Parse);

                comments.Add(new(commentId, texts ?? Array.Empty<string>(), repliesId, likeCount));

            }
            else if (item.GetPropertyOrNull("continuationItemRenderer")
                is JsonElement continuationItemRenderer)
            {
                continuation = continuationItemRenderer
                    .GetProperty("continuationEndpoint")
                    .GetProperty("continuationCommand")
                    .GetProperty("token")
                    .GetString();
            }
            else
                continue;
        }

        return new CommentBatch(
            comments.ToArray(),
            continuation);
    }
}