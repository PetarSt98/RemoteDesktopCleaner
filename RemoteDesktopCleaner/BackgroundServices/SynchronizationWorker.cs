using Microsoft.Extensions.Hosting;
using RemoteDesktopCleaner.Exceptions;
using System.Diagnostics;
using SynchronizerLibrary.Loggers;
using SynchronizerLibrary.Data;


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
                //CloneSQL2MySQLdb();

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

                _synchronizer.SynchronizeAsync("cerngt01");

                using (var db = new RapContext())
                {
                    ConfigValidator.UpdateDatabase(db);
                }
                    //break;
                }
                catch (OperationCanceledException)
                {
                    LoggerSingleton.General.Info("Program canceled.");
                    //break;
                }
                catch (CloningException)
                {
                    //break;
                }
                catch (Exception ex)
                {
                    LoggerSingleton.General.Fatal(ex.ToString());
                    Console.WriteLine(ex.ToString());
                    //break;
                }
            //}
        }

        private readonly string source_username = @"CERN\pstojkov";
        private readonly string source_password = "GeForce9800GT.";
        private readonly string target_hostname = "dbod-remotedesktop.cern.ch";
        private readonly string target_username = "admin";
        private readonly string target_password = "oUgDdp5AnSzrvizXtN";
        private readonly string target_database = "RemoteDesktop";
        private readonly string target_port = "5500";

        private void CloneSQL2MySQLdb()
        {
            LoggerSingleton.General.Info("Started cloning RemoteDesktop database as MySQL database.");
            string scriptPath = $@"{Directory.GetParent(Environment.CurrentDirectory).Parent.Parent.FullName}\PowerShellScripts\database_copy_SQL2MySQL.ps1";
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-ExecutionPolicy Bypass -File \"{scriptPath}\" -source_username \"{source_username}\" -source_password \"{source_password}\" -target_hostname \"{target_hostname}\" -target_username \"{target_username}\" -target_password \"{target_password}\" -target_database \"{target_database}\" -target_port \"{target_port}\" -Add_toDelete_column",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using Process process = new Process { StartInfo = startInfo };
            process.Start();

            string errors = process.StandardError.ReadToEnd();
            if (errors.Length > 0)
            {
                LoggerSingleton.General.Fatal("Failed cloning RemoteDesktop database as MySQL database.");
                throw new CloningException();
            }
            process.WaitForExit();
            LoggerSingleton.General.Info("Successful cloning RemoteDesktop database as MySQL database.");
        }

    }
}
