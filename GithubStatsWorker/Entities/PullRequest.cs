using Octokit;
using Octokit.GraphQL;
using Octokit.GraphQL.Core;

namespace GithubStatsWorker.Entities;

public class PullRequest
{
    public long Id { get; set; }
    public long Number { get; set; }
    public string State { get; set; }
    public string Title { get; set; }
    public string Body { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public DateTimeOffset? MergedAt { get; set; }

    public long CommentsCount { get; set; }
    public long CommitsCount { get; set; }
    public long AdditionsCount { get; set; }
    public long DeletionsCount { get; set; }
    public long ChangedFilesCount { get; set; }


    public long? CreatorUserId { get; set; }
    public long? MergerUserId { get; set; }
    public long RepoId { get; set; }

    public string TargetRef { get; set; }
    public string FromRef { get; set; }
    internal List<PullRequestRequestedReviewer> RequestedReviewerIds { get; set; }
    internal List<PullRequestReviews> Reviews { get; set; }
    internal List<PullRequestCommits> Commits { get; set; }
    internal string CreatorUserName { get; set; } = null!;
    public string MergerUserName { get; set; } = null!;

    public bool CreatorIsHuman { get; set; }
    public bool MergerIsHuman { get; set; }

    public GQLPagedResponse<PullRequestFile> FirstPageFiles { get; set; }
}
