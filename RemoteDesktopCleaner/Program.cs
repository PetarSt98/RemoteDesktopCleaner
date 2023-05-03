using NLog;
using Unity;
using RemoteDesktopCleaner.BackgroundServices;
using Unity.Lifetime;
using RemoteDesktopCleaner.Exceptions;
using NLog.Targets;


namespace RemoteDesktopCleaner
{
    class Program
    {
        private static Logger LoggerGeneral;

        static void Main(string[] args)
        {
            try
            {
                //string directoryPath = $@"{Directory.GetParent(Environment.CurrentDirectory).Parent.Parent.FullName}";
                //Configuration config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
                //config.AppSettings.Settings["SourceCodePath"].Value = directoryPath;
                //config.Save(ConfigurationSaveMode.Modified);
                //ConfigurationManager.RefreshSection("appSettings");

                // Load the NLog configuration
                string configFilePath = $@"{Directory.GetParent(Environment.CurrentDirectory).Parent.Parent.FullName}\nlog.config";
                LogManager.Configuration = new NLog.Config.XmlLoggingConfiguration(configFilePath);

                // Reload the NLog configuration
                LogManager.ReconfigExistingLoggers();

                // Get the path to the .log file for each target
                foreach (var target in LogManager.Configuration.AllTargets)
                {
                    if (target is FileTarget fileTarget)
                    {
                        string logFilePath = fileTarget.FileName.Render(new LogEventInfo());
                        Console.WriteLine($"Log file path for {fileTarget.Name}: {logFilePath}");
                    }
                }

                // Get the logger instance
                LoggerGeneral = LogManager.GetLogger("logfileGeneral");
                LoggerGeneral.Info($"Starting RemoteDesktopClearner console app");

                UnityContainer container = new UnityContainer();
                ConfigureServices(container);

                SynchronizationWorker cw = container.Resolve<SynchronizationWorker>();
                cw.StartAsync(new CancellationToken());
                Console.ReadKey();
            }
            catch (NoAccesToDomain)
            {
                LoggerGeneral.Fatal("Unable to access domain (to fetch admin usernames).");
            }
            catch (Exception ex)
            {
                LoggerGeneral.Fatal(ex.Message);
            }
        }

        private static void ConfigureServices(UnityContainer container)
        {
            LoggerGeneral.Info($"Configuring services");
            container.RegisterType<IConfigValidator, ConfigValidator>(new HierarchicalLifetimeManager());
            container.RegisterType<IGatewayRapSynchronizer, GatewayRapSynchronizer>(new HierarchicalLifetimeManager());
            container.RegisterType<ISynchronizer, Synchronizer>(new HierarchicalLifetimeManager());
            container.RegisterType<IGatewayLocalGroupSynchronizer, GatewayLocalGroupSynchronizer>(new HierarchicalLifetimeManager());
        }
    }
}
