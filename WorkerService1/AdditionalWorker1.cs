using System;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace WorkerService1;

public class AdditionalWorker1 : BackgroundService
{

    private readonly ILogger<AdditionalWorker1> _logger;

    private static readonly Action<ILogger, DateTimeOffset, Exception> _workerRunning;

    static AdditionalWorker1()
    {
        _workerRunning = LoggerMessage.Define<DateTimeOffset>(
            LogLevel.Information,
            new EventId(1, "AdditionalWorker1 running"),
            "Worker running at: {time}");
    }

    public AdditionalWorker1(ILogger<AdditionalWorker1> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Executes the background worker logic, periodically logging activity
    /// until a cancellation is requested.
    /// </summary>
    /// <param name="stoppingToken">
    /// A token that signals when the background operation should stop.
    /// </param>
    /// <returns>
    /// A <see cref="Task"/> that represents the lifetime of the background operation.
    /// </returns>
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
