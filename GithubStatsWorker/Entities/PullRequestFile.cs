namespace GithubStatsWorker.Entities;

public class PullRequestFile
{
    public long PullRequestId { get; set; }

    public string FilePath { get; set; } = null!;
    public string ChangeType { get; set; } = null!;
    public long Additions { get; set; }
    public long Deletions { get; set; }
}
