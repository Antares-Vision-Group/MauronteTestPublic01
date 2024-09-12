using Microsoft.Extensions.Logging;
using Moq;

namespace WorkerService1.Tests;

public class WorkerServiceTests : IDisposable
{
    private readonly Mock<ILogger<Worker>> _mockLogger;
    private readonly Worker _workerService;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly CancellationTokenSource _stopCancellationTokenSource;

    public WorkerServiceTests()
    {
        // Arrange
        _mockLogger = new Mock<ILogger<Worker>>();
        _workerService = new Worker(_mockLogger.Object);
        _cancellationTokenSource = new CancellationTokenSource();
        _stopCancellationTokenSource = new CancellationTokenSource();
        _stopCancellationTokenSource.CancelAfter(4000); // Auto-cancel after 4 seconds
    }

    [Fact]
    public async Task WorkerService_LogsAtLeastOnce_AfterRunningForTwoSeconds()
    {
        // Act
        var workerTask = _workerService.StartAsync(_cancellationTokenSource.Token);

        await Task.Delay(2000); // Simulate running for at least 2 seconds

        // Stop the worker service
        await _workerService.StopAsync(_stopCancellationTokenSource.Token);
        await workerTask; // Await the completion of the worker task

        // Assert
        _mockLogger.Verify(
            logger => logger.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                (Func<It.IsAnyType, Exception, string>)It.IsAny<object>()),
            Times.AtLeastOnce);

    }

    public void Dispose()
    {
        _cancellationTokenSource.Dispose();
        _stopCancellationTokenSource.Dispose();
    }
}