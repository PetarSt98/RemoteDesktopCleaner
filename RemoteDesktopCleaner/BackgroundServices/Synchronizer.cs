using System.Text.Json;
using SynchronizerLibrary.Data;
using SynchronizerLibrary.Loggers;
using SynchronizerLibrary.CommonServices;



namespace RemoteDesktopCleaner.BackgroundServices
{
    public class Synchronizer : ISynchronizer
    {
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
                LoggerSingleton.General.Info($"Starting the synchronization of '{serverName}' gateway.");
                LoggerSingleton.General.Info($"Awaiting getting gateway RAP/Policy names for '{serverName}'.");
                var taskGtRapNames = _gatewayRapSynchronizer.GetGatewaysRapNamesAsync(serverName);
                LoggerSingleton.General.Info($"Awaiting getting gateway Local Group names for '{serverName}'.");
                if (_gatewayLocalGroupSynchronizer.DownloadGatewayConfig(serverName))
                {
                    var cfgDiscrepancy = GetConfigDiscrepancy(serverName);
                    var changedLocalGroups = FilterChangedLocalGroups(cfgDiscrepancy.LocalGroups); 

                    var addedGroups = _gatewayLocalGroupSynchronizer.SyncLocalGroups(changedLocalGroups, serverName); 
                    var allGatewayGroups = GetAllGatewayGroupsAfterSynchronization(changedLocalGroups);

                    LoggerSingleton.General.Info($"Finished getting gateway RAP names for '{serverName}'.");

                    _gatewayRapSynchronizer.SynchronizeRaps(serverName, allGatewayGroups, changedLocalGroups.LocalGroupsToDelete.Where(lg => lg.Name.StartsWith("LG-")).Select(lg => lg.Name).ToList(), taskGtRapNames); 
                }
                LoggerSingleton.General.Info("Finished synchronization");
            }
            catch (Exception ex)
            {
                LoggerSingleton.General.Error(ex, $"Error while synchronizing gateway: '{serverName}'.");
            }
            Console.WriteLine($"Finished synchronization for gateway '{serverName}'.");
            LoggerSingleton.General.Info($"Finished synchronization for gateway '{serverName}'.");
        }

        private GatewayConfig GetConfigDiscrepancy(string serverName)
        {
            LoggerSingleton.General.Info("Started comparing Local Groups and members from database and server");
            GatewayConfig modelCfgValid = ReadValidConfigDbModel();
            GatewayConfig modelCfgInvalid = ReadInvalidConfigDbModel();
            GatewayConfig gatewayCfg = ReadGatewayConfigFromFile(serverName);
            return CompareWithModel(gatewayCfg, modelCfgValid, modelCfgInvalid);
        }

        public GatewayConfig ReadGatewayConfigFromFile(string serverName)
        {
            LoggerSingleton.General.Info($"Reading config for gateway: '{serverName}' from file.");
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
                var content = File.ReadAllText(path);
                LoggerSingleton.SynchronizedLocalGroups.Debug($"Reading local groups for '{serverName}' from file.");
                LoggerSingleton.General.Debug($"Reading local groups for '{serverName}' from file.");
                localGroups.AddRange(Newtonsoft.Json.JsonConvert.DeserializeObject<List<LocalGroup>>(content));
                return localGroups;
            }
            catch (Exception ex)
            {
                LoggerSingleton.General.Fatal($"{ex.ToString()} Error while reading local groups of server: '{serverName}'");
                throw;
            }
        }

        public GatewayConfig ReadValidConfigDbModel()
        {
            LoggerSingleton.General.Info("Getting valid config model.");
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

        public GatewayConfig ReadInvalidConfigDbModel()
        {
            LoggerSingleton.General.Info("Getting valid config model.");
            var raps = GetRaps();
            var localGroups = new List<LocalGroup>();
            var validRaps = raps.Where(IsRapInvalid);
            foreach (var rap in validRaps)
            {
                var owner = rap.login;
                var resources = rap.rap_resource.Where(IsResourceInvalid)
                    .Select(resource => $"{resource.resourceName}$").ToList();
                resources.Add(owner);
                var lg = new LocalGroup(rap.resourceGroupName, resources);
                localGroups.Add(lg);
            }
            var gatewayModel = new GatewayConfig("MODEL", localGroups);
            return gatewayModel;
        }

        public GatewayConfig CompareWithModel(GatewayConfig gatewayCfg, GatewayConfig modelCfgValid, GatewayConfig modelCfgInvalid)
        {
            LoggerSingleton.General.Debug($"Comparing gateway '{gatewayCfg.ServerName}' config to DB model.");
            var diff = new GatewayConfig(gatewayCfg.ServerName);
            var modelLgsValid = modelCfgValid.LocalGroups;
            diff.Add(CheckExistingAndObsoleteGroups(modelCfgInvalid, gatewayCfg));
            diff.Add(CheckForNewGroups(modelLgsValid, gatewayCfg));
            SaveToFile(diff);
            return diff;
        }

        private List<LocalGroup> CheckExistingAndObsoleteGroups(GatewayConfig modelCfgInvalid, GatewayConfig gatewayCfg)
        {
            var results = new List<LocalGroup>();
            foreach (var modelLocalGroup in modelCfgInvalid.LocalGroups)
            {
                LoggerSingleton.SynchronizedLocalGroups.Info($"Checkf if obsolete {modelLocalGroup.Name} exists in DB");
                LocalGroup lgDiff;
                if (IsInConfig(modelLocalGroup.Name, gatewayCfg))
                {
                    lgDiff = new LocalGroup(modelLocalGroup.Name, LocalGroupFlag.Delete);
                    lgDiff.Computers.AddRange(modelLocalGroup.Computers);
                    lgDiff.ComputersObj.AddRange(modelLocalGroup.Computers, Enumerable.Repeat(LocalGroupFlag.Delete, modelLocalGroup.Computers.Count).ToList());
                    lgDiff.Members.AddRange(modelLocalGroup.Members);
                    lgDiff.MembersObj.AddRange(modelLocalGroup.Members, Enumerable.Repeat(LocalGroupFlag.Delete, modelLocalGroup.Members.Count).ToList());
                    results.Add(lgDiff);
                }   
            }
            return results;
        }


        //private List<LocalGroup> CheckExistingAndObsoleteGroups2(GatewayConfig modelCfg, GatewayConfig gatewayCfg)
        //{
        //    var results = new List<LocalGroup>();
        //    foreach (var gtLocalGroup in gatewayCfg.LocalGroups)
        //    {
        //        LoggerSingleton.SynchronizedLocalGroups.Info($"Checkf if {gtLocalGroup.Name} exists in DB");
        //        LocalGroup lgDiff;
        //        if (IsInConfig(gtLocalGroup.Name, modelCfg))
        //        {
        //            LoggerSingleton.SynchronizedLocalGroups.Debug($"{gtLocalGroup.Name} exists in DB, check members and devices");
        //            lgDiff = new LocalGroup(gtLocalGroup.Name, LocalGroupFlag.CheckForUpdate);
        //            var modelLocalGroup = modelCfg.LocalGroups.First(lg => lg.Name == gtLocalGroup.Name);
        //            lgDiff.ComputersObj.AddRange(GetListDiscrepancyTest(modelLocalGroup.Computers, gtLocalGroup.Computers));
        //            lgDiff.MembersObj.AddRange(GetListDiscrepancyTest(modelLocalGroup.Members, gtLocalGroup.Members));
        //        }
        //        else
        //        {
        //            lgDiff = new LocalGroup(gtLocalGroup.Name, LocalGroupFlag.Delete);
        //            lgDiff.Computers.AddRange(gtLocalGroup.Computers);
        //            lgDiff.ComputersObj.AddRange(gtLocalGroup.Computers, Enumerable.Repeat(LocalGroupFlag.Delete, gtLocalGroup.Computers.Count).ToList());
        //            lgDiff.Members.AddRange(gtLocalGroup.Members);
        //            lgDiff.MembersObj.AddRange(gtLocalGroup.Members, Enumerable.Repeat(LocalGroupFlag.Delete, gtLocalGroup.Members.Count).ToList());
        //        }
        //        results.Add(lgDiff);
        //    }

        //    return results;
        //}

        private List<LocalGroup> CheckForNewGroups(List<LocalGroup> modelGroups, GatewayConfig gatewayCfg)
        {
            var result = new List<LocalGroup>();
            foreach (var modelLocalGroup in modelGroups)
            {
                if (IsInConfig(modelLocalGroup.Name, gatewayCfg)) continue;
                var lg = new LocalGroup(modelLocalGroup.Name, LocalGroupFlag.Add);
                lg.ComputersObj.AddRange(GetListDiscrepancyTest(modelLocalGroup.Computers, new List<string>()));
                lg.MembersObj.AddRange(GetListDiscrepancyTest(modelLocalGroup.Members, new List<string>()));
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
                LoggerSingleton.General.Warn($"{ex.ToString()} Failed saving gateway: '{diff.ServerName}' discrepancy config to file.");
            }
        }

        private LocalGroupsChanges FilterChangedLocalGroups(List<LocalGroup> allGroups)
        {
            var groupsToDelete = allGroups.Where(lg => lg.Flag == LocalGroupFlag.Delete).ToList();
            var groupsToAdd = allGroups.Where(lg => lg.Flag == LocalGroupFlag.Add).ToList();
            var changedContent = allGroups.Where(lg => lg.Flag == LocalGroupFlag.CheckForUpdate && lg.MembersObj.Flags.Any(content => content != LocalGroupFlag.None)).ToList();
            var groupsToSync = new LocalGroupsChanges();
            groupsToSync.LocalGroupsToDelete = groupsToDelete;
            groupsToSync.LocalGroupsToAdd = groupsToAdd;
            groupsToSync.LocalGroupsToUpdate = changedContent;
            return groupsToSync;
        }

        private List<string> GetAllGatewayGroupsAfterSynchronization(LocalGroupsChanges discrepancy)
        {
            var alreadyExistingGroups = discrepancy.LocalGroupsToUpdate
                                                    .Where(lg => lg.Name.StartsWith("LG-"))
                                                    .Select(lg => lg.Name).ToList();
            var addedGroups = discrepancy.LocalGroupsToAdd
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
                LoggerSingleton.General.Fatal($"Failed query. {ex}");
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

        private bool IsRapInvalid(rap rap)
        {
            return rap.toDelete;
        }

        private bool IsResourceInvalid(rap_resource resource)
        {
            return resource.toDelete || !resource.invalid.HasValue || resource.invalid.Value;
        }
    }
}
