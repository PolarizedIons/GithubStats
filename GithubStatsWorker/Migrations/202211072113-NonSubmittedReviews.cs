using FluentMigrator;

namespace GithubStatsWorker.Migrations;

[TimestampedMigration(2022, 11, 07, 21, 13)]
public class NonSubmittedReviews : Migration
{
    public override void Up()
    {
        Alter.Table("PullRequestReviews")
            .AlterColumn("SubmittedAt").AsDateTime().Nullable();
    }

    public override void Down()
    {
        Alter.Table("PullRequestReviews")
            .AlterColumn("SubmittedAt").AsDateTime().NotNullable();
    }
}
