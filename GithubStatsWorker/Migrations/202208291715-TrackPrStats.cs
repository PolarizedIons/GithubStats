using FluentMigrator;

namespace GithubStatsWorker.Migrations;

[TimestampedMigration(2022, 08, 29, 17, 15)]
public class TrackPrStats : Migration
{
    public override void Up()
    {
        Create.Table("PullRequests")
            .WithColumn("Id").AsInt64().NotNullable().PrimaryKey()
            .WithColumn("Number").AsInt64().NotNullable()
            .WithColumn("State").AsString().NotNullable()
            .WithColumn("Title").AsString().NotNullable()
            .WithColumn("Body").AsString().Nullable()
            .WithColumn("CreatedAt").AsDateTimeOffset().NotNullable()
            .WithColumn("UpdatedAt").AsDateTimeOffset().NotNullable()
            .WithColumn("ClosedAt").AsDateTimeOffset().Nullable()
            .WithColumn("MergedAt").AsDateTimeOffset().Nullable()
            .WithColumn("CommentsCount").AsInt64().NotNullable()
            .WithColumn("CommitsCount").AsInt64().NotNullable()
            .WithColumn("AdditionsCount").AsInt64().NotNullable()
            .WithColumn("DeletionsCount").AsInt64().NotNullable()
            .WithColumn("ChangedFilesCount").AsInt64().NotNullable()
            .WithColumn("CreatorUserId").AsInt64().NotNullable().ForeignKey("Users", "Id")
            .WithColumn("MergerUserId").AsInt64().Nullable().ForeignKey("Users", "Id")
            .WithColumn("RepoId").AsInt64().NotNullable().ForeignKey("Repositories", "Id")
            .WithColumn("ScanCompleted").AsBoolean().NotNullable();

        Alter.Table("Repositories")
            .AddColumn("LastPR").AsInt64().Nullable().ForeignKey("PullRequests", "Id");

        Create.Table("PullRequestLabels")
            .WithColumn("PullRequestId").AsInt64().NotNullable().PrimaryKey().ForeignKey("PullRequests", "Id")
            .WithColumn("LabelId").AsInt64().NotNullable().PrimaryKey()
            .WithColumn("LabelName").AsString().NotNullable();

        Create.Table("PullRequestRequestedReviewers")
            .WithColumn("PullRequestId").AsInt64().NotNullable().PrimaryKey().ForeignKey("PullRequests", "Id")
            .WithColumn("ReviewerId").AsInt64().NotNullable().PrimaryKey().ForeignKey("Users", "Id");

        Create.Table("PullRequestReviews")
            .WithColumn("Id").AsInt64().NotNullable().PrimaryKey()
            .WithColumn("UserId").AsInt64().NotNullable().ForeignKey("Users", "Id")
            .WithColumn("PullRequestId").AsInt64().NotNullable().ForeignKey("PullRequests", "Id")
            .WithColumn("State").AsString().NotNullable()
            .WithColumn("Body").AsString().Nullable()
            .WithColumn("SubmittedAt").AsDateTimeOffset().NotNullable()
            .WithColumn("IsLatestReview").AsBoolean().NotNullable();

        Create.Table("PullRequestCommits")
            .WithColumn("PullRequestId").AsInt64().NotNullable().ForeignKey("PullRequests", "Id")
            .WithColumn("UserId").AsInt64().Nullable().ForeignKey("Users", "Id")
            .WithColumn("Sha").AsString().NotNullable().ForeignKey("Commits", "Sha");
    }

    public override void Down()
    {
        Delete.Table("PullRequests");
        Delete.Table("PullRequestLabels");
        Delete.Table("PullRequestRequestedReviewers");
        Delete.Table("PullRequestReviews");
        Delete.Table("PullRequestCommits");
        Delete.Column("LastPR").FromTable("Repositories");
    }
}
