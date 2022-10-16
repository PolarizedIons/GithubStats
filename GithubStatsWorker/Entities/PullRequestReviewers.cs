namespace GithubStatsWorker.Entities;

public class PullRequestRequestedReviewers
{
    public long PullRequestId { get; set; }
    public long ReviewerId { get; set; }
}
