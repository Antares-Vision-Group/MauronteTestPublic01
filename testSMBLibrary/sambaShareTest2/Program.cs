using sambaShareTest2;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.Configure<SmbShareOptions>(builder.Configuration.GetSection("SmbShare"));
builder.Services.Configure<SmbBrowseOptions>(builder.Configuration.GetSection("SmbBrowse"));
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
