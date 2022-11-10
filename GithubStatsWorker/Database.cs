using System.Data;
using Dapper;
using FluentMigrator.Runner;
using FluentMigrator.Runner.Announcers;
using FluentMigrator.Runner.Initialization;
using FluentMigrator.Runner.Processors;
using GithubStatsWorker.Entities;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Octokit.GraphQL.Model;
using Serilog;
using PullRequest = GithubStatsWorker.Entities.PullRequest;
using Repository = GithubStatsWorker.Entities.Repository;


namespace GithubStatsWorker;

public class Database : IDisposable
{
    private readonly IConfiguration _config;
    private static IDbConnection? _connection;

    public Database(IConfiguration config)
    {
        _config = config;
        _connection ??= GetDbConnection();
    }

    
    public NpgsqlConnection GetDbConnection() => new(_config.GetConnectionString("db"));

    public void Migrate()
    {
        var announcer = new ConsoleAnnouncer();
        var ctx = new RunnerContext(announcer) {
            Connection = _config.GetConnectionString("db"),
            Namespace = "GithubStatsWorker.Migrations",
            NestedNamespaces = true,
        };

        var pg = new FluentMigrator.Runner.Processors.Postgres.PostgresProcessorFactory();
        using var processor = pg.Create(_config.GetConnectionString("db"), announcer, new ProcessorOptions
        {
            Timeout = TimeSpan.FromMinutes(1), PreviewOnly = false,
        });
        var runner = new MigrationRunner(typeof(Program).Assembly, ctx, processor);
        runner.MigrateUp(true);
    }

