using System.ServiceProcess;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ParentalControl.Watchdog;

public class Worker(ILogger<Worker> logger) : BackgroundService
{
    private const string MainServiceName = "ParentalControlService";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Watchdog started — monitoring {Service}", MainServiceName);

        // Ensure main service is running at watchdog startup
        TryStartMain();

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); }
            catch (OperationCanceledException) { break; }

            if (stoppingToken.IsCancellationRequested) break;

            try
            {
                using var sc = new ServiceController(MainServiceName);
                if (sc.Status == ServiceControllerStatus.Stopped)
                {
                    logger.LogWarning("Watchdog: {Service} was stopped unexpectedly — restarting.", MainServiceName);
                    sc.Start();
                    sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
                    logger.LogInformation("Watchdog: {Service} restarted successfully.", MainServiceName);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Watchdog: failed to check/restart {Service}", MainServiceName);
            }
        }

        logger.LogInformation("Watchdog stopping — will not restart {Service}", MainServiceName);
    }

    private void TryStartMain()
    {
        try
        {
            using var sc = new ServiceController(MainServiceName);
            if (sc.Status != ServiceControllerStatus.Running &&
                sc.Status != ServiceControllerStatus.StartPending)
            {
                sc.Start();
                sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Watchdog: could not start {Service} at watchdog startup", MainServiceName);
        }
    }
}
