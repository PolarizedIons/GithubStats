using Octokit.GraphQL;

namespace GithubStatsWorker.Entities;

public class Commit
{
    public string Sha { get; set; } = null!;
    public string Message { get; set; } = null!;   
    public long UserId { get; set; }
    public long RepoId { get; set; }
    public DateTimeOffset Date { get; set; }

    internal string UserName { get; set; } = null!;
    internal string UserEmail { get; set; } = null!;
}
