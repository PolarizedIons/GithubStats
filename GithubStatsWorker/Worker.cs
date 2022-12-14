using GithubStatsWorker.Entities;
using Octokit.GraphQL;
using Serilog;

namespace GithubStatsWorker;

public class Worker
{
    private readonly Database _db;
    private readonly Connection _gitHubClient;
    private readonly Repository _repo;

    public Worker(Database db, Connection gitHubClient, Repository repo)
    {
        _db = db;
        _gitHubClient = gitHubClient;
        _repo = repo;
    }

    public async Task UpdateRepoStats()
    {
        Log.Information("{RepoName}: Starting work...", _repo.Name);
        await _db.TryAddRepo(_repo);

        await UpdateCommitStats();
        await UpdatePrStats();

        Log.Information("{RepoName}: Done",_repo.Name);
    }

    private async Task UpdateCommitStats()
    {
        var cursor = await _db.GetCursor(EntityType.Commit, _repo.Id.ToString());
        var parameters = new Dictionary<string, object?>()
        {
            { "repoName", _repo.Name },
            { "repoOwner", _repo.Owner },
            { "after", cursor }
        };
        Log.Information("{RepoName}: Fetching commits...", _repo.Name);
        var repoContainsSomething = !string.IsNullOrEmpty(await _gitHubClient.Run(Queries.GetRepoDefaultBranch, parameters));
        if (!repoContainsSomething)
        {
            Log.Warning("{RepoName} repo contains no default branch, skipping adding commits", _repo.Name);
            return;
        }

        var commits = _gitHubClient.CreateQuery(Queries.GetAllCommitsForRepo, parameters);

        await foreach (var commit in commits)
        {
            await _db.TryAddCommit(_repo, commit);
        }

        await _db.SetCursor(EntityType.Commit, _repo.Id.ToString(), commits.LastEndCursor);
    }

    private async Task UpdatePrStats()
    {
        var cursor = await _db.GetCursor(EntityType.PR, _repo.Id.ToString());

        var parameters = new Dictionary<string, object?>()
        {
            { "repoName", _repo.Name },
            { "repoOwner", _repo.Owner},
            { "after", cursor },
        };

        Log.Information("{RepoName}: Fetching PRs...", _repo.Name);
        var prStats = _gitHubClient.CreateQuery(Queries.GetAllPrsForRepo, parameters);

        await foreach (var prStat in prStats)
        {
            if (prStat.CreatorUserId is {} && prStat.CreatorIsHuman && !(await _db.UserExists(prStat.CreatorUserId.Value)))
            {
                var userParams = new Dictionary<string, object>()
                {
                    { "login", prStat.CreatorUserName }
                };
                var user = await _gitHubClient.Run(Queries.GetUser, userParams);
                await _db.TryAddUser(user.Id, user.Username, user.Email);
            }

            if (prStat.MergerUserId is { } && prStat.MergerIsHuman && !(await _db.UserExists(prStat.MergerUserId.Value)))
            {
                var userParams = new Dictionary<string, object>()
                {
                    { "login", prStat.MergerUserName }
                };
                var user = await _gitHubClient.Run(Queries.GetUser, userParams);
                await _db.TryAddUser(user.Id, user.Username, user.Email);
            }

            await _db.UpsertPullRequest(_repo, prStat);
            
            foreach (var request in prStat.RequestedReviewerIds)
            {
                if (request.ReviewerId != default && !(await _db.UserExists(request.ReviewerId)))
                {
                    var userParams = new Dictionary<string, object>()
                    {
                        { "login", request.ReviewerName }
                    };
                    var user = await _gitHubClient.Run(Queries.GetUser, userParams);
                    await _db.TryAddUser(user.Id, user.Username, user.Email);
                }
                
                await _db.TryAddRequestedReview(prStat.Id, request.ReviewerId);
            }

            foreach (var review in prStat.Reviews)
            {
                if (review.UserId is { } && !(await _db.UserExists(review.UserId.Value)))
                {
                    var userParams = new Dictionary<string, object>()
                    {
                        { "login", review.UserName }
                    };
                    var user = await _gitHubClient.Run(Queries.GetUser, userParams);
                    await _db.TryAddUser(user.Id, user.Username, user.Email);
                }

                await _db.UpsertPullRequestReview(_repo, prStat, review);
            }

            foreach (var commit in prStat.Commits)
            {
                await _db.UpsertPullRequestCommit(_repo, prStat, commit);
            }

            foreach (var prFile in prStat.FirstPageFiles.Items)
            {
                // GH's api doesn't have this property :(
                prFile.PullRequestId = prStat.Id;

                await _db.UpsertPullRequestFile(_repo, prStat, prFile);
            }

            if (prStat.FirstPageFiles.HasNextPage)
            {
                var filesParams = new Dictionary<string, object?>()
                {
                    { "repoName", _repo.Name },
                    { "repoOwner", _repo.Owner },
                    { "prNumber", prStat.Number },
                    { "after", prStat.FirstPageFiles.EndCursor }
                };

                var prFiles = _gitHubClient.CreateQuery(Queries.GetPRFileChanges, filesParams);
                await foreach (var prFile in prFiles)
                {
                    // GH's api doesn't have this property :(
                    prFile.PullRequestId = prStat.Id;

                    await _db.UpsertPullRequestFile(_repo, prStat, prFile);
                }
            }
        }

        await _db.SetCursor(EntityType.PR, _repo.Id.ToString(), prStats.LastEndCursor);
    }
}
