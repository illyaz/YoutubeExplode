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

internal class VideoController(HttpClient http)
{
    protected HttpClient Http { get; } = http;

    public async ValueTask<VideoWatchPage> GetVideoWatchPageAsync(
        VideoId videoId,
        CancellationToken cancellationToken = default
    )
    {
        for (var retriesRemaining = 5; ; retriesRemaining--)
        {
            var watchPage = VideoWatchPage.TryParse(
                await Http.GetStringAsync(
                    $"https://www.youtube.com/watch?v={videoId}&bpctr=9999999999",
                    cancellationToken
                )
            );

            if (watchPage is null)
            {
                if (retriesRemaining > 0)
                    continue;

                throw new YoutubeExplodeException(
                    "Video watch page is broken. Please try again in a few minutes."
                );
            }

            if (!watchPage.IsAvailable)
                throw new VideoUnavailableException($"Video '{videoId}' is not available.");

            return watchPage;
        }
    }

    public async ValueTask<PlayerResponse> GetPlayerResponseAsync(
        VideoId videoId,
        CancellationToken cancellationToken = default
    )
    {
        // The most optimal client to impersonate is the Android client, because
        // it doesn't require signature deciphering (for both normal and n-parameter signatures).
        // However, the regular Android client has a limitation, preventing it from downloading
        // multiple streams from the same manifest (or the same stream multiple times).
        // As a workaround, we're using ANDROID_TESTSUITE which appears to offer the same
        // functionality, but doesn't impose the aforementioned limitation.
        // https://github.com/Tyrrrz/YoutubeExplode/issues/705
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://www.youtube.com/youtubei/v1/player"
        )
        {
            Content = new StringContent(
                // lang=json
                $$"""
                {
                    "videoId": "{{videoId}}",
                    "context": {
                        "client": {
                            "clientName": "ANDROID_TESTSUITE",
                            "clientVersion": "1.9",
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
            "com.google.android.youtube/17.36.4 (Linux; U; Android 12; GB) gzip"
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
        CancellationToken cancellationToken = default
    )
    {
        // The only client that can handle age-restricted videos without authentication is the
        // TVHTML5_SIMPLY_EMBEDDED_PLAYER client.
        // This client does require signature deciphering, so we only use it as a fallback.
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://www.youtube.com/youtubei/v1/player"
        )
        {
            Content = new StringContent(
                // lang=json
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
                            "signatureTimestamp": "{{signatureTimestamp ?? "19369"}}"
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

    public async ValueTask<PlayerResponse> GetPlayerResponseWebAsync(
        VideoId videoId,
        CancellationToken cancellationToken = default
    )
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://www.youtube.com/youtubei/v1/player"
        )
        {
            Content = new StringContent(
                // lang=json
                $$"""
                {
                    "videoId": "{{videoId}}",
                    "context": {
                        "client": {
                            "clientName": "WEB",
                            "clientVersion": "2.20240419.01.00",
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

        var playerResponse = PlayerResponse.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken)
        );

        if (!playerResponse.IsAvailable)
            throw new VideoUnavailableException($"Video '{videoId}' is not available.");

        return playerResponse;
    }

    public async ValueTask<string?> GetCommentTokenAsync(
        VideoId videoId,
        CancellationToken cancellationToken = default
    )
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://www.youtube.com/youtubei/v1/next?fields=engagementPanels.engagementPanelSectionListRenderer.content.sectionListRenderer.contents.itemSectionRenderer(sectionIdentifier,contents.continuationItemRenderer.continuationEndpoint.continuationCommand)"
        )
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
        request.Headers.Add("User-Agent", "Mozilla/5.0 (Linux; Android 10; SM-G981B) gzip");

        using var response = await Http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var content = Json.Parse(await response.Content.ReadAsStringAsync(cancellationToken));

        return content
            .GetPropertyOrNull("engagementPanels")
            ?.EnumerateArrayOrEmpty()
            .Select(x =>
                x.GetPropertyOrNull("engagementPanelSectionListRenderer")
                    ?.GetPropertyOrNull("content")
                    ?.GetPropertyOrNull("sectionListRenderer")
                    ?.GetPropertyOrNull("contents")
                    ?.EnumerateArrayOrNull()
                    ?.Select(content => content.GetPropertyOrNull("itemSectionRenderer"))
                    .WhereNotNull()
            )
            .WhereNotNull()
            .SelectMany(x => x)
            .Where(x =>
                x.GetPropertyOrNull("sectionIdentifier")?.GetString() == "comment-item-section"
            )
            .FirstOrNull()
            ?.GetPropertyOrNull("contents")
            ?.EnumerateArrayOrNull()
            ?.Select(content =>
                content
                    .GetPropertyOrNull("continuationItemRenderer")
                    ?.GetPropertyOrNull("continuationEndpoint")
                    ?.GetPropertyOrNull("continuationCommand")
                    ?.GetPropertyOrNull("token")
                    ?.GetString()
            )
            .WhereNotNull()
            .FirstOrDefault();
    }

