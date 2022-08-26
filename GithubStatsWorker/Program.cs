using GithubStatsWorker;
using Microsoft.Extensions.Configuration;
using Octokit;
using Serilog;

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

var ghClient = new GitHubClient(new ProductHeaderValue("Polarizedions-repo-stats-puller"));
ghClient.Credentials = new Credentials(config["Github:PAT"]);

Log.Information("Fetching all repos for {Target}", config["Github:Target"]);
var repos = new List<Repository>();
while (true)
{
    var searchResult = await ghClient.Search.SearchRepo(new SearchRepositoriesRequest
    {
        User = config["Github:Target"],
    });
    
    repos.AddRange(searchResult.Items);
    if (searchResult.TotalCount >= repos.Count)
    {
        break;
    }
}

Log.Information("Got {Count} repos from {Target}", repos.Count, config["Github:Target"]);

Log.Debug("Queueing workers...");
var workerThreads = int.Parse(config["Github:WorkerThreads"]);
ThreadPool.SetMinThreads(workerThreads, workerThreads);
ThreadPool.SetMinThreads(workerThreads,workerThreads);

var leftToProcess = repos.Count;
using var resetEvent = new ManualResetEvent(false);
foreach (var repo in repos)
{
    var worker = new Worker(db, config, ghClient, repo);
    ThreadPool.QueueUserWorkItem(async x =>
    {
        await worker.UpdateRepoStats(null);
        if (Interlocked.Decrement(ref leftToProcess) == 0)
            resetEvent.Set();
    });
}
resetEvent.WaitOne();
Log.Information("Done :)");
