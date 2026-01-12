using System;

namespace WorkerService1;

public class AdditionalWorker1 : BackgroundService
{

    private readonly ILogger<AdditionalWorker1> _logger;

    private static Action<ILogger, DateTimeOffset, Exception> _workerRunning;

    static AdditionalWorker1()
    {
        _workerRunning = LoggerMessage.Define<DateTimeOffset>(
            LogLevel.Information,
            new EventId(1, "Worker running"),
            "Worker running at: {time}");
    }

    public AdditionalWorker1(ILogger<AdditionalWorker1> logger)
    {
        _logger = logger;
    }

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
