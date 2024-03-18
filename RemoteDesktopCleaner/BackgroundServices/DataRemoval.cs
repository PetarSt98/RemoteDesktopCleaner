using System.Text.Json;
using SynchronizerLibrary.Data;
using SynchronizerLibrary.Loggers;
using SynchronizerLibrary.CommonServices;
using SynchronizerLibrary.CommonServices.LocalGroups;


namespace RemoteDesktopCleaner.BackgroundServices
{
    public class DataRemoval : IDataRemoval
    {
        private readonly IGatewayRapSynchronizer _gatewayRapSynchronizer;
        private readonly IGatewayLocalGroupSynchronizer _gatewayLocalGroupSynchronizer;

        public DataRemoval(IGatewayRapSynchronizer gatewayRapSynchronizer, IGatewayLocalGroupSynchronizer gatewayLocalGroupSynchronizer)
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
                var taskGtRapNames = await _gatewayRapSynchronizer.GetGatewaysRapNamesAsync(serverName, false);
                LoggerSingleton.General.Info($"Awaiting getting gateway Local Group names for '{serverName}'.");
                if (await _gatewayLocalGroupSynchronizer.DownloadGatewayConfig(serverName, false))
                {
                    GatewayConfig gatewayCfg = await GetLocalGroupsToDelete(serverName);

                    var changedLocalGroups = Synchronizer.FilterChangedLocalGroups(gatewayCfg.LocalGroups);

                    var addedGroups = await _gatewayLocalGroupSynchronizer.SyncLocalGroups(changedLocalGroups, serverName);
                    var allGatewayGroups = Synchronizer.GetAllGatewayGroupsAfterSynchronization(changedLocalGroups);

                    LoggerSingleton.General.Info($"Finished getting gateway RAP names for '{serverName}'.");

                    await _gatewayRapSynchronizer.SynchronizeRaps(serverName, allGatewayGroups, changedLocalGroups.LocalGroupsToDelete.Where(lg => lg.Name.StartsWith("LG-")).Select(lg => lg.Name).ToList(), taskGtRapNames);
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

        public async static Task<GatewayConfig> GetLocalGroupsToDelete(string serverName)
        {
            LoggerSingleton.General.Info($"Reading config for gateway: '{serverName}' from file.");
            var lgGroupsCurrent = Synchronizer.GetGatewayLocalGroupsFromFile(serverName);
            var lgGroupsOther = Synchronizer.GetGatewayLocalGroupsFromFile(AppConfig.GetRemovalGateway());

            var localGroups = new List<LocalGroup>();

            localGroups.AddRange(GetObsoleteLocalGroups(lgGroupsCurrent, lgGroupsOther));
            localGroups.AddRange(GetOvelapLocalGroups(lgGroupsCurrent, lgGroupsOther));
            localGroups.AddRange(GetMissingLocalGroups(lgGroupsCurrent, lgGroupsOther));

            var cfg = new GatewayConfig(serverName, localGroups);
            return cfg;
        }

        private static List<LocalGroup> GetObsoleteLocalGroups(List<LocalGroup> lgGroupsCurrent, List<LocalGroup> lgGroupsOther)
        {
            var localGroupsObsolete = new List<LocalGroup>();

            localGroupsObsolete.AddRange(lgGroupsCurrent.Where(cg => !lgGroupsOther.Any(og => og.Name == cg.Name)));

            for (int i = 0; i < localGroupsObsolete.Count; i++)
            {
                localGroupsObsolete[i].Flag = LocalGroupFlag.Delete;

                for (int j = 0; j < localGroupsObsolete[i].ComputersObj.Flags.Count; j++)
                {
                    localGroupsObsolete[i].ComputersObj.Flags[j] = LocalGroupFlag.Delete;
                }
                for (int j = 0; j < localGroupsObsolete[i].MembersObj.Flags.Count; j++)
                {
                    localGroupsObsolete[i].MembersObj.Flags[j] = LocalGroupFlag.Delete;
                }
            }

            return localGroupsObsolete;
        }

        private static List<LocalGroup> GetMissingLocalGroups(List<LocalGroup> lgGroupsCurrent, List<LocalGroup> lgGroupsOther)
        {
            var localGroupsMissing = new List<LocalGroup>();

            localGroupsMissing.AddRange(lgGroupsOther.Where(cg => !lgGroupsCurrent.Any(og => og.Name == cg.Name)));

            for (int i = 0; i < localGroupsMissing.Count; i++)
            {
                localGroupsMissing[i].Flag = LocalGroupFlag.Add;

                for (int j = 0; j < localGroupsMissing[i].ComputersObj.Flags.Count; j++)
                {
                    localGroupsMissing[i].ComputersObj.Flags[j] = LocalGroupFlag.Add;
                }
                for (int j = 0; j < localGroupsMissing[i].MembersObj.Flags.Count; j++)
                {
                    localGroupsMissing[i].MembersObj.Flags[j] = LocalGroupFlag.Add;
                }
            }

            return localGroupsMissing;
        }

        private static List<LocalGroup> GetOvelapLocalGroups(List<LocalGroup> lgGroupsCurrent, List<LocalGroup> lgGroupsOther)
        {
            var localGroupsOverlap = new List<LocalGroup>();

            localGroupsOverlap.AddRange(lgGroupsCurrent.Where(cg => lgGroupsOther.Any(og => og.Name == cg.Name)));

            foreach (var localGroupOverlap in localGroupsOverlap)
            {
                var matchingGroup = lgGroupsOther.FirstOrDefault(g => g.Name == localGroupOverlap.Name);

                if (matchingGroup != null)
                {

                    for (int i = 0; i < localGroupOverlap.ComputersObj.Names.Count(); i++)
                    {
                        var name = localGroupOverlap.ComputersObj.Names[i];
                        var flag = localGroupOverlap.ComputersObj.Flags[i];

                        if (name.StartsWith(Constants.OrphanedSid))
                        {
                            localGroupOverlap.Flag = LocalGroupFlag.CheckForUpdate;
                            localGroupOverlap.MembersObj.Flags[i] = LocalGroupFlag.Delete;
                            continue;
                        }

                        if (!matchingGroup.ComputersObj.Names.Contains(name))
                        {
                            localGroupOverlap.Flag = LocalGroupFlag.CheckForUpdate;
                            localGroupOverlap.ComputersObj.Flags[i] = LocalGroupFlag.Delete;
                        }
                        else
                        {
                            localGroupOverlap.ComputersObj.Flags[i] = LocalGroupFlag.None;
                        }
                    }

                    for (int i = 0; i < localGroupOverlap.MembersObj.Names.Count(); i++)
                    {
                        var name = localGroupOverlap.MembersObj.Names[i];
                        var flag = localGroupOverlap.MembersObj.Flags[i];

                        if (name.StartsWith(Constants.OrphanedSid))
                        {
                            localGroupOverlap.Flag = LocalGroupFlag.CheckForUpdate;
                            localGroupOverlap.MembersObj.Flags[i] = LocalGroupFlag.Delete;
                            continue;
                        }

                        if (!matchingGroup.MembersObj.Names.Contains(name))
                        {
                            localGroupOverlap.Flag = LocalGroupFlag.CheckForUpdate;
                            localGroupOverlap.MembersObj.Flags[i] = LocalGroupFlag.Delete;
                        }
                        else
                        {
                            localGroupOverlap.MembersObj.Flags[i] = LocalGroupFlag.None;
                        }
                    }
                }
            }

            return localGroupsOverlap;
        }
    }
}
