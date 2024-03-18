using Microsoft.Extensions.Hosting;
using RemoteDesktopCleaner.Exceptions;
using System.Diagnostics;
using SynchronizerLibrary.Loggers;
using SynchronizerLibrary.Data;
using SynchronizerLibrary.DataBuffer;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using SynchronizerLibrary.CommonServices;


namespace RemoteDesktopCleaner.BackgroundServices
{
    public sealed class CacheWorker : BackgroundService
    {
        private readonly IGatewayRapSynchronizer _gatewayRapSynchronizer;
        private readonly IGatewayLocalGroupSynchronizer _gatewayLocalGroupSynchronizer;

        public CacheWorker(IGatewayRapSynchronizer gatewayRapSynchronizer, IGatewayLocalGroupSynchronizer gatewayLocalGroupSynchronizer)
        {
            _gatewayLocalGroupSynchronizer = gatewayLocalGroupSynchronizer;
            _gatewayRapSynchronizer = gatewayRapSynchronizer;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            LoggerSingleton.General.Info("Cleaner Worker is starting.");
            var gateways = AppConfig.GetGatewaysInUse();
            Console.WriteLine("Cleaner Worker is starting.");
            try
            {

                //var gatewaysToSynchronize = new List<string>{ "cerngt01","cerngt05","cerngt06","cerngt07" };
                var gatewaysToSynchronize = new List<string> { "cerngt08" };
                foreach (var gatewayName in gatewaysToSynchronize)
                {

                    LoggerSingleton.General.Info($"Starting the synchronization of '{gatewayName}' gateway.");
                    LoggerSingleton.General.Info($"Awaiting getting gateway RAP/Policy names for '{gatewayName}'.");
                    Console.WriteLine($"Get policies on {gatewayName}");
                    var taskGtRapNames = _gatewayRapSynchronizer.GetGatewaysRapNamesAsync(gatewayName, false);
                    LoggerSingleton.General.Info($"Awaiting getting gateway Local Group names for '{gatewayName}'.");
                    _ = await _gatewayLocalGroupSynchronizer.DownloadGatewayConfig(gatewayName, false);
                }

            }
            catch (OperationCanceledException)
            {
                LoggerSingleton.General.Info("Program canceled.");

            }
            catch (Exception ex)
            {
                LoggerSingleton.General.Fatal(ex.ToString());
                Console.WriteLine(ex.ToString());
            }

        }


    }
}
