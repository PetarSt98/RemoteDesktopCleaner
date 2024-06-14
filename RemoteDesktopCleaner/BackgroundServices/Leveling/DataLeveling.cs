using System.Text.Json;
using SynchronizerLibrary.Data;
using SynchronizerLibrary.Loggers;
using SynchronizerLibrary.CommonServices;
using SynchronizerLibrary.CommonServices.LocalGroups;
using RemoteDesktopCleaner.BackgroundServices.Services;

namespace RemoteDesktopCleaner.BackgroundServices
{
    public class DataLeveling : IDataLeveling
    {
        private readonly IGatewayRapSynchronizer _gatewayRapSynchronizer;
        private readonly IGatewayLocalGroupSynchronizer _gatewayLocalGroupSynchronizer;

        public DataLeveling(IGatewayRapSynchronizer gatewayRapSynchronizer, IGatewayLocalGroupSynchronizer gatewayLocalGroupSynchronizer)
        {
            _gatewayLocalGroupSynchronizer = gatewayLocalGroupSynchronizer;
            _gatewayRapSynchronizer = gatewayRapSynchronizer;
        }

        public async Task SynchronizeAsync(string serverName)
        {


            try
            {
                LoggerSingleton.General.Info($"Starting the synchronization of '{serverName}' gateway.");


                GatewayConfig gatewayCfg = await ReadGatewayConfigFromFile(serverName);

                gatewayCfg = updateFlags(gatewayCfg);

                var changedLocalGroups = Synchronizer.FilterChangedLocalGroups(gatewayCfg.LocalGroups);

                var addedGroups = await _gatewayLocalGroupSynchronizer.SyncLocalGroups(changedLocalGroups, serverName, "serverInit");
                var allGatewayGroups = Synchronizer.GetAllGatewayGroupsAfterSynchronization(changedLocalGroups);

                //LoggerSingleton.General.Info($"Finished getting gateway RAP names for '{serverName}'.");
                LoggerSingleton.General.Info("Finished synchronization");
            }
            catch (Exception ex)
            {
                LoggerSingleton.General.Error(ex, $"Error while synchronizing gateway: '{serverName}'.");
            }
            Console.WriteLine($"Finished synchronization for gateway '{serverName}'.");
            LoggerSingleton.General.Info($"Finished synchronization for gateway '{serverName}'.");
        }

        public async Task LeveloutDataAsync(List<string> gatewaysToSynchronize)
        {
            try
            {
                await this.DownloadGatewayConfiguration(gatewaysToSynchronize);

                LGSParser lGSParser = new LGSParser(gatewaysToSynchronize);
                lGSParser.ParseMissingLGs();
                lGSParser.StoreParsedMissigLGs();
                lGSParser.AddMissingLGsToDB();
                lGSParser.CleanData();

                Console.WriteLine("Finished comparing!");

            }
            catch(Exception ex)
            {
                Console.WriteLine(ex.Message);
                LoggerSingleton.General.Error(ex.Message);
            }
        }

        public async Task DownloadGatewayConfiguration(List<string> gatewaysToSynchronize)
        {
            var downloadingTasks = new List<Task>();
            foreach (var gatewayName in gatewaysToSynchronize)
            {
                downloadingTasks.Add(Task.Run(() => _gatewayLocalGroupSynchronizer.DownloadGatewayConfig(gatewayName, false)));
            }

            await Task.WhenAll(downloadingTasks);
        }

        public async static Task<GatewayConfig> ReadGatewayConfigFromFile(string serverName)
        {
            LoggerSingleton.General.Info($"Reading config for gateway: '{serverName}' from file.");
            var lgGroups = await GetGatewayLocalGroupsFromFile(serverName);
            var cfg = new GatewayConfig(serverName, lgGroups);
            return cfg;
        }

        public async static Task<IEnumerable<LocalGroup>> GetGatewayLocalGroupsFromFile(string serverName)
        {
            try
            {
                var missingLocalGroups = new List<MissingLocalGroups>();

                LoggerSingleton.SynchronizedLocalGroups.Debug($"Reading local groups for '{serverName}' from file.");
                LoggerSingleton.General.Debug($"Reading local groups for '{serverName}' from file.");
                Console.WriteLine($"Reading local groups for '{serverName}' from file.");

                missingLocalGroups.AddRange(LGSParser.LoadMissingLocalGroupsCacheFromFile(serverName));
                var localGroups = new List<LocalGroup>();
                
                foreach (var missingGroup in missingLocalGroups)
                {
                    List<string> allMembers = new List<string>(missingGroup.Computers);
                    allMembers.AddRange(missingGroup.Members);

                    localGroups.Add(new LocalGroup(missingGroup.Name, allMembers));
                }

                return localGroups;
            }
            catch (Exception ex)
            {
                LoggerSingleton.General.Fatal($"{ex.ToString()} Error while reading local groups of server: '{serverName}'");
                throw;
            }
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
