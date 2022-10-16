namespace GithubStatsWorker.Entities;

public class PullRequestLabels
{
    public long PulLRequestId { get; set; }
    public long LabelId { get; set; }
    public string LabelName { get; set; } = null!;
}
