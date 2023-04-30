namespace YoutubeExplode.Videos;

public class Comment
{
    public string Id { get; }
    public string[] Runs { get; }
    public string? RepliesId { get; }
    public long LikeCount { get; }

    public Comment(
        string id,
        string[] runs,
        string? repliesId,
        long likeCount)
    {
        Id = id;
        Runs = runs;
        RepliesId = repliesId;
        LikeCount = likeCount;
    }
}

public class CommentBatch
{
    public Comment[] Comments { get; }
    public string? Continuation { get; }

    public CommentBatch(
        Comment[] comments,
        string? continuation)
    {
        Comments = comments;
        Continuation = continuation;
    }
}

