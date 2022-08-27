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
        var connection = GetDbConnection();
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
        var connection = GetDbConnection();
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
               UPDATE SET ""Name"" = excluded.""Name"", ""DefaultBranch"" = excluded.""DefaultBranch""
        ", parameters);
    }

    public async Task TryAddUserFromCommit(GitHubCommit user)
    {
        var connection = GetDbConnection();
        var parameters = new DynamicParameters();
        parameters.Add("id", user.Author?.Id ?? user.Committer?.Id ?? 0);
        parameters.Add("username", user.Author?.Login ?? user.Committer?.Login ?? "[unknown]");
        parameters.Add("email", user.Commit.Author.Email);

        await connection.ExecuteAsync(@"
            INSERT INTO ""Users"" (""Id"", ""Username"", ""Email"")
            VALUES (@id, @username, @email)
            ON CONFLICT (""Id"") 
            DO 
               UPDATE SET ""Username"" = excluded.""Username"", ""Email"" = excluded.""Email""
        ", parameters);
    }

    public async Task AddCommit(Repository repo, GitHubCommit commit)
    {
        Log.Debug("[Thread-{Thread}] Adding commit {Sha} in {RepoName} by {Author}", Environment.CurrentManagedThreadId, commit.Sha, repo.Name, commit.Author?.Login ?? commit.Committer?.Login ?? "[unknown]");

        var connection = GetDbConnection();
        var parameters = new DynamicParameters();
        parameters.Add("sha", commit.Sha);
        parameters.Add("message", commit.Commit.Message);
        parameters.Add("date", commit.Commit.Committer.Date);
        parameters.Add("userId", commit.Author?.Id ?? commit.Committer?.Id ?? 0);
        parameters.Add("repoId", repo.Id);

        await connection.ExecuteAsync(@"
            INSERT INTO ""Commits"" (""Sha"", ""Message"", ""Date"", ""UserId"", ""RepoId"")
            VALUES (@sha, @message, @date, @UserId, @repoId)
            ON CONFLICT (""Sha"") DO NOTHING
        ", parameters);
    }
}
