namespace GithubStatsWorker.Entities;

public class PullRequestCommits
{
    public long PullRequestId { get; set; }
    public long UserId { get; set; }
    public string Sha { get; set; } = null!;

    public string UserName { get; set; } = null!;
    public string UserEmail { get; set; } = null!;
    internal string Message { get; set; }
    internal DateTimeOffset Date { get; set; }
}
