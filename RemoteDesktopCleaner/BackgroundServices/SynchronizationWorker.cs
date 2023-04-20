using System;
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

namespace RemoteDesktopCleaner.BackgroundServices
{
    public enum ObjectClass
    {
        User,
        Group,
        Computer,
        All,
        Sid
    }
    public sealed class SynchronizationWorker : BackgroundService
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly ISynchronizer _synchronizer;
        private readonly IConfigValidator _configValidator;
        private const string AdSearchGroupPath = "WinNT://{0}/{1},group";
        private const string NamespacePath = @"\root\CIMV2\TerminalServices";
        private readonly DirectoryEntry _rootDir = new DirectoryEntry("LDAP://DC=cern,DC=ch");
        //public SynchronizationWorker(ISynchronizer synchronizer, IFileArchiver fileArchiver, IConfigValidator configValidator)
        //{
        //    _synchronizer = synchronizer;
        //    _fileArchiver = fileArchiver;
        //    _configValidator = configValidator;
        //}

        public SynchronizationWorker(ISynchronizer synchronizer, IConfigValidator configValidator)
        {
            _synchronizer = synchronizer;
            _configValidator = configValidator;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Logger.Debug("CleanerWorker is starting.");
            var gateways = AppConfig.GetGatewaysInUse(); // getting gateway name
            stoppingToken.Register(() => Logger.Debug("CleanerWorker background task is stopping."));
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    //TODO run on each midnight
                    Console.WriteLine($"Starting weekly synchronization for the following gateways: {string.Join(",", gateways)}");
                    //if (!_configValidator.MarkObsoleteData()) // running the cleaner
                    //{
                    //    Logger.Info("Failed validating model config. Existing one will be used.");
                    //    Console.WriteLine("Failed validating model config. Existing one will be used.");
                    //}
                    //else
                    //{
                    //    Logger.Info("Failed validating model config. Existing one will be used.");
                    //    Console.WriteLine("Failed validating model config. Existing one will be used.");
                    //}
                    //var tasks = gateways
                    //    .Select(gateway => Task.Run(() => SynchronizeAsync(gateway)))
                    //    .ToList();
                    SynchronizeAsync("cerngt01");
                    //await Task.WhenAll(tasks);
                    ////ArchiveGatewayReports();
                    //await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        public async void SynchronizeAsync(string serverName)
        {
            try
            {
                //_reporter.Start(serverName); // pravi se loger
                Logger.Debug($"Starting the synchronization of '{serverName}' gateway.");
                //var taskGtRapNames = GetGatewaysRapNamesAsync(serverName); // get all raps from CERNGT01
                if (DownloadGatewayConfig(serverName))
                { // ako uspesno loadujes local group names i napravis LG objekte // ubaci da baci gresku ako je prazno
                //    var cfgDiscrepancy = GetConfigDiscrepancy(serverName); // ovo je diff izmedju MODEL-a i CERNGT01, tj diff kojim treba CERNGT01 da se updatuje
                //    var changedLocalGroups = FilterChangedLocalGroups(cfgDiscrepancy.LocalGroups); // devide diff into groups: group for adding LGs, group for deleting LGs, group for changing LGs (adding/deleting computers/members)
                //    InitReport(serverName, changedLocalGroups); // write a report
                //    var addedGroups = _groupSynchronizer.SyncLocalGroups(changedLocalGroups, serverName); // add/remove/update LGs with cfgDiscrepancy/changedLocalGroups, return added groups
                //    var allGatewayGroups = GetAllGatewayGroupsAfterSynchronization(cfgDiscrepancy, addedGroups); // get LGs which are updated with members and computers (not removed or added) and append with new added groups, so we have now current active groups
                //    Logger.Info($"Awaiting getting gateway RAP names for '{serverName}'.");
                //    var gatewayRapNames = await taskGtRapNames; // update server CERNGT01, get all raps from CERNGT01
                //    Logger.Info($"Finished getting gateway RAP names for '{serverName}'.");
                //    _rapSynchronizer.SynchronizeRaps(serverName, allGatewayGroups, gatewayRapNames); // UPDATE SERVER CERNGT01, gatewayRapNames are raps from server CERNGT01
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
                    var members = GetGroupMembers(lg, serverName);
                    localGroups.Add(new LocalGroup(lg, members));
                    i++;
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
            

            //try
            //{
            //    using (PrincipalContext context = new PrincipalContext(ContextType.Machine, serverName, "svcgtw", "7KJuswxQnLXwWM3znp"))
            //    {
            //        using (GroupPrincipal groupPrincipal = new GroupPrincipal(context))
            //        {
            //            groupPrincipal.Name = $"*{",computer"}*";

            //            using (PrincipalSearcher searcher = new PrincipalSearcher(groupPrincipal))
            //            {
            //                foreach (GroupPrincipal group in searcher.FindAll())
            //                {
            //                    Console.WriteLine("Group Name: {0}", group.Name);
            //                }
            //            }
            //        }
            //    }
            //}
            //catch (Exception ex)
            //{
            //    Console.WriteLine("Error: {0}", ex.Message);
            //}



            catch (Exception ex)
            {
                //_logger.LogError(ex, $"Error getting all local groups on gateway: '{serverName}'.");
            }

            return localGroups;
        }

        public List<string> GetGroupMembers(string groupName, string serverName, ObjectClass memberType = ObjectClass.All)
        {
            var ret = new List<string>();
            try
            {
                if (string.IsNullOrEmpty(groupName))
                    throw new Exception("Group name not specified.");

                using (var groupEntry = new DirectoryEntry(string.Format(AdSearchGroupPath, serverName, groupName)))
                {
                    if (groupEntry == null) throw new Exception($"Group '{groupName}' not found on gateway: '{serverName}'.");

                    foreach (var member in (IEnumerable)groupEntry.Invoke("Members"))
                    {
                        string memberName = GetGroupMember(member, memberType);
                        if (memberName != null)
                            ret.Add(memberName);
                    }
                }

            }
            catch (Exception ex)
            {
                //_logger.LogError(ex, $"Error while getting members of group: '{groupName}' from gateway: '{serverName}'");
            }

            return ret;
        }

        private string GetGroupMember(object member, ObjectClass memberType)
        {
            string result;
            using (var memberEntryNt = new DirectoryEntry(member))
            {
                string memberName = memberEntryNt.Name;
                using (var ds = new DirectorySearcher(_rootDir))
                {
                    switch (memberType)
                    {
                        case ObjectClass.User:
                            ds.Filter =
                                $"(&(objectCategory=CN=Person,CN=Schema,CN=Configuration,DC=cern,DC=ch)(samaccountname={memberName}))";
                            break;
                        case ObjectClass.Group:
                            ds.Filter =
                                $"(&(objectCategory=CN=Group,CN=Schema,CN=Configuration,DC=cern,DC=ch)(samaccountname={memberName}))";
                            break;
                        case ObjectClass.Computer:
                            ds.Filter =
                                $"(&(objectCategory=CN=Computer,CN=Schema,CN=Configuration,DC=cern,DC=ch)(samaccountname={memberName}))";
                            break;
                        case ObjectClass.Sid:
                            ds.Filter = $"(&(samaccountname={memberName}))";
                            //if (ds.FindOne() == null)
                            //    ret.Add(memberName.Trim());
                            break;
                    }

                    SearchResult res = ds.FindOne();
                    if (res == null)
                        return null;

                    if (memberType == ObjectClass.Computer)
                        memberName = memberName.Replace('$', ' ');
                    result = memberName.Trim();
                }
            }

            return result;
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
            try
            {
                const string osQuery = "SELECT * FROM Win32_TSGatewayResourceAuthorizationPolicy";
                const string NamespacePath = "root\\cimv2";

                // Create the ConnectionOptions object with the credentials
                ConnectionOptions options = new ConnectionOptions
                {
                    Username = username,
                    Password = password,
                    Impersonation = ImpersonationLevel.Impersonate,
                    Authentication = AuthenticationLevel.PacketPrivacy
                };

                // Create the ManagementScope with the ConnectionOptions
                ManagementScope scope = new ManagementScope($"\\\\{serverName}\\{NamespacePath}", options);
                scope.Connect();

                // Create the ObjectQuery with the query string
                ObjectQuery query = new ObjectQuery(osQuery);

                // Create the ManagementObjectSearcher with the ManagementScope and ObjectQuery
                ManagementObjectSearcher searcher = new ManagementObjectSearcher(scope, query);

                // Execute the query
                ManagementObjectCollection queryResults = searcher.Get();

                var rapNames = new List<string>();
                Console.WriteLine($"Querying '{serverName}'.");
                foreach (ManagementObject result in queryResults)
                    rapNames.Add(result["Name"].ToString());

                return rapNames;
            }
            catch (Exception ex)
            {
                //_logger.LogError(ex, "Error while getting rap names from gateway: '{serverName}'. Ex: {ex}");
            }

            return new List<string>();
        }

        //private List<string> QueryGatewayRapNamesAsync(string serverName)
        //{
        //    try
        //    {
        //        //const string osQuery = "SELECT * FROM Win32_TSGatewayResourceAuthorizationPolicy";
        //        //var userName = "svcgtw"; // Replace with your username
        //        //var password = "7KJuswxQnLXwWM3znp"; // Replace with your password

        //        //var securePassword = new SecureString();
        //        //foreach (char c in password)
        //        //    securePassword.AppendChar(c);

        //        //WSManConnectionInfo connectionInfo = new WSManConnectionInfo
        //        //{
        //        //    ComputerName = serverName,
        //        //    Credential = new PSCredential(userName, securePassword)
        //        //};

        //        //var runspace = RunspaceFactory.CreateRunspace(connectionInfo);
        //        //runspace.Open();

        //        //using (PowerShell ps = PowerShell.Create())
        //        //{
        //        //    ps.Runspace = runspace;
        //        //    ps.AddScript($"Get-CimInstance -Namespace 'root/CIMV2' -Query '{osQuery}'");
        //        //    var results = ps.Invoke();

        //        //    var rapNames = new List<string>();
        //        //    Console.WriteLine($"Querying '{serverName}'.");
        //        //    foreach (var result in results)
        //        //    {
        //        //        rapNames.Add(result.Properties["Name"].Value.ToString());
        //        //    }

        //            return rapNames;
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        Console.WriteLine(ex.Message);
        //        //_logger.LogError(ex, "Error while getting rap names from gateway: '{serverName}'. Ex: {ex}");
        //    }

        //    return new List<string>();
        //}

    }
}
