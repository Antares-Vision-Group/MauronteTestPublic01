using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace WorkerService1;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;

    private static Action<ILogger, DateTimeOffset, Exception> _workerRunning;
    private static readonly Meter _workerMeter = new("WorkerService1.Meter", "1.0");
    private static readonly Counter<long> _loopsCounter = _workerMeter.CreateCounter<long>("loops");
    private static readonly Counter<long> _sleepCounter = _workerMeter.CreateCounter<long>("sleeped", "ms");

    private static readonly ActivitySource ActivitySource = new("WorkerService1.Worker");
    static Worker()
    {
        _workerRunning = LoggerMessage.Define<DateTimeOffset>(
            LogLevel.Information,
            new EventId(1, "Worker running"),
            "Worker running at: {time}");
    }

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// This method is called when the <see cref="IHostedService"/> starts. The implementation should return a task that represents the lifetime of the long
    /// </summary>
    /// <param name="stoppingToken"></param>
    /// <returns></returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            _loopsCounter.Add(1);
            _workerRunning(_logger, DateTimeOffset.Now, null!);  
 
            using (var activity = ActivitySource.StartActivity("Delay"))
            {

                try
                {
                    await Task.Delay(1000, stoppingToken);
                    _sleepCounter.Add(1000);
                    _logger.LogDebug("Delay succesfully completed");
                }
                catch (TaskCanceledException)
                {
                    _logger.LogInformation("Task was canceled");
                    break;
                }
            }
        }
    }
}