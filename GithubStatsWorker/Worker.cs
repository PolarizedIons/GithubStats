using Octokit;
using Serilog;

namespace GithubStatsWorker;

public class Worker
{
    private readonly Database _db;
    private readonly GitHubClient _gitHubClient;
    private readonly Repository _repo;

    public Worker(Database db, GitHubClient gitHubClient, Repository repo)
    {
        _db = db;
        _gitHubClient = gitHubClient;
        _repo = repo;
    }

    private void AvoidApiRateLimit()
    {
        var lastApiInfo = _gitHubClient.GetLastApiInfo();
        if (lastApiInfo == null)
        {
            return;
        }

        var rateLimit = lastApiInfo.RateLimit;
        if (rateLimit.Remaining > 5)
        {
            return;
            
        }

        var sleepTime = rateLimit.Reset - DateTime.UtcNow;
        do {
            Log.Information("Sleeping to avoid rate limit, until {ResetTime} ({Time}s)", rateLimit.Reset, (int)(sleepTime.TotalSeconds));
            Thread.Sleep(TimeSpan.FromSeconds(10));
            sleepTime = rateLimit.Reset - DateTime.UtcNow;
        } while (rateLimit.Reset > DateTime.UtcNow);
    }

    public async Task UpdateRepoStats(object? state)
    {
        Log.Debug("[Thread-{Thread}] {RepoName}: Starting work...", Environment.CurrentManagedThreadId, _repo.FullName);
        await _db.TryAddRepo(_repo);

        await UpdateCommitStats();
        await UpdatePrStats();

        Log.Debug("[Thread-{Thread}] {RepoName}: Done", Environment.CurrentManagedThreadId, _repo.FullName);
    }

    private async Task UpdateCommitStats()
    {
        var lastCommit = await _db.GetLatestCommitForRepository(_repo.Id);
        Log.Debug("[Thread-{Thread}] {RepoName}: Last fetched commit was at {Timestamp}", Environment.CurrentManagedThreadId, _repo.FullName, lastCommit?.Date.ToString( ) ?? "never");

        IReadOnlyList<GitHubCommit> commits = Array.Empty<GitHubCommit>();
        try
        {
            AvoidApiRateLimit();
            commits = await _gitHubClient.Repository.Commit.GetAll(_repo.Id, new CommitRequest()
            {
                Since = lastCommit?.Date,
            });
        }
        catch (Exception)
        {
            // Ignore: Git repository is empty
        }

        Log.Debug("[Thread-{Thread}] {RepoName}: Processing {Count} commits", Environment.CurrentManagedThreadId, _repo.FullName, commits.Count);

        foreach (var commit in commits)
        {
            AvoidApiRateLimit();
            await _db.TryAddUserFromCommit(commit);
            await _db.AddCommit(_repo, commit);
        }
    }

    private async Task UpdatePrStats()
    {
        var lastPr = await _db.GetLastUpdatedPrForRepository(_repo.Id);

        AvoidApiRateLimit();
        var prs = await _gitHubClient.Repository.PullRequest.GetAllForRepository(_repo.Id, new PullRequestRequest
        {
            State = ItemStateFilter.All,
            SortDirection = SortDirection.Ascending,
            SortProperty = PullRequestSort.Created,
        });

        Log.Debug("[Thread-{Thread}] {RepoName} has {Count} total PRs, we know of {PrevPrNumber}", Environment.CurrentManagedThreadId, _repo.FullName, prs.Count, lastPr?.Number ?? 0);
        foreach (var pullRequest in prs.Skip((int)((lastPr?.Number ?? 1L) - 1L)))
        {
            AvoidApiRateLimit();
            if (lastPr is not null && pullRequest.Number <= lastPr.Number)
            {
                continue;
            }

            await _db.UpsertPullRequest(_repo, pullRequest);

            var reviews = await _gitHubClient.Repository.PullRequest.Review.GetAll(_repo.Id, pullRequest.Number);
            foreach (var review in reviews)
            {
                AvoidApiRateLimit();
                await _db.UpsertPullRequestReview(_repo, pullRequest, review);
            }

            var commits = await _gitHubClient.Repository.PullRequest.Commits(_repo.Id, pullRequest.Number);
            foreach (var commit in commits)
            {
                AvoidApiRateLimit();
                await _db.UpsertPullRequestCommit(_repo, pullRequest, commit);
            }

            await _db.MarkPRComplete(pullRequest, _repo);
        }
    }
}
