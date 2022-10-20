namespace GithubStatsWorker.Entities;

public class PullRequestRequestedReviewer
{
    public long PullRequestId { get; set; }
    public long ReviewerId { get; set; }
    internal string ReviewerName { get; set; } = null!;
}
