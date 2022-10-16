using Octokit;

namespace GithubStatsWorker.Entities;

public class PullRequestReviews
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public long PullRequestId { get; set; }
    public StringEnum<PullRequestReviewState> State { get; set; }
    public string Body { get; set; } = null!;
    public DateTimeOffset SubmittedAt { get; set; }

    public bool IsLatestReview { get; set; }
}
