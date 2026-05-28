
using EzSmb;
using EzSmb.Params;
using Microsoft.Extensions.Options;
using sambaShareCommon;

namespace sambaShareTest3;

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

        var path = $@"{_shareOptions.Host}\{_shareOptions.ShareName}\{_browseOptions.SubFolder}";

        var paramSet = new ParamSet
        {
            UserName = _shareOptions.Username,
            Password = _shareOptions.Password,
            DomainName = _shareOptions.Domain
        };

        var folder = await Node.GetNode(path, paramSet);

        if (folder == null || folder.HasError)
        {
            _logger.LogError("Failed to connect to '{Path}'", path);
            if (folder != null)
                foreach (var err in folder.Errors)
                    _logger.LogError("  {Error}", err);

            _lifetime.StopApplication();
            return;
        }

        _logger.LogInformation("Listing files in '\\\\{Path}':", path);

        var nodes = await folder.GetList();

        if (nodes == null || folder.HasError)
        {
            _logger.LogError("Failed to list folder contents");
            foreach (var err in folder.Errors)
                _logger.LogError("  {Error}", err);
        }
        else
        {
            foreach (var node in nodes)
            {
                _logger.LogInformation("  {Type}  {Name}",
                    node.Type == NodeType.Folder ? "[DIR] " : "[FILE]", node.Name);
            }
        }

        _lifetime.StopApplication();
    }
}
