using Octokit;

namespace GithubStatsWorker.Entities;

public class PullRequestReviews
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public long PullRequestId { get; set; }
    public string State { get; set; } = null!;
    public string Body { get; set; } = null!;
    public DateTimeOffset? SubmittedAt { get; set; }

    public bool IsLatestReview { get; set; }

    internal string UserName { get; set; } = null!;
}
