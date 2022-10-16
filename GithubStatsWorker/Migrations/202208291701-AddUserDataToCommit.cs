using FluentMigrator;

namespace GithubStatsWorker.Migrations;

[TimestampedMigration(2022, 08, 29, 17, 01)]
public class AddUserDataToCommit : Migration
{
    public override void Up()
    {
        Alter.Table("Commits")
            .AlterColumn("UserId").AsInt64().Nullable()
            .AddColumn("CommitUsername").AsString().NotNullable()
            .AddColumn("CommitEmail").AsString().NotNullable();

        Alter.Table("Users")
            .AlterColumn("Email").AsString().Nullable();

        Execute.Sql(@"
            truncate table ""Commits""; 
        ");
    }

    public override void Down()
    {
        Alter.Table("Commits")
            .AlterColumn("UserId").AsInt64().NotNullable().ForeignKey("Users", "Id");

        Delete.Column("CommitUsername").FromTable("Commits");
        Delete.Column("CommitEmail").FromTable("Commits");
    }
}
