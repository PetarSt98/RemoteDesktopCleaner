using System.Text.Json;
using SynchronizerLibrary.Data;
using SynchronizerLibrary.Loggers;
using SynchronizerLibrary.CommonServices;
using SynchronizerLibrary.CommonServices.LocalGroups;
using System.Data.Entity;


namespace RemoteDesktopCleaner.BackgroundServices
{
    public class ServerInit : IServerInit
    {
        private readonly IGatewayRapSynchronizer _gatewayRapSynchronizer;
        private readonly IGatewayLocalGroupSynchronizer _gatewayLocalGroupSynchronizer;

        public ServerInit(IGatewayRapSynchronizer gatewayRapSynchronizer, IGatewayLocalGroupSynchronizer gatewayLocalGroupSynchronizer)
        {
            _gatewayLocalGroupSynchronizer = gatewayLocalGroupSynchronizer;
            _gatewayRapSynchronizer = gatewayRapSynchronizer;
        }

        public async Task SynchronizeAsync(string serverName)
        {
            try
            {
                LoggerSingleton.General.Info($"Starting the synchronization of '{serverName}' gateway.");
                LoggerSingleton.General.Info($"Awaiting getting gateway RAP/Policy names for '{serverName}'.");
                Console.WriteLine($"Get policies on {serverName}");
                LoggerSingleton.General.Info($"Awaiting getting gateway Local Group names for '{serverName}'.");

                
                GatewayConfig gatewayCfg = await GetAllUsersLGs(serverName);

                gatewayCfg = updateFlags(gatewayCfg);

                var changedLocalGroups = Synchronizer.FilterChangedLocalGroups(gatewayCfg.LocalGroups);

                var addedGroups = await _gatewayLocalGroupSynchronizer.SyncLocalGroups(changedLocalGroups, serverName, "serverInit");
                var allGatewayGroups = Synchronizer.GetAllGatewayGroupsAfterSynchronization(changedLocalGroups);

                LoggerSingleton.General.Info($"Finished getting gateway RAP names for '{serverName}'.");

                await _gatewayRapSynchronizer.SynchronizeRaps(serverName, allGatewayGroups, changedLocalGroups.LocalGroupsToDelete.Where(lg => lg.Name.StartsWith("LG-")).Select(lg => lg.Name).ToList(), new List<string>());
                
                LoggerSingleton.General.Info("Finished synchronization");
            }
            catch (Exception ex)
            {
                LoggerSingleton.General.Error(ex, $"Error while synchronizing gateway: '{serverName}'.");
            }
            Console.WriteLine($"Finished synchronization for gateway '{serverName}'.");
            LoggerSingleton.General.Info($"Finished synchronization for gateway '{serverName}'.");
        }

        private async Task<GatewayConfig> GetAllUsersLGs(string serverName)
        {
            LoggerSingleton.General.Info("Getting valid config model.");
            var raps = GetRaps();
            var goodRAPs = raps
                        .Where(r => !r.toDelete)
                        .ToList();

            var localGroups = new List<LocalGroup>();

            foreach (var rap in goodRAPs)
            {
                var owner = rap.login;
                var resources = rap.rap_resource.Where(r => !r.toDelete)
                    .Select(resource => $"{resource.resourceName}$").ToList();
                resources.Add(owner);
                var lg = new LocalGroup(rap.resourceGroupName, resources);
                localGroups.Add(lg);
            }
            var gatewayModel = new GatewayConfig("MODEL", localGroups);
            var result = new List<LocalGroup>();
            foreach (var modelLocalGroup in gatewayModel.LocalGroups)
            {
                var lg = new LocalGroup(modelLocalGroup.Name, LocalGroupFlag.Add);
                lg.ComputersObj.AddRange(GetListDiscrepancyTest(modelLocalGroup.Computers, "Add"));
                lg.MembersObj.AddRange(GetListDiscrepancyTest(modelLocalGroup.Members, "Add"));
                result.Add(lg);
            }
            var diff = new GatewayConfig(serverName);
            diff.Add(result);
            return diff;
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

        private LocalGroupContent GetListDiscrepancyTest(ICollection<string> modelList, string addOrDeleteFlag)
        {

            var flags = new List<LocalGroupFlag>();
            var names = new List<string>();
            if (addOrDeleteFlag == "None")
            {
                flags.AddRange(from el in modelList select LocalGroupFlag.None);
            }
            else if (addOrDeleteFlag == "Add")
                flags.AddRange(from el in modelList select LocalGroupFlag.Add);
            else if (addOrDeleteFlag == "Delete")
                flags.AddRange(from el in modelList select LocalGroupFlag.Delete);
            else
            {
                throw new Exception("Unknown operator!");
            }
            names.AddRange(from el in modelList select el.ToLower());

            return new LocalGroupContent(names, flags);
        }

        public async Task<IEnumerable<rap>> GetRapsAsync()
        {
            var results = new List<rap>();
            try
            {
                using (var db = new RapContext())
                {
                    results.AddRange(await db.raps.Include("rap_resource").ToListAsync());
                }
            }
            catch (Exception ex)
            {
                LoggerSingleton.General.Fatal($"Failed query. {ex}");
                Console.WriteLine("Failed query.");
            }

            return results;
        }

        private bool IsResourceValid(rap_resource resource)
        {
            return !resource.toDelete && resource.invalid.HasValue && !resource.invalid.Value;
            //return !resource.toDelete && resource.invalid.HasValue && !resource.invalid.Value;
        }

        private GatewayConfig updateFlags(GatewayConfig cfg)
        {
            for (int i = 0; i < cfg.LocalGroups.Count; i++)
            {
                cfg.LocalGroups[i].Flag = LocalGroupFlag.Add;

                for (int j = 0; j < cfg.LocalGroups[i].ComputersObj.Flags.Count; j++)
                {
                    cfg.LocalGroups[i].ComputersObj.Flags[j] = LocalGroupFlag.Add;
                }
                for (int j = 0; j < cfg.LocalGroups[i].MembersObj.Flags.Count; j++)
                {
                    cfg.LocalGroups[i].MembersObj.Flags[j] = LocalGroupFlag.Add;
                }
            }
            return cfg;
        }
    }
}
