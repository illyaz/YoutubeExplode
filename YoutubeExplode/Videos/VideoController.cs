using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using YoutubeExplode.Bridge;
using YoutubeExplode.Exceptions;
using YoutubeExplode.Utils;
using YoutubeExplode.Utils.Extensions;

namespace YoutubeExplode.Videos;

internal class VideoController(HttpClient http)
{
    private string? _visitorData;

    protected HttpClient Http { get; } = http;

    private async ValueTask<string> ResolveVisitorDataAsync(
        CancellationToken cancellationToken = default
    )
    {
        if (!string.IsNullOrWhiteSpace(_visitorData))
            return _visitorData;

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "https://www.youtube.com/sw.js_data"
        );

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        request.Headers.Add(
            "User-Agent",
            "com.google.android.youtube/20.10.38 (Linux; U; ANDROID 11) gzip"
        );

        using var response = await Http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        // TODO: move this to a bridge wrapper
        var jsonString = await response.Content.ReadAsStringAsync(cancellationToken);
        if (jsonString.StartsWith(")]}'"))
            jsonString = jsonString[4..];

        var json = Json.Parse(jsonString);

        // This is just an ordered (but unstructured) blob of data
        var value = json[0][2][0][0][13].GetStringOrNull();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new YoutubeExplodeException("Failed to resolve visitor data.");
        }

        return _visitorData = value;
    }

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
        var visitorData = await ResolveVisitorDataAsync(cancellationToken);

        // The most optimal client to impersonate is any mobile client, because they
        // don't require signature deciphering (for both normal and n-parameter signatures).
        // YouTube now requires Proof of Origin (PO) tokens for most Innertube clients (iOS, Android, etc.),
        // causing stream downloads to fail with 403 Forbidden errors. The ANDROID_VR client (Oculus Quest)
        // still works without PO tokens and provides full format access.
        // https://github.com/Tyrrrz/YoutubeExplode/issues/933
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://www.youtube.com/youtubei/v1/player"
        );

        request.Content = new StringContent(
            // lang=json
            $$"""
            {
              "videoId": {{Json.Serialize(videoId)}},
              "contentCheckOk": true,
              "context": {
                "client": {
                  "clientName": "ANDROID_VR",
                  "clientVersion": "1.60.19",
                  "deviceMake": "Oculus",
                  "deviceModel": "Quest 3",
                  "osName": "Android",
                  "osVersion": "12L",
                  "platform": "MOBILE",
                  "visitorData": {{Json.Serialize(visitorData)}},
                  "hl": "en",
                  "gl": "US",
                  "utcOffsetMinutes": 0
                }
              }
            }
            """
        );

        // User agent appears to be sometimes required when impersonating Android
        // https://github.com/iv-org/invidious/issues/3230#issuecomment-1226887639
        request.Headers.Add(
            "User-Agent",
            "com.google.android.apps.youtube.vr.oculus/1.60.19 (Linux; U; Android 12L; Quest 3 Build/SQ3A.220605.009.A1) gzip"
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
        var visitorData = await ResolveVisitorDataAsync(cancellationToken);

        // The only client that can handle age-restricted videos without authentication is the
        // TVHTML5_SIMPLY_EMBEDDED_PLAYER client.
        // This client does require signature deciphering, so we only use it as a fallback.
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://www.youtube.com/youtubei/v1/player"
        );

        request.Content = new StringContent(
            // lang=json
            $$"""
            {
              "videoId": {{Json.Serialize(videoId)}},
              "context": {
                "client": {
                  "clientName": "TVHTML5_SIMPLY_EMBEDDED_PLAYER",
                  "clientVersion": "2.0",
                  "visitorData": {{Json.Serialize(visitorData)}},
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
                  "signatureTimestamp": {{Json.Serialize(signatureTimestamp)}}
                }
              }
            }
            """
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
            ),
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
            ),
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
            "https://www.youtube.com/youtubei/v1/next?fields=frameworkUpdates.entityBatchUpdate.mutations.payload.commentEntityPayload(key,properties.content(content,commandRuns(startIndex,length)),toolbar.likeCountLiked),onResponseReceivedEndpoints.*.continuationItems(continuationItemRenderer.continuationEndpoint.continuationCommand.token,commentThreadRenderer(comment.commentRenderer(commentId,contentText.runs.text,actionButtons.commentActionButtonsRenderer.likeButton.toggleButtonRenderer.accessibilityData.accessibilityData.label),commentViewModel.commentViewModel.commentKey,replies.commentRepliesRenderer.contents.continuationItemRenderer.continuationEndpoint.continuationCommand.token))"
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
            ),
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

        try
        {
            var commentEntityPayloads = content
                .GetPropertyOrNull("frameworkUpdates")
                ?.GetProperty("entityBatchUpdate")
                .GetProperty("mutations")
                .EnumerateArray()
                .Select(x =>
                    x.GetPropertyOrNull("payload")?.GetPropertyOrNull("commentEntityPayload")
                )
                .WhereNotNull()
                .ToDictionary(
                    k =>
                        k.GetProperty("key").GetString() ?? throw new InvalidOperationException(
                            "`key` null"
                        ),
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
                            .GetPropertyOrNull("commentViewModel")
                            ?.GetPropertyOrNull("commentViewModel")
                            ?.GetPropertyOrNull("commentKey")
                            ?.GetString()
                        ?? commentThreadRenderer
                            .GetPropertyOrNull("comment")
                            ?.GetPropertyOrNull("commentRenderer")
                            ?.GetPropertyOrNull("commentId")
                            ?.GetString()
                        ?? throw new InvalidOperationException("`commentId` null");

                    var likeCount = 0;
                    var texts = new List<string>();

                    if (commentEntityPayloads != null)
                    {
                        var payload = commentEntityPayloads[commentId];

                        // It's protobuf, restore original comment id
                        var commentIdBytes = Convert.FromBase64String(
                            WebUtility.UrlDecode(commentId)
                        );
                        commentId = Encoding.ASCII.GetString(commentIdBytes[2..28]);

                        var payloadContent = payload
                            .GetProperty("properties")
                            .GetProperty("content");
                        var text = payloadContent.GetProperty("content").GetString()!;
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
                            if (text[i] == '\n')
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
                                        i += len - 1;
                                        shouldAddNewRun = true;
                                        continue;
                                    }
                                }

                                texts[^1] += text[i];
                            }
                        }

                        if (texts.Count > 0 && texts[^1] == string.Empty)
                            texts.RemoveAt(texts.Count - 1);

                        likeCount =
                            (int)
                                payload
                                    .GetProperty("toolbar")
                                    .GetProperty("likeCountLiked")
                                    .GetString()!
                                    .Pipe(StringExtensions.ParseLongWithSizeSuffix) - 1;
                    }
                    else
                    {
                        var commentRenderer = commentThreadRenderer
                            .GetProperty("comment")
                            .GetProperty("commentRenderer");

                        texts.AddRange(
                            commentRenderer
                                .GetProperty("contentText")
                                .GetPropertyOrNull("runs")
                                ?.EnumerateArray()
                                .Select(run =>
                                    run.GetProperty("text").GetString()
                                    ?? throw new System.InvalidOperationException(
                                        "`runs.text` null"
                                    )
                                )!
                        );

                        likeCount = commentRenderer
                            .GetProperty("actionButtons")
                            .GetProperty("commentActionButtonsRenderer")
                            .GetProperty("likeButton")
                            .GetProperty("toggleButtonRenderer")
                            .GetProperty("accessibilityData")
                            .GetProperty("accessibilityData")
                            .GetProperty("label")
                            .GetString()!
                            .Pipe(StringExtensions.StripNonDigit)
                            .Pipe(int.Parse);
                    }

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

                    comments.Add(new(commentId, texts.ToArray(), repliesId, likeCount));
                }
                else if (
                    item.GetPropertyOrNull("continuationItemRenderer") is
                    { } continuationItemRenderer
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
        catch
        {
            Console.WriteLine(content);
            throw;
        }
    }
}
