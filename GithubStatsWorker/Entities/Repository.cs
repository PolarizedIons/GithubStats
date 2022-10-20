namespace GithubStatsWorker.Entities;

public class Repository
{
    public long Id { get; set; }
    public string Owner { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? DefaultBranch { get; set; }
}
