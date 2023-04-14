using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Owin.Hosting;
using NLog;
using Unity; // deprecated, change
using RemoteDesktopCleaner.BackgroundServices;
//using RemoteDesktopAccessCleaner.DataAccessLayer;
//using RemoteDesktopAccessCleaner.DataAccessLayer.MsSql;
//using RemoteDesktopAccessCleaner.DataAccessLayer.MySql;
//using RemoteDesktopAccessCleaner.Models;
//using RemoteDesktopAccessCleaner.Models.EF;
//using RemoteDesktopAccessCleaner.Modules.ActiveDirectory;
//using RemoteDesktopAccessCleaner.Modules.ConfigProvider;
//using RemoteDesktopAccessCleaner.Report;

namespace RemoteDesktopCleaner
{
    class Program
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        static void Main(string[] args)
        {
            WebApi();
            //ConsoleApp();
        }

        private static void WebApi()
        {
            string baseAddress = "http://localhost:5004/";
            Logger.Info($"Starting on address: {baseAddress}");
            using (WebApp.Start<Startup>(baseAddress))
            { // settingup the webapp
                var cw = Startup.Container.Resolve<SynchronizationWorker>(); // containter creates instance of SynchronizationWorker
                cw.StartAsync(new CancellationToken());
                Console.ReadKey();
            }
        }

        //private static void ConsoleApp()
        //{
        //    //var ad = new AdDataRetriever();
        //    //var acc = "aabdulha";
        //    ////if (ad.IsComputerInActiveDirectory(acc))
        //    ////    Console.WriteLine($"{acc} computer is in AD.");
        //    //if (ad.IsUserInActiveDirectory(acc))
        //    //    Console.WriteLine($"{acc} user is in AD.");
        //    //if (ad.IsGroupInActiveDirectory(acc))
        //    //    Console.WriteLine($"{acc} group is in AD.");
        //    //if(ad.IsUserInAD(acc))
        //    //    Console.WriteLine($"'acc' found with 2nd v");
        //    //Console.WriteLine("ends");
        //    CopyDataToNewDb();
        //    Console.ReadKey();
        //}

        //private static List<string> RapNamesForDbModel(IReporter reporter)
        //{
        //    var rapNames = new List<string>();
        //    var x = new MySqlProxy();
        //    var configReader = new ConfigReader(x, reporter);
        //    var model = configReader.ReadValidConfigDbModel();
        //    foreach (var lg in model.LocalGroups)
        //        rapNames.Add(LgNameToRapName(lg.Name));
        //    return rapNames;
        //}

        //private static string LgNameToRapName(string lgName)
        //{
        //    return lgName.Replace("LG-", "RAP_");
        //}

        //private static void CopyDataToNewDb()
        //{
        //    var sqlConnector = new MsSqlProxy();
        //    var mysqlProxy = new MySqlProxy();
        //    var adQueryWorker = new AdDataRetriever();
        //    var raps = sqlConnector.GetAllRaps();
        //    int i = 1;
        //    var deadUsers = new HashSet<string>();
        //    var deadComputers = new HashSet<string>();
        //    Console.WriteLine($"Found {raps.Count} raps in total");
        //    foreach (var rap in raps)
        //    {
        //        Console.Write($"\r{i}/{raps.Count} - {100 * i / raps.Count}%");
        //        if (!adQueryWorker.IsUserInActiveDirectory(rap.Login) && !adQueryWorker.IsGroupInActiveDirectory(rap.Login))
        //        {
        //            deadUsers.Add(rap.Login);
        //            rap.ToDelete = true;
        //        }
        //        var rapResources = sqlConnector.GetResources(rap);
        //        rap.Resources.AddRange(rapResources);
        //        var rapDeadComputers = from resource in rapResources
        //                               where !adQueryWorker.IsComputerInActiveDirectory(resource.ResourceName)
        //                               select resource.ResourceName;
        //        foreach (var deadComputerName in rapDeadComputers)
        //        {
        //            deadComputers.Add(deadComputerName);
        //            rapResources.Where(r => r.ResourceName == deadComputerName)
        //                .ToList().ForEach(a => a.ToDelete = true);
        //        }
        //        if (rap.Resources.All(x => x.ToDelete))
        //            rap.ToDelete = true;
        //        mysqlProxy.Save(rap);
        //        i++;
        //    }
        //    Console.WriteLine($"\n'Dead' users/groups: {deadUsers.Count}");
        //    Console.WriteLine($"Dead computers: {deadComputers.Count}");
        //}

    }
}
