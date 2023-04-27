using System;
using RemoteDesktopCleaner.BackgroundServices;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.DirectoryServices;
using System.Management;
using System.Collections.Generic;
using System.Management.Automation;
using Microsoft.Extensions.Hosting;
using NLog;
using RemoteDesktopCleaner.BackgroundServices;
using Microsoft.Management.Infrastructure;
//using RemoteDesktopCleaner.Modules.FileArchival;
using Microsoft.Management.Infrastructure;
using Microsoft.Management.Infrastructure.Options;
using System.Security;
using System.Net;
using System.Management.Automation.Runspaces;
using System.Collections;
using System.Text.Json;
using System.DirectoryServices.AccountManagement;
using RemoteDesktopCleaner.Data;
using System.Text;

namespace RemoteDesktopCleaner.BackgroundServices
{
    public class Synchronizer : ISynchronizer
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        public Synchronizer()
        {

        }
        public async void SynchronizeAsync(string serverName)
        {
            try
            {
                //_reporter.Start(serverName); // pravi se loger
                Logger.Debug($"Starting the synchronization of '{serverName}' gateway.");
                var taskGtRapNames = GetGatewaysRapNamesAsync(serverName); // get all raps from CERNGT01
                if (DownloadGatewayConfig(serverName))
                { // ako uspesno loadujes local group names i napravis LG objekte // ubaci da baci gresku ako je prazno
                    var cfgDiscrepancy = GetConfigDiscrepancy(serverName); // ovo je diff izmedju MODEL-a i CERNGT01, tj diff kojim treba CERNGT01 da se updatuje
                    var changedLocalGroups = FilterChangedLocalGroups(cfgDiscrepancy.LocalGroups); // devide diff into groups: group for adding LGs, group for deleting LGs, group for changing LGs (adding/deleting computers/members)
                    //InitReport(serverName, changedLocalGroups); // write a report
                    var addedGroups = SyncLocalGroups(changedLocalGroups, serverName); // add/remove/update LGs with cfgDiscrepancy/changedLocalGroups, return added groups
                    var allGatewayGroups = GetAllGatewayGroupsAfterSynchronization(cfgDiscrepancy, addedGroups); // get LGs which are updated with members and computers (not removed or added) and append with new added groups, so we have now current active groups
                    Logger.Info($"Awaiting getting gateway RAP names for '{serverName}'.");
                    //var gatewayRapNames = await taskGtRapNames; // update server CERNGT01, get all raps from CERNGT01
                    //    Logger.Info($"Finished getting gateway RAP names for '{serverName}'.");
                    SynchronizeRaps(serverName, allGatewayGroups, taskGtRapNames); // UPDATE SERVER CERNGT01, gatewayRapNames are raps from server CERNGT01
                }
                //_reporter.Finish(serverName); // create log file and send it to email
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error while synchronizing gateway: '{serverName}'.");
                //_reporter.Finish(serverName);
            }
            Console.WriteLine($"Finished synchronization for gateway '{serverName}'.");
        }
        private List<string> GetGatewaysRapNamesAsync(string serverName)
        {
            //_logger.LogInformation($"Getting RAP names from gateway '{serverName}'.");
            try
            {
                return GetGatewayRapNamesAsync2(serverName);
            }
            catch (Exception ex)
            {
                //_logger.LogError(ex, $"Failed getting RAP names from gateway '{serverName}'.");
                throw;
            }
        }
        public List<string> GetGatewayRapNamesAsync2(string serverName)
        {
            return QueryGatewayRapNamesAsync(serverName);
        }

        private List<string> QueryGatewayRapNamesAsync(string serverName)
        {
            var username = "svcgtw"; // replace with your username
            var password = "7KJuswxQnLXwWM3znp"; // replace with your password
            var securepassword = new SecureString();
            foreach (char c in password)
                securepassword.AppendChar(c);
            const string AdSearchGroupPath = "WinNT://{0}/{1},group";
            const string NamespacePath = @"\root\CIMV2\TerminalServices";
            string _oldGatewayServerHost = @"\\cerngt01.cern.ch";
            try
            {
                const string osQuery = "SELECT * FROM Win32_TSGatewayResourceAuthorizationPolicy";
                CimCredential Credentials = new CimCredential(PasswordAuthenticationMechanism.Default, "cern.ch", username, securepassword);

                WSManSessionOptions SessionOptions = new WSManSessionOptions();
                SessionOptions.AddDestinationCredentials(Credentials);
                CimSession mySession = CimSession.Create(serverName, SessionOptions);
                IEnumerable<CimInstance> queryInstance = mySession.QueryInstances(_oldGatewayServerHost + NamespacePath, "WQL", osQuery);
                var rapNames = new List<string>();
                Console.WriteLine($"Querying '{serverName}'.");
                foreach (CimInstance x in queryInstance)
                    rapNames.Add(x.CimInstanceProperties["Name"].Value.ToString());

                return rapNames;
            }
            catch (Exception ex)
            {
                //_logger.LogError(ex, "Error while getting rap names from gateway: '{serverName}'. Ex: {ex}");
            }

            return new List<string>();
        }
        public bool DownloadGatewayConfig(string serverName)
        {
            return ReadRemoteGatewayConfig(serverName);
        }

        private bool ReadRemoteGatewayConfig(string serverName)
        {
            try
            {
                // _reporter.Info(serverName, "Downloading the gateway config.");
                var localGroups = new List<LocalGroup>();
                var server = $"{serverName}.cern.ch";
                var dstDir = AppConfig.GetInfoDir();
                var path = dstDir + @"\" + serverName + ".json";
                var localGroupNames = GetAllLocalGroups(server);
                var i = 0;
                foreach (var lg in localGroupNames)
                {
                    Console.Write(
                        $"\rDownloading {serverName} config - {i + 1}/{localGroupNames.Count} - {100 * (i + 1) / localGroupNames.Count}%"); //TODO delete
                    var members = GetGroupMembers(lg, serverName + ".cern.ch");
                    localGroups.Add(new LocalGroup(lg, members));
                    i++;
                    //if (i > 6) break;
                }

                File.WriteAllText(path, JsonSerializer.Serialize(localGroups));
                // _reporter.Info(serverName, "Gateway config downloaded.");
                return true;
            }
            catch (Exception ex)
            {
                //_logger.LogDebug(ex, $"Error while reading gateway: '{serverName}' config.");
                //_reporter.Error(serverName, "Error during downloading the gateway config.");
                return false;
            }
        }
        public List<string> GetAllLocalGroups(string serverName)
        {
            var localGroups = new List<string>();
            try
            {
                string username = "svcgtw";
                string password = "7KJuswxQnLXwWM3znp";

                using (var groupEntry = new DirectoryEntry($"WinNT://{serverName},computer", username, password))
                {
                    foreach (DirectoryEntry child in (IEnumerable)groupEntry.Children)
                    {
                        if (child.SchemaClassName.Equals("group", StringComparison.OrdinalIgnoreCase) &&
                            child.Name.StartsWith("LG-", StringComparison.OrdinalIgnoreCase))
                        {
                            localGroups.Add(child.Name);
                        }
                    }
                }
            }

            catch (Exception ex)
            {
                //_logger.LogError(ex, $"Error getting all local groups on gateway: '{serverName}'.");
            }

            return localGroups;
        }
    }
}
