namespace YoutubeExplode.Videos;

/// <summary>
/// </summary>
public class Comment
{
    /// <summary>
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// </summary>
    public string[] Runs { get; }

    /// <summary>
    /// </summary>
    public string? RepliesId { get; }

    /// <summary>
    /// </summary>
    public long LikeCount { get; }

    /// <summary>
    /// </summary>
    public Comment(string id, string[] runs, string? repliesId, long likeCount)
    {
        Id = id;
        Runs = runs;
        RepliesId = repliesId;
        LikeCount = likeCount;
    }
}

/// <summary>
/// </summary>
public class CommentBatch
{
    /// <summary>
    /// </summary>
    public Comment[] Comments { get; }

    /// <summary>
    /// </summary>
    public string? Continuation { get; }

    /// <summary>
    /// </summary>
    public CommentBatch(Comment[] comments, string? continuation)
    {
        Comments = comments;
        Continuation = continuation;
    }
}
