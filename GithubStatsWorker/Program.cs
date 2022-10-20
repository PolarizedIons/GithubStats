using GithubStatsWorker;
using Microsoft.Extensions.Configuration;
using Octokit.GraphQL;
using Octokit.GraphQL.Core;
using Octokit.GraphQL.Model;
using Serilog;
using static Octokit.GraphQL.Variable;
using Repository = GithubStatsWorker.Entities.Repository;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console()
    .CreateLogger();

var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", true)
    .AddJsonFile("appsettings.Development.json", true)
    .AddEnvironmentVariables()
    .Build();

var db = new Database(config);
Log.Debug("Migrating db");
db.Migrate();
Log.Debug("Done migrating");

var productInfo = new ProductHeaderValue("Polarizedions-repo-stats-puller", "v0.1");
var ghClient = new Connection(productInfo, config["Github:PAT"]);

Log.Information("Fetching all repos for {Target}", config["Github:Target"]);
var reposQuery = new Query()
    .Organization(Var("ownerId"))
    .Repositories()
    .AllPages()
    .Select(repo => new Repository
    {
        Id = repo.DatabaseId ?? 0L,
        Name = repo.Name,
        Owner = repo.Owner.Login,
        DefaultBranch = repo.DefaultBranchRef != null ? repo.DefaultBranchRef.Name : null,
    })
    .Compile();

var repos = await ghClient.Run(reposQuery, new Dictionary<string, object>
{
    { "ownerId", config["Github:Target"] }
});

Log.Information("Got {Count} repos from {Target}", repos.Count(), config["Github:Target"]);

// Log.Debug("Queueing workers...");
// var workerThreads = int.Parse(config["Github:WorkerThreads"]);
// ThreadPool.SetMinThreads(1, 1);
// ThreadPool.SetMaxThreads(workerThreads,workerThreads);

// var leftToProcess = repos.Count;
// using var resetEvent = new ManualResetEvent(false);
try
{
    foreach (var repo in repos)
    {
        var worker = new Worker(db, ghClient, repo);
        // ThreadPool.QueueUserWorkItem(async _ =>
        // {
        await worker.UpdateRepoStats();

        //     if (Interlocked.Decrement(ref leftToProcess) == 0)
        //         resetEvent.Set();
        // });
    }
}
finally
{
    db.Dispose();
}

// resetEvent.WaitOne();
Log.Information("Done :)");
