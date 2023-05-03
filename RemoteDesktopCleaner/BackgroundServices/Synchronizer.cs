using System.DirectoryServices;
using Microsoft.Extensions.Hosting;
using NLog;
using System.Text.Json;
using RemoteDesktopCleaner.Data;


namespace RemoteDesktopCleaner.BackgroundServices
{
    public class Synchronizer : ISynchronizer
    {
        private static readonly Logger LoggerGeneral = LogManager.GetLogger("logfileGeneral");
        private static readonly Logger LoggerSynchronizedLocalGroups = LogManager.GetLogger("logfileSynchronizedLocalGroups");
        private static readonly Logger LoggerSynchronizedRAPs = LogManager.GetLogger("logfileSynchronizedRAPs");
        private readonly IGatewayRapSynchronizer _gatewayRapSynchronizer;
        private readonly IGatewayLocalGroupSynchronizer _gatewayLocalGroupSynchronizer;

        public Synchronizer(IGatewayRapSynchronizer gatewayRapSynchronizer, IGatewayLocalGroupSynchronizer gatewayLocalGroupSynchronizer)
        {
            _gatewayLocalGroupSynchronizer = gatewayLocalGroupSynchronizer;
            _gatewayRapSynchronizer = gatewayRapSynchronizer;
        }

        public async void SynchronizeAsync(string serverName)
        {
            try
            {
                LoggerGeneral.Info($"Starting the synchronization of '{serverName}' gateway.");
                var taskGtRapNames = _gatewayRapSynchronizer.GetGatewaysRapNamesAsync(serverName); // get all raps from CERNGT01
                if (_gatewayLocalGroupSynchronizer.DownloadGatewayConfig(serverName))
                { // ako uspesno loadujes local group names i napravis LG objekte // ubaci da baci gresku ako je prazno
                    var cfgDiscrepancy = GetConfigDiscrepancy(serverName); // ovo je diff izmedju MODEL-a i CERNGT01, tj diff kojim treba CERNGT01 da se updatuje
                    var changedLocalGroups = FilterChangedLocalGroups(cfgDiscrepancy.LocalGroups); // devide diff into groups: group for adding LGs, group for deleting LGs, group for changing LGs (adding/deleting computers/members)
                    //InitReport(serverName, changedLocalGroups); // write a report
                    var addedGroups = _gatewayLocalGroupSynchronizer.SyncLocalGroups(changedLocalGroups, serverName); // add/remove/update LGs with cfgDiscrepancy/changedLocalGroups, return added groups
                    var allGatewayGroups = GetAllGatewayGroupsAfterSynchronization(cfgDiscrepancy, addedGroups); // get LGs which are updated with members and computers (not removed or added) and append with new added groups, so we have now current active groups
                    LoggerGeneral.Info($"Awaiting getting gateway RAP names for '{serverName}'.");
                    //var gatewayRapNames = await taskGtRapNames; // update server CERNGT01, get all raps from CERNGT01
                    LoggerGeneral.Info($"Finished getting gateway RAP names for '{serverName}'.");
                    _gatewayRapSynchronizer.SynchronizeRaps(serverName, allGatewayGroups, taskGtRapNames); // UPDATE SERVER CERNGT01, gatewayRapNames are raps from server CERNGT01
                }
                //_reporter.Finish(serverName); // create log file and send it to email
            }
            catch (Exception ex)
            {
                LoggerGeneral.Error(ex, $"Error while synchronizing gateway: '{serverName}'.");
                //_reporter.Finish(serverName);
            }
            Console.WriteLine($"Finished synchronization for gateway '{serverName}'.");
        }

        private GatewayConfig GetConfigDiscrepancy(string serverName)
        {
            LoggerGeneral.Info("Started comparing Local Groups and members from database and server");
            GatewayConfig modelCfg = ReadValidConfigDbModel();
            GatewayConfig gatewayCfg = ReadGatewayConfigFromFile(serverName);
            return CompareWithModel(gatewayCfg, modelCfg);
        }

        public GatewayConfig ReadGatewayConfigFromFile(string serverName)
        {
            LoggerGeneral.Info($"Reading config for gateway: '{serverName}' from file.");
            var lgGroups = GetGatewayLocalGroupsFromFile(serverName);
            var cfg = new GatewayConfig(serverName, lgGroups);
            return cfg;
        }

        private List<LocalGroup> GetGatewayLocalGroupsFromFile(string serverName)
        {
            try
            {
                var localGroups = new List<LocalGroup>();
                var dstDir = AppConfig.GetInfoDir();
                var path = dstDir + @"\" + serverName + ".json";
                var content = File.ReadAllText(path); // ucitamo iz jsona koji smo napravili, ucitamo LG-ove sa CERNGT01
                LoggerSynchronizedLocalGroups.Debug($"Reading local groups for '{serverName}' from file.");
                LoggerGeneral.Debug($"Reading local groups for '{serverName}' from file.");
                localGroups.AddRange(Newtonsoft.Json.JsonConvert.DeserializeObject<List<LocalGroup>>(content)); // deserialize mora kad se cita iz jsona
                return localGroups;
            }
            catch (Exception ex)
            {
                LoggerGeneral.Fatal($"{ex.ToString()} Error while reading local groups of server: '{serverName}'");
                throw;
            }
        }

