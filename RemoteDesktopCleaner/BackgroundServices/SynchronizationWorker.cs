using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NLog;
using RemoteDesktopCleaner.BackgroundServices;
//using RemoteDesktopCleaner.Modules.FileArchival;

namespace RemoteDesktopCleaner.BackgroundServices
{
    public sealed class SynchronizationWorker : BackgroundService
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly ISynchronizer _synchronizer;
        private readonly IConfigValidator _configValidator;

        //public SynchronizationWorker(ISynchronizer synchronizer, IFileArchiver fileArchiver, IConfigValidator configValidator)
        //{
        //    _synchronizer = synchronizer;
        //    _fileArchiver = fileArchiver;
        //    _configValidator = configValidator;
        //}

        public SynchronizationWorker(ISynchronizer synchronizer, IConfigValidator configValidator)
        {
            _synchronizer = synchronizer;
            _configValidator = configValidator;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Logger.Debug("CleanerWorker is starting.");
            var gateways = AppConfig.GetGatewaysInUse(); // getting gateway name
            stoppingToken.Register(() => Logger.Debug("CleanerWorker background task is stopping."));
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    //TODO run on each midnight
                    Console.WriteLine($"Starting weekly synchronization for the following gateways: {string.Join(",", gateways)}");
                    if (!_configValidator.MarkObsoleteData()) // running the cleaner
                        Logger.Info("Failed validating model config. Existing one will be used.");

                    //var tasks = gateways
                    //    .Select(gateway => Task.Run(() => _synchronizer.SynchronizeAsync(gateway)))
                    //    .ToList();

                    //await Task.WhenAll(tasks);
                    ////ArchiveGatewayReports();
                    //await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }


        //private void ArchiveGatewayReports()
        //{
        //    try
        //    {
        //        var syncLogsDir = AppConfig.GetSyncLogDir();
        //        _fileArchiver.ArchiveFiles(syncLogsDir);
        //    }
        //    catch (Exception ex)
        //    {
        //        Logger.Warn(ex, "Failed archiving gateway logs.");
        //    }
        //}
    }
}
