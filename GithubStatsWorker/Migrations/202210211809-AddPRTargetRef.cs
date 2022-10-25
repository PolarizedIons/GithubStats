using FluentMigrator;

namespace GithubStatsWorker.Migrations;

[TimestampedMigration(2022, 10, 21, 18, 09)]
public class AddPRTargetRef : Migration
{
    public override void Up()
    {
        Delete.FromTable("PullRequestCommits").AllRows();
        Delete.FromTable("PullRequestLabels").AllRows();
        Delete.FromTable("PullRequestRequestedReviewers").AllRows();
        Delete.FromTable("PullRequestReviews").AllRows();
        Update.Table("Repositories")
            .Set(new { LastPR = DBNull.Value }).AllRows();
        Update.Table("Cursors")
            .Set(new { Cursor = DBNull.Value})
            .Where(new { Type = "PR"});
        Delete.FromTable("PullRequests").AllRows();
        Alter.Table("PullRequests")
            .AddColumn("FromRef").AsString().NotNullable()
            .AddColumn("TargetRef").AsString().NotNullable();
    }

    public override void Down()
    {
        Delete.Column("TargetRef")
            .FromTable("PullRequests");
    }
}
