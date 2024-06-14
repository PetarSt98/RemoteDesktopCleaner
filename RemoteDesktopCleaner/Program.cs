using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RemoteDesktopCleaner.BackgroundServices;
using RemoteDesktopCleaner.Exceptions;
using SynchronizerLibrary.CommonServices;
using SynchronizerLibrary.Data;
using SynchronizerLibrary.Loggers;
using System.Security;

namespace RemoteDesktopCleaner
{

    internal class StaticFunctions
    {
        public static IServiceProvider ConfigureServices()
        {
            LoggerSingleton.General.Info("Configuring services");
            Console.WriteLine("Configuring services");

            var services = new ServiceCollection();
            services.AddSingleton<IConfigValidator, ConfigValidator>();
            services.AddSingleton<IGatewayRapSynchronizer, GatewayRapSynchronizer>();
            services.AddSingleton<IDataRestoration, DataRestoration>();
            services.AddSingleton<IDataLeveling, DataLeveling>();
            services.AddSingleton<IDataRemoval, DataRemoval>();
            services.AddSingleton<IServerInit, ServerInit>();
            services.AddSingleton<ISynchronizer, Synchronizer>();
            services.AddSingleton<IGatewayLocalGroupSynchronizer, GatewayLocalGroupSynchronizer>();
            services.AddSingleton<SynchronizationWorker>();
            services.AddSingleton<CacheWorker>();
            services.AddSingleton<RestorationWorker>();
            services.AddSingleton<GatewayInitWorker>();
            services.AddSingleton<LevelingWorker>();
            services.AddSingleton<RemovalWorker>();

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

        protected static void AnnounceStart()
        {
            return;
            try
            {
                string variableName = "CLEANER_STATUS";

                string newValue = "ON";

                Environment.SetEnvironmentVariable(variableName, newValue, EnvironmentVariableTarget.Machine);
            }
            catch (SecurityException)
            {
                Console.WriteLine("Error: Administrative privileges are required to modify system environment variables.");
                Environment.Exit(1);
            }
}

        protected static void AnnounceEnd()
        {
            try
            {
                string variableName = "CLEANER_STATUS";

                string newValue = "OFF";

                Environment.SetEnvironmentVariable(variableName, newValue, EnvironmentVariableTarget.Machine);
            }
            catch (SecurityException)
            {
                Console.WriteLine("Error: Administrative privileges are required to modify system environment variables.");
                Environment.Exit(1);
            }
        }

    }

#if CLEANER
    internal class Clean: StaticFunctions
    {
        static async Task Main(string[] args)
        {
            AnnounceStart();
            EnsureDirectoriesExist();

            var host = CreateHostBuilder(args).Build();
            try
            {
                LoggerSingleton.General.Info("Starting RemoteDesktopCleaner console app");
                Console.WriteLine("Starting RemoteDesktopCleaner console app");

                await host.RunAsync();

                Console.WriteLine("Edning...");
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
            finally
            {
                AnnounceEnd();
            }
        }
        protected static IServiceProvider ConfigureServices(IServiceCollection services)
        {
            LoggerSingleton.General.Info("Configuring services");
            Console.WriteLine("Configuring services");

            services.AddSingleton<IConfigValidator, ConfigValidator>();
            services.AddSingleton<IGatewayRapSynchronizer, GatewayRapSynchronizer>();
            services.AddSingleton<IDataRestoration, DataRestoration>();
            services.AddSingleton<IDataRemoval, DataRemoval>();
            services.AddSingleton<IServerInit, ServerInit>();
            services.AddSingleton<ISynchronizer, Synchronizer>();
            services.AddSingleton<IGatewayLocalGroupSynchronizer, GatewayLocalGroupSynchronizer>();
            services.AddSingleton<SynchronizationWorker>();
            services.AddSingleton<CacheWorker>();
            services.AddSingleton<RestorationWorker>();
            services.AddSingleton<GatewayInitWorker>();
            services.AddSingleton<RemovalWorker>();

            var serviceProvider = services.BuildServiceProvider();

            LoggerSingleton.General.Info("Finished configuring services");
            Console.WriteLine("Finished configuring services");

            return serviceProvider;
        }
        public static IHostBuilder CreateHostBuilder(string[] args) =>
    Host.CreateDefaultBuilder(args)
        .ConfigureServices((hostContext, services) =>
        {
            ConfigureServices(services);
            services.AddHostedService<SynchronizationWorker>(); // Or other parallel tasks
                });
    }
#endif

#if CACHEDATA
    internal class CacheData: StaticFunctions
    {
        static void Main(string[] args)
        {
            AnnounceStart();
            EnsureDirectoriesExist();
            try
            {
                LoggerSingleton.General.Info("Starting RemoteDesktopCleaner console app");
                Console.WriteLine("Starting RemoteDesktopCleaner console app");

                var serviceProvider = ConfigureServices();

                var cw = serviceProvider.GetRequiredService<CacheWorker>();

                Console.WriteLine("Starting caching");
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
            finally
            {
                AnnounceEnd();
            }
        }
    }
#endif

#if RESTOREDATA 
    internal class RestoreData: StaticFunctions
    {
        static void Main(string[] args)
        {
            AnnounceStart();
            EnsureDirectoriesExist();
            try
            {
                LoggerSingleton.General.Info("Starting RemoteDesktopCleaner console app");
                Console.WriteLine("Starting RemoteDesktopCleaner console app");

                var serviceProvider = ConfigureServices();

                var cw = serviceProvider.GetRequiredService<RestorationWorker>();

                Console.WriteLine("Starting restoration");
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
            finally
            {
                AnnounceEnd();
            }
        }
    }
#endif

#if REMOVEDATA
    internal class RemoveData: StaticFunctions
    {
        static void Main(string[] args)
        {
            AnnounceStart();
            EnsureDirectoriesExist();
            try
            {
                LoggerSingleton.General.Info("Starting RemoteDesktopCleaner console app");
                Console.WriteLine("Starting RemoteDesktopCleaner console app");

                var serviceProvider = ConfigureServices();

                var cw = serviceProvider.GetRequiredService<RemovalWorker>();

                Console.WriteLine("Starting removal");
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
            finally
            {
                AnnounceEnd();
            }
        }
    }
#endif

#if SERVERINITDEBUG || SERVERINIT
    internal class ServerInitialization: StaticFunctions
    {
        static void Main(string[] args)
        {
            AnnounceStart();
            EnsureDirectoriesExist();

            try
            {
                LoggerSingleton.General.Info("Starting RemoteDesktopCleaner console app");
                Console.WriteLine("Starting RemoteDesktopCleaner console app");

                var serviceProvider = ConfigureServices();

                var cw = serviceProvider.GetRequiredService<GatewayInitWorker>();

                Console.WriteLine("Starting Server Initialization");
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
            finally
            {
                AnnounceEnd();
            }
        }
    }
#endif


#if SYNCDBANDLGS || DEBUG || SYNCHRONIZEDBANDLGSDEBUG || LEVELLGSANDDB
    internal class SyncDBandLGs : StaticFunctions
    {
        static void Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                LoggerSingleton.General.Fatal($"Unhandled exception: {e.ExceptionObject}");
                Environment.FailFast($"Unhandled exception: {e.ExceptionObject}");
                Console.WriteLine($"Unhandled exception: {e.ExceptionObject}");
            };

            EnsureDirectoriesExist();
            try
            {
                LoggerSingleton.General.Info("Starting DB and LG Leveling console app");
                Console.WriteLine("Starting DB and LG Leveling console app");

                var serviceProvider = ConfigureServices();
                var cw = serviceProvider.GetRequiredService<LevelingWorker>();

                Console.WriteLine("Starting leveling.");
                cw.StartWorkAsync(new CancellationToken()).GetAwaiter().GetResult();

                Console.WriteLine("END");

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
