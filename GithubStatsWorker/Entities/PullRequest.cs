using Octokit;

namespace GithubStatsWorker.Entities;

public class PullRequest
{
    public long Id { get; set; }
    public long Number { get; set; }
    public StringEnum<ItemState> State { get; set; }
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


    public long CreatorUserId { get; set; }
    public long? MergerUserId { get; set; }
    public ulong RepoId { get; set; }
}
