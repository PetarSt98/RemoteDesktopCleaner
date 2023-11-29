using Microsoft.Extensions.Hosting;
using RemoteDesktopCleaner.Exceptions;
using System.Diagnostics;
using SynchronizerLibrary.Loggers;
using SynchronizerLibrary.Data;
using SynchronizerLibrary.DataBuffer;
using Microsoft.EntityFrameworkCore;


namespace RemoteDesktopCleaner.BackgroundServices
{
    public sealed class SynchronizationWorker : BackgroundService
    {
        private readonly IConfigValidator _configValidator;
        private readonly ISynchronizer _synchronizer;

        public SynchronizationWorker(ISynchronizer synchronizer, IConfigValidator configValidator)
        {
            _synchronizer = synchronizer;
            _configValidator = configValidator;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            LoggerSingleton.General.Info("Cleaner Worker is starting.");
            Console.WriteLine("Cleaner Worker is starting.");
            var gateways = AppConfig.GetGatewaysInUse();
            stoppingToken.Register(() => LoggerSingleton.General.Info("CleanerWorker background task is stopping."));
            //while (!stoppingToken.IsCancellationRequested)
            //{
            try
            {
                Console.WriteLine($"Starting weekly synchronization for the following gateways: {string.Join(",", gateways)}");
                if (_configValidator.MarkObsoleteData())
                {
                    LoggerSingleton.General.Info("Successful marked obsolete data in RemoteDesktop MySQL database.");
                    Console.WriteLine("Successful marked obsolete data in RemoteDesktop MySQL database.");
                }
                else
                {
                    LoggerSingleton.General.Info("Failed marking obsolete data in RemoteDesktop MySQL database. Existing one will be used.");
                    Console.WriteLine("Failed marking obsolete data in RemoteDesktop MySQL database. Existing one will be used.");
                }

                var gatewaysToSynchronize = new List<string> { "cerngt01", "cerngt05", "cerngt06", "cerngt07" };
                var synchronizationTasks = new List<Task>();

                foreach (var gatewayName in gatewaysToSynchronize)
                {
                    GlobalInstance.Instance.Names.Add(gatewayName);
                    GlobalInstance.Instance.ObjectLists[gatewayName] = new Dictionary<string, RAP_ResourceStatus>();
                    var syncTask = _synchronizer.SynchronizeAsync(gatewayName);
                    synchronizationTasks.Add(syncTask);
                }

                // Wait for all synchronization tasks to complete
                await Task.WhenAll(synchronizationTasks);

                DatabaseSynchronizator databaseSynchronizator = new DatabaseSynchronizator();
                databaseSynchronizator.AverageGatewayReults();
                databaseSynchronizator.UpdateDatabase();


                using (var db = new RapContext())
                {
                    ConfigValidator.UpdateDatabase(db);
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