    public async ValueTask<CommentBatch> GetCommentBatchAsync(
        string token,
        CancellationToken cancellationToken = default
    )
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://www.youtube.com/youtubei/v1/next?fields=frameworkUpdates.entityBatchUpdate.mutations.payload.commentEntityPayload(key,properties.content(content,commandRuns(startIndex,length)),toolbar.likeCountLiked),onResponseReceivedEndpoints.*.continuationItems(continuationItemRenderer.continuationEndpoint.continuationCommand.token,commentThreadRenderer(commentViewModel.commentViewModel.commentKey,replies.commentRepliesRenderer.contents.continuationItemRenderer.continuationEndpoint.continuationCommand.token))"
        )
        {
            Content = new StringContent(
                $$"""
                {
                    "continuation": "{{token}}",
                    "context": {
                        "client": {
                            "clientName": "WEB",
                            "clientVersion": "2.20240419.01.00",
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
            .Select(x =>
                x.GetPropertyOrNull("reloadContinuationItemsCommand")
                ?? x.GetProperty("appendContinuationItemsAction")
            )
            .Select(x => x.GetPropertyOrNull("continuationItems")?.EnumerateArrayOrNull())
            .WhereNotNull()
            .SelectMany(x => x);

        var commentEntityPayloads = content
            .GetProperty("frameworkUpdates")
            .GetProperty("entityBatchUpdate")
            .GetProperty("mutations")
            .EnumerateArray()
            .Select(x => x.GetPropertyOrNull("payload")?.GetPropertyOrNull("commentEntityPayload"))
            .WhereNotNull()
            .ToDictionary(
                k =>
                    k.GetProperty("key").GetString()
                    ?? throw new InvalidOperationException("`key` null"),
                v => v
            );

        var comments = new List<Comment>();
        var continuation = null as string;

        foreach (var item in continuationItems)
        {
            if (item.GetPropertyOrNull("commentThreadRenderer") is { } commentThreadRenderer)
            {
                var commentId =
                    commentThreadRenderer
                        .GetProperty("commentViewModel")
                        .GetProperty("commentViewModel")
                        .GetProperty("commentKey")
                        .GetString() ?? throw new InvalidOperationException("`commentId` null");

                var payload = commentEntityPayloads[commentId];
                var payloadContent = payload.GetProperty("properties").GetProperty("content");
                var text = payloadContent.GetProperty("content").GetString()!;
                var texts = new List<string>();
                var commandRuns = payloadContent
                    .GetPropertyOrNull("commandRuns")
                    ?.EnumerateArray()
                    .Select(x =>
                        (
                            x.GetPropertyOrNull("startIndex")?.GetInt32(),
                            x.GetPropertyOrNull("length")?.GetInt32()
                        )
                    )
                    .Where(x => x is { Item1: not null, Item2: not null })
                    .Select(x => (x.Item1!.Value, x.Item2!.Value))
                    .OrderBy(x => x.Item1)
                    .ToList();
                texts.Add(string.Empty);

                var shouldAddNewRun = false;
                for (var i = 0; i < text.Length; i++)
                {
                    if (text[i] == '\r' && text.ElementAtOrNull(++i) == '\n')
                    {
                        texts.Add("\r\n");
                        shouldAddNewRun = true;
                    }
                    else if (text[i] == '\n')
                    {
                        texts.Add("\n");
                        shouldAddNewRun = true;
                    }
                    else
                    {
                        if (shouldAddNewRun)
                        {
                            texts.Add(string.Empty);
                            shouldAddNewRun = false;
                        }

                        if (commandRuns?.ElementAtOrNull(0) is { } x)
                        {
                            var (start, len) = x;

                            if (i > start)
                                commandRuns.RemoveAt(0);
                            else if (start == i)
                            {
                                texts[^1] += text[i..(i + len)];
                                i += len;
                                shouldAddNewRun = true;
                                continue;
                            }
                        }

                        texts[^1] += text[i];
                    }
                }

                if (texts.Count > 0 && texts[^1] == string.Empty)
                    texts.RemoveAt(texts.Count - 1);

                var repliesId = commentThreadRenderer
                    .GetPropertyOrNull("replies")
                    ?.GetPropertyOrNull("commentRepliesRenderer")
                    ?.GetPropertyOrNull("contents")
                    ?.EnumerateArrayOrNull()
                    ?.Select(x =>
                        x.GetPropertyOrNull("continuationItemRenderer")
                            ?.GetPropertyOrNull("continuationEndpoint")
                            ?.GetPropertyOrNull("continuationCommand")
                            ?.GetProperty("token")
                            .GetString()
                    )
                    .WhereNotNull()
                    .FirstOrDefault();

                var likeCount =
                    payload
                        .GetProperty("toolbar")
                        .GetProperty("likeCountLiked")
                        .GetString()!
                        .Pipe(int.Parse) - 1;

                comments.Add(new(commentId, texts.ToArray(), repliesId, likeCount));
            }
            else if (
                item.GetPropertyOrNull("continuationItemRenderer") is { } continuationItemRenderer
            )
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

        return new CommentBatch(comments.ToArray(), continuation);
    }
}
