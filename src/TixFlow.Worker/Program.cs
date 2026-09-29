using TixFlow.Application;
using TixFlow.Infrastructure;
using TixFlow.Worker;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHostedService<Worker>();

IHost host = builder.Build();
host.Run();

