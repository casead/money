using MoneyRecord.Infrastructure;
using MoneyRecord.Infrastructure.Persistence;
using MoneyRecord.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<ReconciliationService>();
builder.Services.AddHostedService<ReconciliationWorker>();

var host = builder.Build();
host.Run();
