using FluentMigrator;

namespace GithubStatsWorker.Migrations;

[TimestampedMigration(2022, 11, 07, 20, 31)]
public class AddPRFiles : Migration
{
    public override void Up()
    {
        Create.Table("PullRequestFiles")
            .WithColumn("PullRequestId").AsInt64().PrimaryKey().ForeignKey("PullRequests", "Id")
            .WithColumn("FilePath").AsString().PrimaryKey()
            .WithColumn("Additions").AsInt64().NotNullable()
            .WithColumn("Deletions").AsInt64().NotNullable()
            .WithColumn("ChangeType").AsString().NotNullable();
    }

    public override void Down()
    {
        Delete.Table("PullRequestFiles");
    }
}
