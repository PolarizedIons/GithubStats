using Dapper;
using FluentMigrator.Runner;
using FluentMigrator.Runner.Announcers;
using FluentMigrator.Runner.Initialization;
using FluentMigrator.Runner.Processors;
using Microsoft.Extensions.Configuration;
using Octokit;
using Npgsql;
using Serilog;
using Repository = Octokit.Repository;


namespace GithubStatsWorker;

public class Database
{
    private readonly IConfiguration _config;

    public Database(IConfiguration config)
    {
        _config = config;
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
        await using var connection = GetDbConnection();
        var parameters = new DynamicParameters();
        parameters.Add("id", id);

        return await connection.QuerySingleOrDefaultAsync<Entities.Commit>(@"
            select c.* 
            from ""Commits"" c
            join ""Repositories"" r on c.""RepoId"" = r.""Id""
            where r.""Id"" = @id
            order by ""Date"" desc
            limit 1", parameters);
    }

    public async Task TryAddRepo(Repository repo)
    {
        await using var connection = GetDbConnection();
        var parameters = new DynamicParameters();
        parameters.Add("id", repo.Id);
        parameters.Add("owner", repo.Owner.Login);
        parameters.Add("name", repo.Name);
        parameters.Add("defaultBranch", repo.DefaultBranch);

        await connection.ExecuteAsync(@"
            INSERT INTO ""Repositories"" (""Id"", ""Owner"", ""Name"", ""DefaultBranch"")
            VALUES (@id, @owner, @name, @defaultBranch)
            ON CONFLICT (""Id"") 
            DO 
               UPDATE SET ""Name"" = excluded.""Name"",
                          ""DefaultBranch"" = excluded.""DefaultBranch""
        ", parameters);
    }

    public async Task TryAddUserFromCommit(GitHubCommit commit)
    {
        var userId = commit.Author?.Id ?? commit.Committer?.Id;
        if (userId is null)
        {
            return;
        }

        await TryAddUser(
            userId.Value,
            commit.Author?.Login ?? commit.Committer?.Login ?? commit.Commit.Author?.Email ?? commit.Commit.Committer?.Email ?? "[unknown]",
            commit.Commit.Author?.Email ?? commit.Commit.Committer?.Email ?? "[unknown]"
        );
    }

    private async Task TryAddUser(long userId, string username, string email)
    {
        await using var connection = GetDbConnection();
        var parameters = new DynamicParameters();
        parameters.Add("id", userId);
        parameters.Add("username", username);
        parameters.Add("email", email);

        await connection.ExecuteAsync(@"
            INSERT INTO ""Users"" as u (""Id"", ""Username"", ""Email"")
            VALUES (@id, @username, @email)
            ON CONFLICT (""Id"")
            DO 
               UPDATE SET ""Username"" = coalesce(excluded.""Username"", u.""Username""), ""Email"" = coalesce(excluded.""Email"", u.""Email"")
        ", parameters);
    }

    public async Task AddCommit(Repository repo, GitHubCommit commit)
    {
        await AddCommit(
            repo,
            commit.Sha,
            commit.Commit.Message,
            commit.Commit.Author?.Date ?? commit.Commit.Committer!.Date,
            commit.Author?.Id ?? commit.Committer?.Id,
            commit.Commit.Author?.Name ?? commit.Commit.Committer.Name,
            commit.Commit.Author?.Email ?? commit.Commit.Committer.Email
        );
    }

    private async Task AddCommit(Repository repo, PullRequestCommit commit)
    {
        await AddCommit(
            repo,
            commit.Sha,
            commit.Commit.Message,
            commit.Commit.Author?.Date ?? commit.Commit.Committer!.Date,
            commit.Author?.Id ?? commit.Committer?.Id,
            commit.Commit.Author?.Name ?? commit.Commit.Committer.Name,
            commit.Commit.Author?.Email ?? commit.Commit.Committer.Email
        );
    }

    private async Task AddCommit(Repository repo, string sha, string message, DateTimeOffset date, long? userId, string commitUsername, string commitEmail)
    {
        Log.Debug("[Thread-{Thread}] Adding commit {Sha} in {RepoName} by {Author}", Environment.CurrentManagedThreadId, sha, repo.Name, commitUsername);

        await using var connection = GetDbConnection();
        var parameters = new DynamicParameters();
        parameters.Add("sha", sha);
        parameters.Add("message", message);
        parameters.Add("date", date);
        parameters.Add("userId", userId);
        parameters.Add("repoId", repo.Id);
        parameters.Add("commitUsername", commitUsername);
        parameters.Add("commitEmail", commitEmail);

        await connection.ExecuteAsync(@"
            INSERT INTO ""Commits"" (""Sha"", ""Message"", ""Date"", ""UserId"", ""RepoId"", ""CommitUsername"", ""CommitEmail"")
            VALUES (@sha, @message, @date, @UserId, @repoId, @commitUsername, @commitEmail)
            ON CONFLICT (""Sha"") DO NOTHING
        ", parameters);
    }

    public async Task<Entities.PullRequest> GetLastUpdatedPrForRepository(long id)
    {
        await using var connection = GetDbConnection();
        var parameters = new DynamicParameters();
        parameters.Add("id", id);

        return await connection.QuerySingleOrDefaultAsync<Entities.PullRequest>(@"
            select pr.* 
            from ""Repositories"" repo
            join ""PullRequests"" pr on pr.""Id"" = repo.""LastPR""
            where repo.""Id"" = @id",
            parameters);
    }

    public async Task UpsertPullRequest(Repository repo, PullRequest pullRequest)
    {
        Log.Debug("[Thread-{Thread}] {RepoName}: Processing PR {PRNumber}: {PRTitle}", Environment.CurrentManagedThreadId, repo.FullName, pullRequest.Number, pullRequest.Title);

        await TryAddUser(pullRequest.User.Id, pullRequest.User.Name ?? pullRequest.User.Login, pullRequest.User.Email);
        if (pullRequest.MergedBy is not null)
        {
            await TryAddUser(pullRequest.MergedBy.Id, pullRequest.MergedBy.Name, pullRequest.MergedBy.Email);
        }

        {
            await using var connection = GetDbConnection();
            var parameters = new DynamicParameters();
            parameters.Add("id", pullRequest.Id);
            parameters.Add("number", pullRequest.Number);
            parameters.Add("state", pullRequest.State.StringValue);
            parameters.Add("title", pullRequest.Title);
            parameters.Add("body", pullRequest.Body);
            parameters.Add("createdAt", pullRequest.CreatedAt);
            parameters.Add("updatedAt", pullRequest.UpdatedAt);
            parameters.Add("closedAt", pullRequest.ClosedAt);
            parameters.Add("mergedAt", pullRequest.MergedAt);
            parameters.Add("commentsCount", pullRequest.Comments);
            parameters.Add("commitsCount", pullRequest.Commits);
            parameters.Add("additionsCount", pullRequest.Additions);
            parameters.Add("deletionsCount", pullRequest.Deletions);
            parameters.Add("changedFilesCount", pullRequest.ChangedFiles);
            parameters.Add("creatorUserId", pullRequest.User.Id);
            parameters.Add("mergerUserId", pullRequest.MergedBy?.Id);
            parameters.Add("repoId", repo.Id);

            await connection.ExecuteAsync(@"
            insert into ""PullRequests""(""Id"", ""Number"", ""State"", ""Title"", ""Body"", ""CreatedAt"", ""UpdatedAt"", ""ClosedAt"", ""MergedAt"", ""CommentsCount"", ""CommitsCount"", ""AdditionsCount"", ""DeletionsCount"", ""ChangedFilesCount"", ""CreatorUserId"", ""MergerUserId"", ""RepoId"", ""ScanCompleted"")
            values (@id, @number, @state, @title, @body, @createdAt, @updatedAt, @closedAt, @mergedAt, @commentsCount, @commitsCount, @additionsCount, @deletionsCount, @changedFilesCount, @creatorUserId, @mergerUserId, @repoId, false)
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
                    ""MergerUserId"" = excluded.""MergerUserId"",
                    ""ScanCompleted"" = excluded.""ScanCompleted""
            ", parameters);
        }

        foreach (var requestedReviewer in pullRequest.RequestedReviewers)
        {
            if (requestedReviewer is null)
            {
                continue;
            }

            await TryAddUser(requestedReviewer.Id, requestedReviewer.Name ?? "[unknown]", requestedReviewer.Email ?? "[unknown]");
            await TryAddRequestedReview(pullRequest.Id, requestedReviewer.Id);
        }

        foreach (var label in pullRequest.Labels)
        {
            await TryAddPRLabel(pullRequest, label);
        }
    }

    private async Task TryAddPRLabel(PullRequest pullRequest, Label label)
    {
        await using var connection = GetDbConnection();
        var parameters = new DynamicParameters();
        parameters.Add("prId", pullRequest.Id);
        parameters.Add("labelId", label.Id);
        parameters.Add("labelName", label.Name);

        await connection.ExecuteAsync(@"
            insert into ""PullRequestLabels""(""PullRequestId"", ""LabelId"", ""LabelName"")
            values (@prId, @labelId, @labelName)
            on conflict (""PullRequestId"", ""LabelId"") do nothing;
        ", parameters);
    }

    private async Task TryAddRequestedReview(long pullRequestId, int userId)
    {
        await using var connection = GetDbConnection();
        var parameters = new DynamicParameters();
        parameters.Add("prId", pullRequestId);
        parameters.Add("userId", userId);

        await connection.ExecuteAsync(@"
            insert into ""PullRequestRequestedReviewers""(""PullRequestId"", ""ReviewerId"")
            values (@prId, @userId)
            on conflict (""PullRequestId"", ""ReviewerId"") do nothing;
        ", parameters);
    }

    public async Task UpsertPullRequestReview(Repository repository, PullRequest pullRequest, PullRequestReview review)
    {
        Log.Debug("[Thread-{Thread}] {RepoName}: Processing PR {PRNumber} review", Environment.CurrentManagedThreadId, repository.FullName, pullRequest.Number);

        await TryAddUser(review.User.Id, review.User.Name ?? review.User.Login, review.User.Email);
        await TryAddRequestedReview(repository.Id, review.User.Id);

        await using var connection = GetDbConnection();
        var parameters = new DynamicParameters();
        parameters.Add("id", review.Id);
        parameters.Add("userId", review.User.Id);
        parameters.Add("pullRequestId", pullRequest.Id);
        parameters.Add("state", review.State.StringValue);
        parameters.Add("body", review.Body);
        parameters.Add("submittedAt", review.SubmittedAt);

        await connection.ExecuteAsync(@"
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

    public async Task UpsertPullRequestCommit(Repository repository, PullRequest pullRequest, PullRequestCommit commit)
    {
        Log.Debug("[Thread-{Thread}] {RepoName}: Processing PR {PRNumber} commit {CommitSha}", Environment.CurrentManagedThreadId, repository.FullName, pullRequest.Number, commit.Sha);

        var userId = commit.Author?.Id ?? commit.Committer?.Id;
        if (userId is not null)
        {
            await TryAddUser(
                userId.Value,
                commit.Author?.Login ?? commit.Author?.Name ?? commit.Committer?.Login ?? commit.Committer?.Name ?? "[unknown]",
                commit.Author?.Email ?? commit.Committer?.Email ?? "[unknown]"
            );
        }

        await AddCommit(repository, commit);

        await using var connection = GetDbConnection();
        var parameters = new DynamicParameters();
        parameters.Add("pullRequestId", pullRequest.Id);
        parameters.Add("userId", commit.Author?.Id ?? commit.Committer?.Id);
        parameters.Add("sha", commit.Sha);

        await connection.ExecuteAsync(@"
            insert into ""PullRequestCommits""(""PullRequestId"", ""UserId"", ""Sha"")
            values (@pullRequestId, @userId, @sha);
        ", parameters);
    }

    public async Task MarkPRComplete(PullRequest pullRequest, Repository repository)
    {
        await using var connection = GetDbConnection();
        var parameters = new DynamicParameters();
        parameters.Add("id", pullRequest.Id);
        parameters.Add("repoId", repository.Id);

        await connection.ExecuteAsync(@"
            update ""PullRequests""
            set ""ScanCompleted"" = true
            where ""Id"" = @id;
            
            update ""Repositories""
            set ""LastPR"" = @id
            where ""Id"" = @repoId;
        ", parameters);
    }
}
