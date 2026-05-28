using Microsoft.Extensions.Options;
using SMBLibrary;
using SMBLibrary.Client;
using sambaShareCommon;

namespace sambaShareTest4;

public partial class Worker : BackgroundService, IAsyncDisposable
{
    private readonly ILogger<Worker> _logger;
    private readonly SmbShareOptions _shareOptions;
    private readonly SmbBrowseOptions _browseOptions;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly SMB2Client _client;

    private SMB2FileStore? _fileStore;
    private object? _directoryHandle;
    private bool _loggedIn;

    public Worker(
        ILogger<Worker> logger,
        IOptions<SmbShareOptions> shareOptions,
        IOptions<SmbBrowseOptions> browseOptions,
        IHostApplicationLifetime lifetime)
    {
        _logger = logger;
        _shareOptions = shareOptions.Value;
        _browseOptions = browseOptions.Value;
        _lifetime = lifetime;
        _client = new SMB2Client(
            TimeSpan.FromSeconds(20),
            msg => LogClientError(_logger, msg));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield(); // allow host startup to complete

        bool connected = await _client.Connect(_shareOptions.Host, SMBTransportType.DirectTCPTransport);
        if (!connected)
        {
            _logger.LogError("Failed to connect to host '{Host}'", _shareOptions.Host);
            _lifetime.StopApplication();
            return;
        }

        NTStatus status = await _client.Login(_shareOptions.Domain, _shareOptions.Username, _shareOptions.Password);
        if (status != NTStatus.STATUS_SUCCESS)
        {
            LogLoginFailed(_logger, status);
            _lifetime.StopApplication();
            return;
        }
        _loggedIn = true;

        var treeResult = await _client.TreeConnect(_shareOptions.ShareName);
        if (treeResult.Status != NTStatus.STATUS_SUCCESS)
        {
            LogTreeConnectFailed(_logger, _shareOptions.ShareName, treeResult.Status);
            _lifetime.StopApplication();
            return;
        }

        _fileStore = (SMB2FileStore)treeResult.Content;

        var createResult = await _fileStore.CreateFile(
            _browseOptions.SubFolder,
            AccessMask.GENERIC_READ,
            SMBLibrary.FileAttributes.Directory,
            ShareAccess.Read | ShareAccess.Write,
            CreateDisposition.FILE_OPEN,
            CreateOptions.FILE_DIRECTORY_FILE,
            null);

        if (createResult.Status != NTStatus.STATUS_SUCCESS)
        {
            LogOpenFolderFailed(_logger, _browseOptions.SubFolder, createResult.Status);
        }
        else
        {
            (_directoryHandle, _) = createResult.Content;

            _logger.LogInformation("Listing files in '\\\\{Host}\\{Share}\\{Folder}':",
                _shareOptions.Host, _shareOptions.ShareName, _browseOptions.SubFolder);

            var queryResult = await _fileStore.QueryDirectory(
                _directoryHandle, "*", FileInformationClass.FileDirectoryInformation);

            if (queryResult.Status == NTStatus.STATUS_SUCCESS || queryResult.Status == NTStatus.STATUS_NO_MORE_FILES)
            {
                foreach (var entry in queryResult.Content)
                {
                    if (entry is FileDirectoryInformation info &&
                        info.FileName != "." && info.FileName != "..")
                    {
                        bool isDir = info.FileAttributes.HasFlag(SMBLibrary.FileAttributes.Directory);
                        _logger.LogInformation("  {Type}  {Name}",
                            isDir ? "[DIR] " : "[FILE]", info.FileName);
                    }
                }
            }
            else
            {
                LogQueryDirectoryFailed(_logger, queryResult.Status);
            }
        }

        _lifetime.StopApplication();
    }

    public async ValueTask DisposeAsync()
    {
        if (_directoryHandle is not null && _fileStore is not null)
        {
            await _fileStore.CloseFile(_directoryHandle);
            _directoryHandle = null;
        }

        if (_fileStore is not null)
        {
            await _fileStore.Disconnect();
            _fileStore = null;
        }

        if (_loggedIn)
            await _client.Logoff();
        _client.Disconnect();

        base.Dispose();
        GC.SuppressFinalize(this);
    }

    [LoggerMessage(EventId = 0, Level = LogLevel.Error,
        Message = "SMB client error: {Message}")]
    static partial void LogClientError(ILogger logger, string message);

    [LoggerMessage(EventId = 1, Level = LogLevel.Error,
        Message = "Login failed with status {Status}")]
    static partial void LogLoginFailed(ILogger logger, NTStatus status);

    [LoggerMessage(EventId = 2, Level = LogLevel.Error,
        Message = "TreeConnect to share '{Share}' failed with status {Status}")]
    static partial void LogTreeConnectFailed(ILogger logger, string share, NTStatus status);

    [LoggerMessage(EventId = 3, Level = LogLevel.Error,
        Message = "Cannot open folder '{Folder}': {Status}")]
    static partial void LogOpenFolderFailed(ILogger logger, string folder, NTStatus status);

    [LoggerMessage(EventId = 4, Level = LogLevel.Error,
        Message = "QueryDirectory failed with status {Status}")]
    static partial void LogQueryDirectoryFailed(ILogger logger, NTStatus status);
}