        public GatewayConfig ReadValidConfigDbModel()
        {
            LoggerGeneral.Info("Getting valid config model.");
            var raps = GetRaps();
            var localGroups = new List<LocalGroup>();
            var validRaps = raps.Where(IsRapValid);
            foreach (var rap in validRaps)
            {
                var owner = rap.login;
                var resources = rap.rap_resource.Where(IsResourceValid)
                    .Select(resource => $"{resource.resourceName}$").ToList();
                resources.Add(owner);
                var lg = new LocalGroup(rap.resourceGroupName, resources);
                localGroups.Add(lg);
            }
            var gatewayModel = new GatewayConfig("MODEL", localGroups);
            return gatewayModel;
        }

        public GatewayConfig CompareWithModel(GatewayConfig gatewayCfg, GatewayConfig modelCfg)
        {
            LoggerGeneral.Debug($"Comparing gateway '{gatewayCfg.ServerName}' config to DB model.");
            //_reporter.Info(gatewayCfg.ServerName, $"Comparing gateway '{gatewayCfg.ServerName}' config to DB model.");
            var diff = new GatewayConfig(gatewayCfg.ServerName); // napravimo novi objekat LG koji ce da nosi razliku izmedju MODEL-a i CERNGT01
            var modelLgs = modelCfg.LocalGroups; // izvuci lokalne grupe iz objekta
            diff.Add(CheckExistingAndObsoleteGroups(modelCfg, gatewayCfg)); // nasli smo LG koje se razlikuju izmedju MODEL-a i CERNGT01, CERNGT)! nam je cilj da bude isti kao MODEL. LG=(group name, computer name, member name), znaci ako MODEL ne sadrzi LG iz CERNGT01 brisi LG zajendo sa svim memberima i kompovima, ako sadrzi onda proveri da li treba da se doda ili brise komp ili member
            diff.Add(CheckForNewGroups(modelLgs, gatewayCfg));
            SaveToFile(diff); // sacuvaj razliku u json, ovo mi se ne svidja, nepotrebno
            return diff;
        }

        private List<LocalGroup> CheckExistingAndObsoleteGroups(GatewayConfig modelCfg, GatewayConfig gatewayCfg)
        {
            var results = new List<LocalGroup>();
            foreach (var gtLocalGroup in gatewayCfg.LocalGroups)
            {
                LoggerSynchronizedLocalGroups.Info($"Checkf if {gtLocalGroup.Name} exists in DB");
                LocalGroup lgDiff;
                if (IsInConfig(gtLocalGroup.Name, modelCfg))
                {
                    LoggerSynchronizedLocalGroups.Debug($"{gtLocalGroup.Name} exists in DB, check members and devices");
                    lgDiff = new LocalGroup(gtLocalGroup.Name, LocalGroupFlag.CheckForUpdate);
                    var modelLocalGroup = modelCfg.LocalGroups.First(lg => lg.Name == gtLocalGroup.Name);
                    //lgDiff.Computers.AddRange(GetListDiscrepancy(modelLocalGroup.Computers, gtLocalGroup.Computers));
                    lgDiff.ComputersObj.AddRange(GetListDiscrepancyTest(modelLocalGroup.Computers, gtLocalGroup.Computers));
                    //lgDiff.Members.AddRange(GetListDiscrepancy(modelLocalGroup.Members, gtLocalGroup.Members));
                    lgDiff.MembersObj.AddRange(GetListDiscrepancyTest(modelLocalGroup.Members, gtLocalGroup.Members));
                }
                else
                {
                    //lgDiff = new LocalGroup($"-{gtLocalGroup.Name}");
                    lgDiff = new LocalGroup(gtLocalGroup.Name, LocalGroupFlag.Delete);
                    lgDiff.Computers.AddRange(gtLocalGroup.Computers);
                    lgDiff.ComputersObj.AddRange(gtLocalGroup.Computers, Enumerable.Repeat(LocalGroupFlag.Delete, gtLocalGroup.Computers.Count).ToList());
                    lgDiff.Members.AddRange(gtLocalGroup.Members);
                    lgDiff.MembersObj.AddRange(gtLocalGroup.Members, Enumerable.Repeat(LocalGroupFlag.Delete, gtLocalGroup.Members.Count).ToList());
                }
                results.Add(lgDiff);
            }

            return results;
        }

