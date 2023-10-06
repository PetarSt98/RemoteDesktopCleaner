using System.Text.Json;
using SynchronizerLibrary.Data;
using SynchronizerLibrary.Loggers;
using SynchronizerLibrary.CommonServices;


namespace RemoteDesktopCleaner.BackgroundServices
{
    public class DataRestoration : IDataRestoration
    {
        private readonly IGatewayRapSynchronizer _gatewayRapSynchronizer;
        private readonly IGatewayLocalGroupSynchronizer _gatewayLocalGroupSynchronizer;

        public DataRestoration(IGatewayRapSynchronizer gatewayRapSynchronizer, IGatewayLocalGroupSynchronizer gatewayLocalGroupSynchronizer)
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
                Console.WriteLine($"Get policies on {serverName}");
                var taskGtRapNames = _gatewayRapSynchronizer.GetGatewaysRapNamesAsync(serverName, true);
                LoggerSingleton.General.Info($"Awaiting getting gateway Local Group names for '{serverName}'.");
                if (_gatewayLocalGroupSynchronizer.DownloadGatewayConfig(serverName, true))
                {
                    GatewayConfig gatewayCfg = Synchronizer.ReadGatewayConfigFromFile(serverName);

                    gatewayCfg = updateFlags(gatewayCfg);

                    var changedLocalGroups = Synchronizer.FilterChangedLocalGroups(gatewayCfg.LocalGroups);

                    var addedGroups = _gatewayLocalGroupSynchronizer.SyncLocalGroups(changedLocalGroups, serverName);
                    var allGatewayGroups = Synchronizer.GetAllGatewayGroupsAfterSynchronization(changedLocalGroups);

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
