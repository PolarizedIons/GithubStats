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

    public async Task UpdateRepoStats(object? state)
    {
        await _db.TryAddRepo(_repo);

        var lastCommit = await _db.GetLatestCommitForRepository(_repo.Id);
        Log.Debug("[Thread-{Thread}] {RepoName} last fetched commit was at {Timestamp}", Environment.CurrentManagedThreadId, _repo.FullName, lastCommit?.Date);

        var commits = await _gitHubClient.Repository.Commit.GetAll(_repo.Id, new CommitRequest()
        {
            Since = lastCommit?.Date,
        });
        Log.Debug("[Thread-{Thread}] {RepoName}: Processing {Count} commits", Environment.CurrentManagedThreadId, _repo.FullName, commits.Count);
        
        foreach (var commit in commits)
        {
            await _db.TryAddUserFromCommit(commit);
            await _db.AddCommit(_repo, commit);
        }
        
        Log.Debug("[Thread-{Thread}] {RepoName}: Done", Environment.CurrentManagedThreadId, _repo.FullName);
    }
}
