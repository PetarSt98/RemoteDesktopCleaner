using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using RemoteDesktopCleaner.BackgroundServices;
//using RemoteDesktopAccessCleaner.Models;
//using RemoteDesktopAccessCleaner.Modules.ConfigComparison;
//using RemoteDesktopAccessCleaner.Modules.ConfigProvider;
//using RemoteDesktopAccessCleaner.Modules.Gateway;
//using RemoteDesktopAccessCleaner.Modules.Gateway.GroupManagement;
//using RemoteDesktopAccessCleaner.Modules.ServiceCommunicator;
//using RemoteDesktopAccessCleaner.Report;

namespace RemoteDesktopCleaner.BackgroundServices
{
    public class Synchronizer : ISynchronizer
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        //private readonly IGatewayScanner _gatewayScanner;
        //private readonly IConfigComparer _configComparer;
        //private readonly IConfigReader _configReader;
        //private readonly IGroupSynchronizer _groupSynchronizer;
        //private readonly IRapSynchronizer _rapSynchronizer;
        //private readonly IReporter _reporter;
        //private readonly IServiceCommunicator _serviceCommunicator;

        //public Synchronizer(igatewayscanner gatewayscanner, iconfigreader configreader, iconfigcomparer configcomparer, ireporter reporter, igroupsynchronizer groupsynchronizer, irapsynchronizer rapsynchronizer, iservicecommunicator servicecommunicator)
        //{
        //    _gatewayscanner = gatewayscanner;
        //    _configreader = configreader;
        //    _configcomparer = configcomparer;
        //    _reporter = reporter;
        //    _groupsynchronizer = groupsynchronizer;
        //    _rapsynchronizer = rapsynchronizer;
        //    _servicecommunicator = servicecommunicator;
        //}

        public Synchronizer()
        {

        }

        //public async void SynchronizeAsync(string serverName)
        //{
        //    try
        //    {
        //        _reporter.Start(serverName); // pravi se loger
        //        Logger.Debug($"Starting the synchronization of '{serverName}' gateway.");
        //        var taskGtRapNames = GetGatewaysRapNamesAsync(serverName); // get all raps from CERNGT01
        //        if (_gatewayScanner.DownloadGatewayConfig(serverName))
        //        { // ako uspesno loadujes local group names i napravis LG objekte // ubaci da baci gresku ako je prazno
        //            var cfgDiscrepancy = GetConfigDiscrepancy(serverName); // ovo je diff izmedju MODEL-a i CERNGT01, tj diff kojim treba CERNGT01 da se updatuje
        //            var changedLocalGroups = FilterChangedLocalGroups(cfgDiscrepancy.LocalGroups); // devide diff into groups: group for adding LGs, group for deleting LGs, group for changing LGs (adding/deleting computers/members)
        //            InitReport(serverName, changedLocalGroups); // write a report
        //            var addedGroups = _groupSynchronizer.SyncLocalGroups(changedLocalGroups, serverName); // add/remove/update LGs with cfgDiscrepancy/changedLocalGroups, return added groups
        //            var allGatewayGroups = GetAllGatewayGroupsAfterSynchronization(cfgDiscrepancy, addedGroups); // get LGs which are updated with members and computers (not removed or added) and append with new added groups, so we have now current active groups
        //            Logger.Info($"Awaiting getting gateway RAP names for '{serverName}'.");
        //            var gatewayRapNames = await taskGtRapNames; // update server CERNGT01, get all raps from CERNGT01
        //            Logger.Info($"Finished getting gateway RAP names for '{serverName}'.");
        //            _rapSynchronizer.SynchronizeRaps(serverName, allGatewayGroups, gatewayRapNames); // UPDATE SERVER CERNGT01, gatewayRapNames are raps from server CERNGT01
        //        }
        //        _reporter.Finish(serverName); // create log file and send it to email
        //    }
        //    catch (Exception ex)
        //    {
        //        Logger.Error(ex, $"Error while synchronizing gateway: '{serverName}'.");
        //        _reporter.Finish(serverName);
        //    }
        //    Console.WriteLine($"Finished synchronization for gateway '{serverName}'.");
        //}

        //private GatewayConfig GetConfigDiscrepancy(string serverName)
        //{
        //    GatewayConfig modelCfg = _configReader.ReadValidConfigDbModel(); // ovo su objekti LG napravljeni bazom podataka koju smo obradili, invalid podatke izbacujemo (toDelete==1), ovo nosi ime MODEL (promeni u DATABASE)
        //    GatewayConfig gatewayCfg = _configReader.ReadGatewayConfigFromFile(serverName); // ovo su LG objekti ucitani sa gatewaya, ovo nosi ime po serveru CERNGT01
        //    return _configComparer.CompareWithModel(gatewayCfg, modelCfg);
        //}

        //private Task<List<string>> GetGatewaysRapNamesAsync(string serverName)
        //{
        //    Logger.Info($"Getting RAP names from gateway '{serverName}'.");
        //    try
        //    {
        //        return _serviceCommunicator.GetGatewayRapNames(serverName); // get all raps from CERNGT01 server
        //    }
        //    catch (Exception ex)
        //    {
        //        Logger.Error(ex, $"Failed getting RAP names from gateway '{serverName}'.");
        //        return Task.Run(() => new List<string>());
        //    }
        //}

        //private void InitReport(string serverName, List<LocalGroup> changedLocalGroups)
        //{
        //    int shouldAdd = changedLocalGroups.Count(lg => lg.Name.StartsWith("+"));
        //    int shouldDelete = changedLocalGroups.Count(lg => lg.Name.StartsWith("-"));
        //    int shouldSync = changedLocalGroups.Count(lg => lg.Name.StartsWith("LG-"));
        //    _reporter.SetShouldAddGroups(serverName, shouldAdd);
        //    _reporter.SetShouldSynchronizeGroups(serverName, shouldSync);
        //    _reporter.SetShouldDeleteGroups(serverName, shouldDelete);
        //}

        //private List<LocalGroup> FilterChangedLocalGroups(List<LocalGroup> allGroups)
        //{
        //    var groupsToDelete = allGroups.Where(lg => lg.Name.StartsWith("-")).ToList(); // LG to be deleted
        //    var groupsToAdd = allGroups.Where(lg => lg.Name.StartsWith("+")).ToList(); // LG to be added
        //    var changedContent = allGroups.Where(lg => lg.Name.StartsWith("LG-") && lg.Content.Any(content =>
        //        content.StartsWith("S-1-5") || content.StartsWith("+") || content.StartsWith("-"))).ToList(); // LG whose computers or members will be added/deleted
        //    var groupsToSync = groupsToDelete.Concat(groupsToAdd).Concat(changedContent).ToList(); // concatenate it, suvisno
        //    return groupsToSync;
        //}

        //private List<string> GetAllGatewayGroupsAfterSynchronization(GatewayConfig discrepancy, List<string> addedGroups)
        //{
        //    var alreadyExistingGroups = discrepancy.LocalGroups
        //                                            .Where(lg => lg.Name.StartsWith("LG-"))
        //                                            .Select(lg => lg.Name).ToList();
        //    return alreadyExistingGroups.Concat(addedGroups).ToList();
        //}
        //}
    }
}
