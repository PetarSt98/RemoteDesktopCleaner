using Microsoft.Extensions.DependencyInjection;
using RemoteDesktopCleaner.BackgroundServices;
using RemoteDesktopCleaner.Exceptions;
using SynchronizerLibrary.CommonServices;
using SynchronizerLibrary.Loggers;


namespace RemoteDesktopCleaner
{

    internal class StaticFunctions
    {
        protected static IServiceProvider ConfigureServices()
        {
            LoggerSingleton.General.Info("Configuring services");
            Console.WriteLine("Configuring services");

            var services = new ServiceCollection();
            services.AddSingleton<IConfigValidator, ConfigValidator>();
            services.AddSingleton<IGatewayRapSynchronizer, GatewayRapSynchronizer>();
            services.AddSingleton<IDataRestoration, DataRestoration>();
            services.AddSingleton<ISynchronizer, Synchronizer>();
            services.AddSingleton<IGatewayLocalGroupSynchronizer, GatewayLocalGroupSynchronizer>();
            services.AddSingleton<SynchronizationWorker>();
            services.AddSingleton<CacheWorker>();
            services.AddSingleton<RestorationWorker>();

            var serviceProvider = services.BuildServiceProvider();

            LoggerSingleton.General.Info("Finished configuring services");
            Console.WriteLine("Finished configuring services");

            return serviceProvider;
        }
        protected static void EnsureDirectoriesExist()
        {
            string[] directories = { "Logs", "Info", "Cache" };

            foreach (var directory in directories)
            {
                string directoryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, directory);

                if (!Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }
            }
        }
    }

#if PROGRAM
    internal class Program: StaticFunctions
    {
        static void Main(string[] args)
        {
            EnsureDirectoriesExist();
            try
            {
                LoggerSingleton.General.Info("Starting RemoteDesktopCleaner console app");
                Console.WriteLine("Starting RemoteDesktopCleaner console app");

                var serviceProvider = ConfigureServices();

                var cw = serviceProvider.GetRequiredService<SynchronizationWorker>();

                Console.WriteLine("Starting initial cleaning");
                cw.StartAsync(new CancellationToken()).GetAwaiter().GetResult();

            }
            catch (NoAccesToDomain)
            {
                LoggerSingleton.General.Fatal("Unable to access domain (to fetch admin usernames).");
                Console.WriteLine("Unable to access domain (to fetch admin usernames).");
            }
            catch (Exception ex)
            {
                LoggerSingleton.General.Fatal(ex.Message);
                Console.WriteLine(ex.Message);
            }
        }
    }
#endif
#if CACHEDATA
    internal class CacheData: StaticFunctions
    {
        static void Main(string[] args)
        {
            EnsureDirectoriesExist();
            try
            {
                LoggerSingleton.General.Info("Starting RemoteDesktopCleaner console app");
                Console.WriteLine("Starting RemoteDesktopCleaner console app");

                var serviceProvider = ConfigureServices();

                var cw = serviceProvider.GetRequiredService<CacheWorker>();

                Console.WriteLine("Starting initial cleaning");
                cw.StartAsync(new CancellationToken()).GetAwaiter().GetResult();

            }
            catch (NoAccesToDomain)
            {
                LoggerSingleton.General.Fatal("Unable to access domain (to fetch admin usernames).");
                Console.WriteLine("Unable to access domain (to fetch admin usernames).");
            }
            catch (Exception ex)
            {
                LoggerSingleton.General.Fatal(ex.Message);
                Console.WriteLine(ex.Message);
            }
        }
    }
#endif
#if RESTOREDATA
    internal class RestoreData: StaticFunctions
    {
        static void Main(string[] args)
        {
            EnsureDirectoriesExist();
            try
            {
                LoggerSingleton.General.Info("Starting RemoteDesktopCleaner console app");
                Console.WriteLine("Starting RemoteDesktopCleaner console app");

                var serviceProvider = ConfigureServices();

                var cw = serviceProvider.GetRequiredService<RestorationWorker>();

                Console.WriteLine("Starting initial cleaning");
                cw.StartAsync(new CancellationToken()).GetAwaiter().GetResult();

            }
            catch (NoAccesToDomain)
            {
                LoggerSingleton.General.Fatal("Unable to access domain (to fetch admin usernames).");
                Console.WriteLine("Unable to access domain (to fetch admin usernames).");
            }
            catch (Exception ex)
            {
                LoggerSingleton.General.Fatal(ex.Message);
                Console.WriteLine(ex.Message);
            }
        }
    }
#endif
}
