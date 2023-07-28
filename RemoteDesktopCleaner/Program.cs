﻿using Microsoft.Extensions.DependencyInjection;
using RemoteDesktopCleaner.BackgroundServices;
using RemoteDesktopCleaner.Exceptions;
using SynchronizerLibrary.CommonServices;
using SynchronizerLibrary.Loggers;


namespace RemoteDesktopCleaner
{
    class Program
    {
        static void Main(string[] args)
        {
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

        private static IServiceProvider ConfigureServices()
        {
            LoggerSingleton.General.Info("Configuring services");
            Console.WriteLine("Configuring services");

            var services = new ServiceCollection();
            services.AddSingleton<IConfigValidator, ConfigValidator>();
            services.AddSingleton<IGatewayRapSynchronizer, GatewayRapSynchronizer>();
            services.AddSingleton<ISynchronizer, Synchronizer>();
            services.AddSingleton<IGatewayLocalGroupSynchronizer, GatewayLocalGroupSynchronizer>();
            services.AddSingleton<SynchronizationWorker>();

            var serviceProvider = services.BuildServiceProvider();

            LoggerSingleton.General.Info("Finished configuring services");
            Console.WriteLine("Finished configuring services");

            return serviceProvider;
        }
    }
}
