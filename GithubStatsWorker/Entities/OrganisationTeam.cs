namespace GithubStatsWorker.Entities;

public class OrganisationTeam
{
    public long Id { get; set; }
    public string Slug { get; set; } = null!;
    public string Name { get; set; } = null!;

    public long? ParentTeamId { get; set; }
}
