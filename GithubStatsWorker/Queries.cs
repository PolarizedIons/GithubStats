using System.Text.RegularExpressions;
using GithubStatsWorker.Entities;
using Octokit.GraphQL;
using Octokit.GraphQL.Model;
using static Octokit.GraphQL.Variable;
using Commit = GithubStatsWorker.Entities.Commit;
using PullRequest = GithubStatsWorker.Entities.PullRequest;
using User = GithubStatsWorker.Entities.User;

namespace GithubStatsWorker;

public static class Queries
{
    public static readonly ICompiledQuery<string> GetRepoDefaultBranch = new Query()
        .Repository(Var("repoName"), Var("repoOwner"))
        .DefaultBranchRef
        .Select(x => x.Name)
        .Compile();

    public static readonly ICompiledQuery<GQLPagedResponse<Commit>> GetAllCommitsForRepo = new Query()
        .Repository(Var("repoName"), Var("repoOwner"))
        .DefaultBranchRef
        .Target
        .Cast<Octokit.GraphQL.Model.Commit>()
        .History(first: 100, after: Var("after"))
        .Select(connection => new GQLPagedResponse<Commit>
        {
            HasNextPage = connection.PageInfo.HasNextPage,
            EndCursor = connection.PageInfo.EndCursor,
            Items = connection.Nodes
                .Select(commit => new Commit
                {
                    Date = commit.CommittedDate,
                    Message = commit.Message,
                    Sha = commit.Oid,
                    RepoId = commit.Repository.DatabaseId ?? 0L,
                    UserId = commit.Committer.Select(x => x.User).Select(x => x.DatabaseId ?? 0L).SingleOrDefault(),
                    UserName = commit.Committer.Name,
                    UserEmail = commit.Committer.Email,
                })
                .ToList()
        })
        .Compile();

    private static readonly IssueOrder PROrder = new()
    {
        Field = IssueOrderField.CreatedAt,
        Direction = OrderDirection.Asc,
    };

    private static readonly Regex AvatarIdRegex = new("https:\\/\\/avatars\\.githubusercontent\\.com\\/u\\/(\\d+).*");

    public static readonly ICompiledQuery<User> GetUser = new Query()
        .User(Var("login"))
        .Select(user => new User
        {
            Id = user.DatabaseId ?? 0L,
            Username = user.Login,
            Email = user.Email,
        })
        .Compile();

    private static long? AvatarUrlToId(this string url) => AvatarIdRegex.IsMatch(url) ? long.Parse(AvatarIdRegex.Match(url).Groups[1].Value) : null;

