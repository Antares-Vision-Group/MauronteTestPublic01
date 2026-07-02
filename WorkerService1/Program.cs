using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using WorkerService1;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddHostedService<Worker>();
        services.AddHostedService<AdditionalWorker1>();
        services.AddOpenTelemetry()
            .WithTracing(builder =>
            {
                builder
                    .AddSource("WorkerService1.Worker")
                    .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("WorkerService1.Service"))                    
                    .AddConsoleExporter();
            })
            .WithMetrics(builder =>
            {
                builder.AddRuntimeInstrumentation();
                builder.AddConsoleExporter();
                builder.AddMeter("WorkerService1.Meter");
            });

    }).ConfigureLogging((context, logging) =>
    {
        logging.AddFile(context.Configuration.GetSection("Logging"));
        logging.AddOpenTelemetry(options =>
        {
            options.AddConsoleExporter();
        });
    })
    .Build();

await host.RunAsync();
