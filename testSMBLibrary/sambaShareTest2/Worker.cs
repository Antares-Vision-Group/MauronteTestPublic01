using Microsoft.Extensions.Options;
using SMBLibrary;
using SMBLibrary.Client;

namespace sambaShareTest2;

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

        var client = new SMB2Client();

        bool connected = client.Connect(_shareOptions.Host, SMBTransportType.DirectTCPTransport);
        if (!connected)
        {
            _logger.LogError("Failed to connect to host '{Host}'", _shareOptions.Host);
            _lifetime.StopApplication();
            return;
        }

        NTStatus status = client.Login(_shareOptions.Domain, _shareOptions.Username, _shareOptions.Password);
        if (status != NTStatus.STATUS_SUCCESS)
        {
            _logger.LogError("Login failed with status {Status}", status);
            client.Disconnect();
            _lifetime.StopApplication();
            return;
        }

        ISMBFileStore fileStore = client.TreeConnect(_shareOptions.ShareName, out status);
        if (status != NTStatus.STATUS_SUCCESS)
        {
            _logger.LogError("TreeConnect to share '{Share}' failed with status {Status}",
                _shareOptions.ShareName, status);
            client.Logoff();
            client.Disconnect();
            _lifetime.StopApplication();
            return;
        }

        object? directoryHandle;
        FileStatus fileStatus;
        status = fileStore.CreateFile(
            out directoryHandle, out fileStatus,
            _browseOptions.SubFolder,
            AccessMask.GENERIC_READ,
            SMBLibrary.FileAttributes.Directory,
            ShareAccess.Read | ShareAccess.Write,
            CreateDisposition.FILE_OPEN,
            CreateOptions.FILE_DIRECTORY_FILE,
            null);

        if (status != NTStatus.STATUS_SUCCESS)
        {
            _logger.LogError("Cannot open folder '{Folder}': {Status}", _browseOptions.SubFolder, status);
        }
        else
        {
            _logger.LogInformation("Listing files in '\\\\{Host}\\{Share}\\{Folder}':",
                _shareOptions.Host, _shareOptions.ShareName, _browseOptions.SubFolder);

            NTStatus queryStatus;
            do
            {
                queryStatus = fileStore.QueryDirectory(
                    out var page, directoryHandle, "*",
                    FileInformationClass.FileDirectoryInformation);

                if (queryStatus == NTStatus.STATUS_SUCCESS || queryStatus == NTStatus.STATUS_NO_MORE_FILES)
                {
                    foreach (var entry in page)
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
            } while (queryStatus == NTStatus.STATUS_SUCCESS);

            fileStore.CloseFile(directoryHandle);
        }

        fileStore.Disconnect();
        client.Logoff();
        client.Disconnect();

        _lifetime.StopApplication();
    }
}