        private List<LocalGroup> CheckForNewGroups(List<LocalGroup> modelGroups, GatewayConfig gatewayCfg)
        {
            var result = new List<LocalGroup>();
            foreach (var modelLocalGroup in modelGroups)
            {
                if (IsInConfig(modelLocalGroup.Name, gatewayCfg)) continue;
                //var lg = new LocalGroup($"+{modelLocalGroup.Name}");
                var lg = new LocalGroup(modelLocalGroup.Name, LocalGroupFlag.Add);
                lg.ComputersObj.AddRange(GetListDiscrepancyTest(modelLocalGroup.Computers, new List<string>()));
                lg.MembersObj.AddRange(GetListDiscrepancyTest(modelLocalGroup.Members, new List<string>()));

                //lg.Computers.AddRange(GetListDiscrepancy(modelLocalGroup.Computers, new List<string>()));
                //lg.Members.AddRange(GetListDiscrepancy(modelLocalGroup.Members, new List<string>()));
                result.Add(lg);
            }

            return result;
        }

        private LocalGroupContent GetListDiscrepancyTest(ICollection<string> modelList, ICollection<string> otherList)
        {
            var flags = otherList
                .Where(el => !el.StartsWith(Constants.OrphanedSid))
                .Select(el => el.ToLower())
                .Select(el => IsInListIgnoreCase(el, modelList) ? LocalGroupFlag.None : LocalGroupFlag.Delete).ToList();
            var names = otherList
                .Where(el => !el.StartsWith(Constants.OrphanedSid))
                .Select(el => el.ToLower()).ToList();
            flags.AddRange(from el in modelList where !IsInListIgnoreCase(el, otherList) select LocalGroupFlag.Add);
            names.AddRange(from el in modelList where !IsInListIgnoreCase(el, otherList) select el.ToLower());
            flags.AddRange(otherList.Where(el => el.StartsWith(Constants.OrphanedSid)).Select(el => LocalGroupFlag.OrphanedSid));

            names.AddRange(otherList.Where(el => el.StartsWith(Constants.OrphanedSid)));
            return new LocalGroupContent(names, flags);
        }

        private static bool IsInConfig(string groupName, GatewayConfig config)
        {
            return config.LocalGroups.Select(lg => lg.Name).Contains(groupName);
        }

        private static bool IsInListIgnoreCase(string el, ICollection<string> list)
        {
            return list.Any(s => s.Equals(el, StringComparison.OrdinalIgnoreCase));
        }

        private void SaveToFile(GatewayConfig diff)
        {
            try
            {
                var path = AppConfig.GetInfoDir() + @"\" + diff.ServerName + "-diff.json";
                File.WriteAllText(path, JsonSerializer.Serialize(diff));
            }
            catch (Exception ex)
            {
                LoggerGeneral.Warn($"{ex.ToString()} Failed saving gateway: '{diff.ServerName}' discrepancy config to file.");
            }
        }

        private LocalGroupsChanges FilterChangedLocalGroups(List<LocalGroup> allGroups)
        {
            var groupsToDelete = allGroups.Where(lg => lg.Flag == LocalGroupFlag.Delete).ToList(); // LG to be deleted
            var groupsToAdd = allGroups.Where(lg => lg.Flag == LocalGroupFlag.Add).ToList();
            var changedContent = allGroups.Where(lg => lg.Flag == LocalGroupFlag.CheckForUpdate && lg.MembersObj.Flags.Any(content => content != LocalGroupFlag.None)).ToList(); // LG whose computers or members will be added/deleted
            var groupsToSync = new LocalGroupsChanges();
            groupsToSync.LocalGroupsToDelete = groupsToDelete;
            groupsToSync.LocalGroupsToAdd = groupsToAdd;
            groupsToSync.LocalGroupsToUpdate = changedContent;
            //var groupsToSync = groupsToDelete.Concat(groupsToAdd).Concat(changedContent).ToList(); // concatenate it, suvisno
            return groupsToSync;
        }

        private List<string> GetAllGatewayGroupsAfterSynchronization(GatewayConfig discrepancy, List<string> addedGroups)
        {
            var alreadyExistingGroups = discrepancy.LocalGroups
                                                    .Where(lg => lg.Name.StartsWith("LG-"))
                                                    .Select(lg => lg.Name).ToList();
            return alreadyExistingGroups.Concat(addedGroups).ToList();
        }

        public IEnumerable<rap> GetRaps()
        {
            var results = new List<rap>();
            try
            {
                using (var db = new RapContext())
                {
                    results.AddRange(db.raps.Include("rap_resource").ToList());
                }
            }
            catch (Exception ex)
            {
                LoggerGeneral.Fatal($"Failed query. {ex}");
                Console.WriteLine("Failed query.");
            }

            return results;
        }

        private bool IsRapValid(rap rap)
        {
            return !rap.toDelete;
        }

        private bool IsResourceValid(rap_resource resource)
        {
            return !resource.toDelete && resource.invalid.HasValue && !resource.invalid.Value;
        }
    }
}
