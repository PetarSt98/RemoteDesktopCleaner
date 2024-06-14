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
    public sealed class LevelingWorker : BackgroundService
    {
        private readonly IConfigValidator _configValidator;
        private readonly IDataLeveling _dataLeveling;

        public LevelingWorker(IDataLeveling dataLeveling, IConfigValidator configValidator)
        {
            _dataLeveling = dataLeveling;
            _configValidator = configValidator;
        }

        public Task StartWorkAsync(CancellationToken cancellationToken)
        {
            return ExecuteAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            LoggerSingleton.General.Info("Leveling Worker is starting.");
            Console.WriteLine("Leveling Worker is starting.");
            //stoppingToken.Register(() => LoggerSingleton.General.Info("Leveling background task is stopping."));
            try
            {

                var gatewaysToSynchronize = AppConfig.GetGatewaysInUse();

                await _dataLeveling.LeveloutDataAsync(gatewaysToSynchronize);

                foreach (var gatewayName in gatewaysToSynchronize)
                {

                    GlobalInstance.Instance.Names.Add(gatewayName);
                    GlobalInstance.Instance.ObjectLists[gatewayName] = new ConcurrentDictionary<string, RAP_ResourceStatus>();
                    await _dataLeveling.SynchronizeAsync(gatewayName);
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
