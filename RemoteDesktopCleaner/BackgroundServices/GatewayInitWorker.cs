using Microsoft.Extensions.Hosting;
using RemoteDesktopCleaner.Exceptions;
using System.Diagnostics;
using SynchronizerLibrary.Loggers;
using SynchronizerLibrary.Data;
using SynchronizerLibrary.DataBuffer;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;

namespace RemoteDesktopCleaner.BackgroundServices
{
    public sealed class GatewayInitWorker : BackgroundService
    {
        private readonly IConfigValidator _configValidator;
        private readonly IServerInit _serverInit;

        public GatewayInitWorker(IServerInit serverInit, IConfigValidator configValidator)
        {
            _serverInit = serverInit;
            _configValidator = configValidator;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            LoggerSingleton.General.Info("Cleaner Worker is starting.");
            Console.WriteLine("Cleaner Worker is starting.");
            var gateways = AppConfig.GetGatewaysInUse();
            stoppingToken.Register(() => LoggerSingleton.General.Info("CleanerWorker background task is stopping."));
            try
            {

                var gatewaysToSynchronize = AppConfig.GetGatewaysInUse();
                foreach (var gatewayName in gatewaysToSynchronize)
                {

                    GlobalInstance.Instance.Names.Add(gatewayName);
                    GlobalInstance.Instance.ObjectLists[gatewayName] = new ConcurrentDictionary<string, RAP_ResourceStatus>();
                    await _serverInit.SynchronizeAsync(gatewayName);
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
