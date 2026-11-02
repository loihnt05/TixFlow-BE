using TixFlow.Application;
using TixFlow.Infrastructure;
using TixFlow.Worker;
using Serilog;
using Serilog.Formatting.Json;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSerilog(configuration => configuration.MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext().WriteTo.Console(new JsonFormatter()));
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHostedService<Worker>();

IHost host = builder.Build();
host.Run();

