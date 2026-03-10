using Microsoft.Extensions.Hosting;
using ParentalControl.Watchdog;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<Worker>();
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "ParentalControlWatchdog";
});
builder.Logging.AddEventLog(settings =>
{
    settings.SourceName = "ParentalControl";
});
builder.Build().Run();