    public async Task<Entities.Commit?> GetLatestCommitForRepository(long id)
    {
        var parameters = new DynamicParameters();
        parameters.Add("id", id);

        return await _connection.QuerySingleOrDefaultAsync<Entities.Commit>(@"
            select c.* 
            from ""Commits"" c
            join ""Repositories"" r on c.""RepoId"" = r.""Id""
            where r.""Id"" = @id
            order by ""Date"" desc
            limit 1", parameters);
    }

    public async Task TryAddRepo(Entities.Repository repo)
    {
        var parameters = new DynamicParameters();
        parameters.Add("id", repo.Id);
        parameters.Add("owner", repo.Owner);
        parameters.Add("name", repo.Name);
        parameters.Add("defaultBranch", repo.DefaultBranch);

        await _connection.ExecuteAsync(@"
            INSERT INTO ""Repositories"" (""Id"", ""Owner"", ""Name"", ""DefaultBranch"")
            VALUES (@id, @owner, @name, @defaultBranch)
            ON CONFLICT (""Id"") 
            DO 
               UPDATE SET ""Owner"" = excluded.""Owner"",
                          ""Name"" = excluded.""Name"",
                          ""DefaultBranch"" = excluded.""DefaultBranch""
        ", parameters);
    }

    public async Task TryAddUser(long userId, string username, string email)
    {
        if (userId == default)
        {
            return;
        }

        var parameters = new DynamicParameters();
        parameters.Add("id", userId);
        parameters.Add("username", username);
        parameters.Add("email", email);

        await _connection.ExecuteAsync(@"
            INSERT INTO ""Users"" as u (""Id"", ""Username"", ""Email"")
            VALUES (@id, @username, @email)
            ON CONFLICT (""Id"")
            DO 
               UPDATE SET ""Username"" = coalesce(excluded.""Username"", u.""Username"", @username), ""Email"" = coalesce(excluded.""Email"", u.""Email"", @email)
        ", parameters);
    }

    public async Task TryAddCommit(Entities.Repository repo, Entities.Commit commit)
    {
        await TryAddUser(commit.UserId, commit.UserName, commit.UserEmail);
        await TryAddCommit(
            repo,
            commit.Sha,
            commit.Message,
            commit.Date,
            commit.UserId,
            commit.UserName,
            commit.UserEmail
        );
    }

    private async Task TryAddCommit(Entities.Repository repo, Entities.PullRequestCommits commit)
    {
        await TryAddUser(commit.UserId, commit.UserName, commit.UserEmail);
        await TryAddCommit(
            repo,
            commit.Sha,
            commit.Message,
            commit.Date,
            commit.UserId,
            commit.UserName,
            commit.UserEmail
        );
    }

    private async Task TryAddCommit(Entities.Repository repo, string sha, string message, DateTimeOffset date, long userId, string commitUserName, string commitEmail)
    {
        Log.Debug("{RepoName}: Adding commit {Sha} by {Author}",sha, repo.Name, commitUserName);

        var parameters = new DynamicParameters();
        parameters.Add("sha", sha);
        parameters.Add("message", message);
        parameters.Add("date", date);
        parameters.Add("userId", userId == default ? null : userId);
        parameters.Add("repoId", repo.Id);
        parameters.Add("commitUsername", commitUserName);
        parameters.Add("commitEmail", commitEmail);

        await _connection.ExecuteAsync(@"
            INSERT INTO ""Commits"" (""Sha"", ""Message"", ""Date"", ""UserId"", ""RepoId"", ""CommitUsername"", ""CommitEmail"")
            VALUES (@sha, @message, @date, @UserId, @repoId, @commitUsername, @commitEmail)
            ON CONFLICT (""Sha"") DO NOTHING
        ", parameters);
    }

    public async Task<Entities.PullRequest> GetLastUpdatedPrForRepository(long id)
    {
        var parameters = new DynamicParameters();
        parameters.Add("id", id);

        return await _connection.QuerySingleOrDefaultAsync<Entities.PullRequest>(@"
            select pr.* 
            from ""Repositories"" repo
            join ""PullRequests"" pr on pr.""Id"" = repo.""LastPR""
            where repo.""Id"" = @id",
            parameters);
    }

    public async Task UpsertPullRequest(Entities.Repository repo, Entities.PullRequest pullRequest)
    {
        Log.Debug("{RepoName}: Processing PR {PRNumber}: {PRTitle}",repo.Name, pullRequest.Number, pullRequest.Title);

        var parameters = new DynamicParameters();
        parameters.Add("id", pullRequest.Id);
        parameters.Add("number", pullRequest.Number);
        parameters.Add("state", pullRequest.State);
        parameters.Add("title", pullRequest.Title);
        parameters.Add("body", pullRequest.Body);
        parameters.Add("createdAt", pullRequest.CreatedAt);
        parameters.Add("updatedAt", pullRequest.UpdatedAt);
        parameters.Add("closedAt", pullRequest.ClosedAt);
        parameters.Add("mergedAt", pullRequest.MergedAt);
        parameters.Add("commentsCount", pullRequest.CommentsCount);
        parameters.Add("commitsCount", pullRequest.CommitsCount);
        parameters.Add("additionsCount", pullRequest.AdditionsCount);
        parameters.Add("deletionsCount", pullRequest.DeletionsCount);
        parameters.Add("changedFilesCount", pullRequest.ChangedFilesCount);
        parameters.Add("creatorUserId", pullRequest.CreatorUserId == 0L ? null : pullRequest.CreatorUserId);
        parameters.Add("creatorUserName", pullRequest.CreatorUserName);
        parameters.Add("creatorIsHuman", pullRequest.CreatorIsHuman);
        parameters.Add("mergerUserId", pullRequest.MergerUserId == 0L ? null : pullRequest.MergerUserId);
        parameters.Add("mergerUserName", pullRequest.MergerUserName);
        parameters.Add("mergerIsHuman", pullRequest.MergerIsHuman);
        parameters.Add("repoId", repo.Id);
        parameters.Add("targetRef", pullRequest.TargetRef);
        parameters.Add("fromRef", pullRequest.FromRef);

        await _connection.ExecuteAsync(@"
        insert into ""PullRequests""(""Id"", ""Number"", ""State"", ""Title"", ""Body"", ""CreatedAt"", ""UpdatedAt"", ""ClosedAt"", ""MergedAt"", ""CommentsCount"", ""CommitsCount"", ""AdditionsCount"", ""DeletionsCount"", ""ChangedFilesCount"", ""CreatorUserId"", ""CreatorUserName"", ""CreatorIsHuman"", ""MergerUserId"", ""MergerUserName"", ""MergerIsHuman"", ""RepoId"", ""TargetRef"", ""FromRef"")
        values (@id, @number, @state, @title, @body, @createdAt, @updatedAt, @closedAt, @mergedAt, @commentsCount, @commitsCount, @additionsCount, @deletionsCount, @changedFilesCount, @creatorUserId, @creatorUserName, @creatorIsHuman, @mergerUserId, @mergerUserName, @mergerIsHuman, @repoId, @targetRef, @fromRef)
        on conflict (""Id"") do
            update set
                ""State"" = excluded.""State"",
                ""Title"" = excluded.""Title"",
                ""Body"" = excluded.""Body"",
                ""UpdatedAt"" = excluded.""UpdatedAt"",
                ""ClosedAt"" = excluded.""ClosedAt"",
                ""MergedAt"" = excluded.""MergedAt"",
                ""CommentsCount"" = excluded.""CommentsCount"",
                ""CommitsCount"" = excluded.""CommitsCount"",
                ""AdditionsCount"" = excluded.""AdditionsCount"",
                ""DeletionsCount"" = excluded.""DeletionsCount"",
                ""ChangedFilesCount"" = excluded.""ChangedFilesCount"",
                ""MergerUserId"" = excluded.""MergerUserId""
        ", parameters);

        var parameters2 = new DynamicParameters();
        parameters2.Add("prId", pullRequest.Id);
        parameters2.Add("repoId", pullRequest.RepoId);

        await _connection.ExecuteAsync(@"
            update ""Repositories""
            set ""LastPR"" = @prId
            where ""Id"" = @repoId;
        ", parameters2);
    }

    private async Task TryAddPRLabel(Entities.PullRequest pullRequest, Label label)
    {
        var parameters = new DynamicParameters();
        parameters.Add("prId", pullRequest.Id);
        parameters.Add("labelId", label.Id);
        parameters.Add("labelName", label.Name);

        await _connection.ExecuteAsync(@"
            insert into ""PullRequestLabels""(""PullRequestId"", ""LabelId"", ""LabelName"")
            values (@prId, @labelId, @labelName)
            on conflict (""PullRequestId"", ""LabelId"") do nothing;
        ", parameters);
    }

    public async Task TryAddRequestedReview(long pullRequestId, long? userId)
    {
        if (userId is not null or 0)
        {
            return;
        }

        var parameters = new DynamicParameters();
        parameters.Add("prId", pullRequestId);
        parameters.Add("userId", userId);

        await _connection.ExecuteAsync(@"
            insert into ""PullRequestRequestedReviewers""(""PullRequestId"", ""ReviewerId"")
            values (@prId, @userId)
            on conflict (""PullRequestId"", ""ReviewerId"") do nothing;
        ", parameters);
    }

    public async Task UpsertPullRequestReview(Entities.Repository repository, Entities.PullRequest pullRequest, PullRequestReviews review)
    {
        Log.Debug("{RepoName}: Processing PR {PRNumber} review {ReviewId}",repository.Name, pullRequest.Number, review.Id);

        await TryAddRequestedReview(pullRequest.Id, review.UserId);
        
        var parameters = new DynamicParameters();
        parameters.Add("id", review.Id);
        parameters.Add("userId", review.UserId);
        parameters.Add("pullRequestId", pullRequest.Id);
        parameters.Add("state", review.State);
        parameters.Add("body", review.Body);
        parameters.Add("submittedAt", review.SubmittedAt);

        await _connection.ExecuteAsync(@"
            update ""PullRequestReviews"" 
            set ""IsLatestReview"" = false 
            where ""PullRequestId"" = @pullRequestId
                and ""UserId"" = @userId;

            insert into ""PullRequestReviews""(""Id"", ""UserId"", ""PullRequestId"", ""State"", ""Body"", ""SubmittedAt"", ""IsLatestReview"")
            values (@id, @userId, @pullRequestId, @state, @body, @submittedAt, true)
            on conflict (""Id"") do
                update set
                           ""State"" = excluded.""State"",
                           ""Body"" = excluded.""Body"",
                           ""SubmittedAt"" = excluded.""SubmittedAt"",
                           ""IsLatestReview"" = true;
        ", parameters);
    }

    public async Task UpsertPullRequestCommit(Entities.Repository repository, Entities.PullRequest pullRequest, PullRequestCommits commit)
    {
        Log.Debug("{RepoName}: Processing PR {PRNumber} commit {CommitSha}",repository.Name, pullRequest.Number, commit.Sha);

        await TryAddCommit(repository, commit);

        var parameters = new DynamicParameters();
        parameters.Add("pullRequestId", pullRequest.Id);
        parameters.Add("userId", commit.UserId == default ? null : commit.UserId);
        parameters.Add("sha", commit.Sha);

        await _connection.ExecuteAsync(@"
            insert into ""PullRequestCommits""(""PullRequestId"", ""UserId"", ""Sha"")
            values (@pullRequestId, @userId, @sha);
        ", parameters);
    }

    public async Task UpsertPullRequestFile(Repository repository, PullRequest pr, PullRequestFile file)
    {
        Log.Debug("{RepoName}: Processing PR {PRNumber} file {Path}",repository.Name, pr.Number, file.FilePath);

        var parameters = new DynamicParameters();
        parameters.Add("pullRequestId", pr.Id);
        parameters.Add("filePath", file.FilePath);
        parameters.Add("changeType", file.ChangeType);
        parameters.Add("additions", file.Additions);
        parameters.Add("deletions", file.Deletions);

        await _connection.ExecuteAsync(@"
            insert into ""PullRequestFiles""(""PullRequestId"", ""FilePath"", ""ChangeType"", ""Additions"", ""Deletions"")
            values (@pullRequestId, @filePath, @changeType, @additions, @deletions)
            on conflict do nothing;
        ", parameters);
    }

    public async Task<bool> UserExists(long userId)
    {
        var parameters = new DynamicParameters();
        parameters.Add("id", userId);

        return await _connection.QuerySingleAsync<bool>(@"
            select count(*) != 0 from ""Users""
            where ""Id"" = @id
        ", parameters);
    }

    public async Task<string?> GetCursor(EntityType type, string id)
    {
        var parameters = new DynamicParameters();
        parameters.Add("type", type.ToString());
        parameters.Add("id", id);

        return await _connection.QuerySingleOrDefaultAsync<string>(@"
            select ""Cursor""
            from ""Cursors""
            where ""Type"" = @type and ""Id"" = @id
        ", parameters);
    }

    public async Task SetCursor(EntityType type, string id, string? cursor)
    {
        var parameters = new DynamicParameters();
        parameters.Add("type", type.ToString());
        parameters.Add("id", id);
        parameters.Add("cursor", cursor);

        await _connection.ExecuteAsync(@"
            insert into ""Cursors""(""Type"", ""Id"" , ""Cursor"")
            values (@type, @id, @cursor)
            on conflict (""Type"", ""Id"") do
                update set ""Cursor"" = excluded.""Cursor""
        ", parameters);
    }

    public void Dispose()
    {
        _connection?.Dispose();
    }
}
