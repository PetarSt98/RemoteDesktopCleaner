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
    public sealed class RemovalWorker : BackgroundService
    {
        private readonly IConfigValidator _configValidator;
        private readonly IDataRemoval _dataRemoval;

        public RemovalWorker(IDataRemoval dataRemoval, IConfigValidator configValidator)
        {
            _dataRemoval = dataRemoval;
            _configValidator = configValidator;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            LoggerSingleton.General.Info("Cleaner Worker is starting.");
            Console.WriteLine("Removal Worker is starting.");
            stoppingToken.Register(() => LoggerSingleton.General.Info("CleanerWorker background task is stopping."));
            try
            {
                var gatewaysToSynchronize = AppConfig.GetGatewaysInUse();

                foreach (var gatewayName in gatewaysToSynchronize)
                {

                    GlobalInstance.Instance.Names.Add(gatewayName);
                    GlobalInstance.Instance.ObjectLists[gatewayName] = new ConcurrentDictionary<string, RAP_ResourceStatus>();
                    await _dataRemoval.SynchronizeAsync(gatewayName);
                }

                //DatabaseSynchronizator databaseSynchronizator = new DatabaseSynchronizator();
                //databaseSynchronizator.AverageGatewayReults();
                //databaseSynchronizator.UpdateDatabase();


                //using (var db = new RapContext())
                //{
                //    ConfigValidator.UpdateDatabase(db);
                //}

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
