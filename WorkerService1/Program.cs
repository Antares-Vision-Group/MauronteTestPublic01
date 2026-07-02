using Serilog;
using WorkerService1;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddHostedService<Worker>();
        services.AddHostedService<AdditionalWorker1>();
    }).ConfigureLogging((context, logging) =>
    {
        logging.AddFile(context.Configuration.GetSection("Logging"));
    })
    .Build();

await host.RunAsync();
