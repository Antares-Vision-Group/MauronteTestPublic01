namespace WorkerService1;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;

    private static Action<ILogger, DateTimeOffset, Exception> _workerRunning;

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
            _workerRunning(_logger, DateTimeOffset.Now, null!);  
            try
            {
                await Task.Delay(1000, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                _logger.LogInformation("Task was canceled");
                break;
            }
        }
    }
}
