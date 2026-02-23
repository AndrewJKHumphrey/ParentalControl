using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ParentalControl.Service;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddHostedService<Worker>();
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "ParentalControlService";
});

builder.Logging.AddEventLog(settings =>
{
    settings.SourceName = "ParentalControl";
});

var host = builder.Build();
host.Run();
