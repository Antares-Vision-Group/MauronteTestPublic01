using Microsoft.Extensions.Options;
using SMBLibrary;
using SMBLibrary.Client;
using sambaShareCommon;

namespace sambaShareTest4;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly SmbShareOptions _shareOptions;
    private readonly SmbBrowseOptions _browseOptions;
    private readonly IHostApplicationLifetime _lifetime;

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
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield(); // allow host startup to complete

        var client = new SMB2Client(
            TimeSpan.FromSeconds(20),
            msg => _logger.LogError("{Msg}", msg));

        bool connected = await client.Connect(_shareOptions.Host, SMBTransportType.DirectTCPTransport);
        if (!connected)
        {
            _logger.LogError("Failed to connect to host '{Host}'", _shareOptions.Host);
            _lifetime.StopApplication();
            return;
        }

        NTStatus status = await client.Login(_shareOptions.Domain, _shareOptions.Username, _shareOptions.Password);
        if (status != NTStatus.STATUS_SUCCESS)
        {
            _logger.LogError("Login failed with status {Status}", status);
            client.Disconnect();
            _lifetime.StopApplication();
            return;
        }

        var treeResult = await client.TreeConnect(_shareOptions.ShareName);
        if (treeResult.Status != NTStatus.STATUS_SUCCESS)
        {
            _logger.LogError("TreeConnect to share '{Share}' failed with status {Status}",
                _shareOptions.ShareName, treeResult.Status);
            await client.Logoff();
            client.Disconnect();
            _lifetime.StopApplication();
            return;
        }

        var fileStore = (SMB2FileStore)treeResult.Content;

        var createResult = await fileStore.CreateFile(
            _browseOptions.SubFolder,
            AccessMask.GENERIC_READ,
            SMBLibrary.FileAttributes.Directory,
            ShareAccess.Read | ShareAccess.Write,
            CreateDisposition.FILE_OPEN,
            CreateOptions.FILE_DIRECTORY_FILE,
            null);

        if (createResult.Status != NTStatus.STATUS_SUCCESS)
        {
            _logger.LogError("Cannot open folder '{Folder}': {Status}", _browseOptions.SubFolder, createResult.Status);
        }
        else
        {
            var (directoryHandle, _) = createResult.Content;

            _logger.LogInformation("Listing files in '\\\\{Host}\\{Share}\\{Folder}':",
                _shareOptions.Host, _shareOptions.ShareName, _browseOptions.SubFolder);

            var queryResult = await fileStore.QueryDirectory(
                directoryHandle, "*", FileInformationClass.FileDirectoryInformation);

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
                _logger.LogError("QueryDirectory failed with status {Status}", queryResult.Status);
            }

            await fileStore.CloseFile(directoryHandle);
        }

        await fileStore.Disconnect();
        await client.Logoff();
        client.Disconnect();

        _lifetime.StopApplication();
    }
}
