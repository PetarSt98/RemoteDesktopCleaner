﻿using Microsoft.Extensions.Hosting;
using RemoteDesktopCleaner.Exceptions;
using System.Diagnostics;
using SynchronizerLibrary.Loggers;
using SynchronizerLibrary.Data;
using SynchronizerLibrary.DataBuffer;
using Microsoft.EntityFrameworkCore;


namespace RemoteDesktopCleaner.BackgroundServices
{
    public sealed class RestorationWorker : BackgroundService
    {
        private readonly IConfigValidator _configValidator;
        private readonly IDataRestoration _dataRestoration;

        public RestorationWorker(IDataRestoration dataRestoration, IConfigValidator configValidator)
        {
            _dataRestoration = dataRestoration;
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

                var gatewaysToSynchronize = new List<string>{"cerngt01"};

                foreach (var gatewayName in gatewaysToSynchronize)
                {

                    GlobalInstance.Instance.Names.Add(gatewayName);
                    GlobalInstance.Instance.ObjectLists[gatewayName] = new Dictionary<string, RAP_ResourceStatus>();
                    _dataRestoration.SynchronizeAsync(gatewayName);
                }
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