    public static readonly ICompiledQuery<GQLPagedResponse<PullRequest>> GetAllPrsForRepo = new Query()
        .Repository(Var("repoName"), Var("repoOwner"))
        .PullRequests(orderBy: PROrder, states: new [] { PullRequestState.Open, PullRequestState.Merged, PullRequestState.Closed }, first: 10, after: Var("after"))
        .Select(connection => new GQLPagedResponse<PullRequest>
        {
            HasNextPage = connection.PageInfo.HasNextPage,
            EndCursor = connection.PageInfo.EndCursor,
            Items = connection.Nodes
                .Select(pr => new PullRequest {
                        Id = pr.DatabaseId ?? 0L,
                        Number = pr.Number,
                        State = pr.State.ToString(),
                        Title = pr.Title,
                        Body = pr.Body,
                        CreatedAt = pr.CreatedAt,
                        UpdatedAt = pr.UpdatedAt,
                        ClosedAt = pr.ClosedAt,
                        MergedAt = pr.MergedAt,
                        CommentsCount = pr.Comments(null, null, null, null, null).TotalCount,
                        CommitsCount = pr.Commits(null, null, null, null).TotalCount,
                        AdditionsCount = pr.Additions,
                        DeletionsCount = pr.Deletions,
                        ChangedFilesCount = pr.ChangedFiles,
                        CreatorUserId = pr.Author.AvatarUrl(100).AvatarUrlToId(),
                        CreatorIsHuman = pr.Author.ResourcePath.ToString().StartsWith("/" + pr.Author.Login),
                        CreatorUserName = pr.Author.Login,
                        MergerUserId = pr.MergedBy.Select(x => x.AvatarUrl(100).AvatarUrlToId()).SingleOrDefault(),
                        MergerUserName = pr.MergedBy.Select(x => x.Login).SingleOrDefault(),
                        MergerIsHuman = pr.MergedBy.Select(x => x.ResourcePath.ToString().StartsWith("/" + pr.MergedBy.Login)).SingleOrDefault(),
                        RepoId = pr.Repository.DatabaseId ?? 0L,
                        TargetRef = pr.BaseRefName,
                        FromRef = pr.HeadRefName,

                        // Assuming there will be no more than 100 reviewers
                        RequestedReviewerIds = pr.ReviewRequests(100, null, null, null)
                            .Nodes
                            .Select(reviewRequest => new PullRequestRequestedReviewer
                                {
                                  ReviewerId = reviewRequest.RequestedReviewer
                                      .Switch<long>(when => when
                                          .User(x => x.DatabaseId ?? 0L)
                                      ),
                                  ReviewerName = reviewRequest.RequestedReviewer
                                      .Switch<string>(when => when
                                          .User(x => x.Login)
                                      ),
                                  PullRequestId = pr.DatabaseId ?? 0L,
                                })
                            .ToList(),
                        
                        // Assuming there will be no more than 100 reviews
                        Reviews = pr.Reviews(100, null, null, null, null, null)
                            .Nodes
                            .Select(review => new PullRequestReviews
                            {
                                Id = review.DatabaseId ?? 0L, 
                                UserId = review.Author.AvatarUrl(100).AvatarUrlToId(),
                                UserName = review.Author.Login,
                                PullRequestId = pr.DatabaseId ?? 0L,
                                State = review.State.ToString(),
                                Body = review.Body,
                                SubmittedAt = review.SubmittedAt,
                            })
                            .ToList(),

                        // NOTE: API limitation: can only get 250 commits per PR. There are no pages that contain the rest
                        Commits = pr.Commits(250, null, null, null)
                            .Nodes
                            .Select(commit => new PullRequestCommits
                            {
                                Sha = commit.Commit.Oid,
                                Message = commit.Commit.Message,
                                Date = commit.Commit.CommittedDate,
                                UserId = commit.Commit.Committer.Select(x => x.User).Select(x => x.DatabaseId ?? 0L).SingleOrDefault(),
                                UserName = commit.Commit.Committer.Name,
                                UserEmail = commit.Commit.Committer.Email,
                                PullRequestId = pr.DatabaseId ?? 0L,
                            })
                            .ToList(),
                        
                        FirstPageFiles = pr.Files(100, null, null, null)
                            .Select(fileConnection => new GQLPagedResponse<PullRequestFile>
                            {
                                HasNextPage = fileConnection.PageInfo.HasNextPage,
                                EndCursor = fileConnection.PageInfo.EndCursor,
                                Items = fileConnection.Nodes
                                    .Select(file => new PullRequestFile
                                    {
                                        Additions = file.Additions,
                                        Deletions = file.Deletions,
                                        ChangeType = file.ChangeType.ToString(),
                                        FilePath = file.Path,
                                    })
                                .ToList(),
                            })
                            .SingleOrDefault(),
                })
                .ToList()
            })
        .Compile();

    public static readonly ICompiledQuery<GQLPagedResponse<PullRequestFile>> GetPRFileChanges = new Query()
        .Repository(Var("repoName"), Var("repoOwner"))
        .PullRequest(number: Var("prNumber"))
        .Files(first: 100, after: Var("after"))
        .Select(connection => new GQLPagedResponse<PullRequestFile>
        {
            HasNextPage = connection.PageInfo.HasNextPage,
            EndCursor = connection.PageInfo.EndCursor,
            Items = connection.Nodes
                .Select(file => new PullRequestFile
                {
                    Additions = file.Additions,
                    Deletions = file.Deletions,
                    ChangeType = file.ChangeType.ToString(),
                    FilePath = file.Path,
                })
                .ToList(),
        })
        .Compile();
}
