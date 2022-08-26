namespace GithubStatsWorker.Entities;

public class User
{
    public long Id { get; set; }
    public string Username { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Email { get; set; } = null!;
}