using NLog;
using Unity;
using RemoteDesktopCleaner.BackgroundServices;
using Unity.Lifetime;


namespace RemoteDesktopCleaner
{
    class Program
    {
        private static readonly Logger Logger = LogManager.GetLogger("logfileGeneral");

        static void Main(string[] args)
        {
            Logger.Info($"Starting RemoteDesktopClearner console app");
            UnityContainer container = new UnityContainer();
            ConfigureServices(container);

            SynchronizationWorker cw = container.Resolve<SynchronizationWorker>();
            cw.StartAsync(new CancellationToken());
            Console.ReadKey();
        }

        private static void ConfigureServices(UnityContainer container)
        {
            Logger.Info($"Configuring services");
            container.RegisterType<IConfigValidator, ConfigValidator>(new HierarchicalLifetimeManager());
            container.RegisterType<IGatewayRapSynchronizer, GatewayRapSynchronizer>(new HierarchicalLifetimeManager());
            container.RegisterType<ISynchronizer, Synchronizer>(new HierarchicalLifetimeManager());
            container.RegisterType<IGatewayLocalGroupSynchronizer, GatewayLocalGroupSynchronizer>(new HierarchicalLifetimeManager());
        }
    }
}
