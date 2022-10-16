namespace GithubStatsWorker.Entities;

public class PullRequestCommits
{
    public long PullRequestId { get; set; }
    public long? UserId { get; set; }
    public string Sha { get; set; } = null!;
}
