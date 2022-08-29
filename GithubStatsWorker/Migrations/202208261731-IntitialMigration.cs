using FluentMigrator;
using Migration = FluentMigrator.Migration;

namespace GithubStatsWorker.Migrations;

[TimestampedMigration(2022, 08, 26, 17, 31)]
public class InitialMigration : Migration
{
    public override void Up()
    {
        Create.Table("Repositories")
            .WithColumn("Id").AsInt64().NotNullable().PrimaryKey()
            .WithColumn("Owner").AsString().NotNullable()
            .WithColumn("Name").AsString().NotNullable()
            .WithColumn("DefaultBranch").AsString().NotNullable();

        Create.Table("Users")
            .WithColumn("Id").AsInt64().NotNullable().PrimaryKey()
            .WithColumn("Username").AsString().NotNullable()
            .WithColumn("Email").AsString().NotNullable();

        Create.Table("Commits")
            .WithColumn("Sha").AsString().NotNullable().PrimaryKey()
            .WithColumn("Message").AsString().NotNullable()
            .WithColumn("Date").AsDateTimeOffset()
            .WithColumn("UserId").AsInt64().NotNullable().ForeignKey("Users", "Id")
            .WithColumn("RepoId").AsInt64().ForeignKey("Repositories", "Id");
    }

    public override void Down()
    {
        Delete.Table("Repository");
        Delete.Table("Users");
        Delete.Table("Commits");
    }
}
