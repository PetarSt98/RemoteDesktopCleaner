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
using RemoteDesktopCleaner.Data;
using System.Text;

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
                    var cfgDiscrepancy = GetConfigDiscrepancy(serverName); // ovo je diff izmedju MODEL-a i CERNGT01, tj diff kojim treba CERNGT01 da se updatuje
                    var changedLocalGroups = FilterChangedLocalGroups(cfgDiscrepancy.LocalGroups); // devide diff into groups: group for adding LGs, group for deleting LGs, group for changing LGs (adding/deleting computers/members)
                    //InitReport(serverName, changedLocalGroups); // write a report
                    var addedGroups = SyncLocalGroups(changedLocalGroups, serverName); // add/remove/update LGs with cfgDiscrepancy/changedLocalGroups, return added groups
                    var allGatewayGroups = GetAllGatewayGroupsAfterSynchronization(cfgDiscrepancy, addedGroups); // get LGs which are updated with members and computers (not removed or added) and append with new added groups, so we have now current active groups
                    Logger.Info($"Awaiting getting gateway RAP names for '{serverName}'.");
                    //    var gatewayRapNames = await taskGtRapNames; // update server CERNGT01, get all raps from CERNGT01
                    //    Logger.Info($"Finished getting gateway RAP names for '{serverName}'.");
                    //SynchronizeRaps(serverName, allGatewayGroups, taskGtRapNames); // UPDATE SERVER CERNGT01, gatewayRapNames are raps from server CERNGT01
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
        private LocalGroupsChanges FilterChangedLocalGroups(List<LocalGroup> allGroups)
        {
            //var groupsToDelete2 = allGroups.Where(lg => lg.Name.StartsWith("-")).ToList(); // LG to be deleted
            var groupsToDelete = allGroups.Where(lg => lg.Flag == LocalGroupFlag.Delete).ToList(); // LG to be deleted
            //var groupsToAdd2 = allGroups.Where(lg => lg.Name.StartsWith("+")).ToList(); // LG to be added
            var groupsToAdd = allGroups.Where(lg => lg.Flag == LocalGroupFlag.Add).ToList();
            //var changedContent2 = allGroups.Where(lg => lg.Name.StartsWith("LG-") && lg.Content.Any(content =>
            //    content.StartsWith("S-1-5") || content.StartsWith("+") || content.StartsWith("-"))).ToList(); // LG whose computers or members will be added/deleted
            var changedContent = allGroups.Where(lg => lg.Flag == LocalGroupFlag.CheckForUpdate && lg.MembersObj.Flags.Any(content => content != LocalGroupFlag.None)).ToList(); // LG whose computers or members will be added/deleted
            var groupsToSync = new LocalGroupsChanges();
            groupsToSync.LocalGroupsToDelete = groupsToDelete;
            groupsToSync.LocalGroupsToAdd = groupsToAdd;
            groupsToSync.LocalGroupsToUpdate = changedContent;
            //var groupsToSync = groupsToDelete.Concat(groupsToAdd).Concat(changedContent).ToList(); // concatenate it, suvisno
            return groupsToSync;
        }
        public List<string> SyncLocalGroups(LocalGroupsChanges changedLocalGroups, string serverName)
        {
            //_reporter.Info(serverName, $"There are {changedLocalGroups.Count} groups to synchronize.");
            var groupsToDelete = changedLocalGroups.LocalGroupsToDelete; // ovo bukv imamo u filteringu, suvisno
            var groupsToAdd = changedLocalGroups.LocalGroupsToAdd;
            var modifiedGroups = changedLocalGroups.LocalGroupsToUpdate;

            DeleteGroups(serverName, groupsToDelete); // delete groups with '-' in the name
            var addedGroups = AddNewGroups(serverName, groupsToAdd); // add the groups with '+' in the name
            SyncModifiedGroups(serverName, modifiedGroups); // update computers and members (add/delete) if LG does not have '+'or'-'

            //_reporter.Info(serverName, "Finished synchronizing groups.");
            return addedGroups;
        }
        private void SyncModifiedGroups(string serverName, List<LocalGroup> modifiedGroups)
        {
            //_reporter.Info(serverName, $"Synchronizing content of {modifiedGroups.Count} groups.");
            modifiedGroups.ForEach(lg => SyncGroupContent(lg, serverName));
        }

        private void SyncGroupContent(LocalGroup lg, string server)
        {
            //_reporter.Info(server, $"Synchronizing {lg.Name}.");
            var success = true;
            var localGroup = GetLocalGroup(server, lg.Name);
            if (CleanFromOrphanedSids(localGroup, lg.Name, server)) // ovo popraviti jer nesto nije ok
            {
                if (!SyncMember(localGroup, lg, server))
                    success = false;

                if (!SyncComputers(localGroup, lg, server))
                    success = false;
            }
            else
            {
                //_logger.LogWarning($"Error while cleaning group: '{lg.Name}' from orphaned SIDs, further synchronization on this group is skipped.");
                //_reporter.Error(server, $"Failed cleaning group '{lg.Name}' from orphaned SIDs.");
                success = false;
            }
            //if (success) _reporter.IncrementSynchronizedGroups(server);
        }
        private DirectoryEntry GetLocalGroup(string server, string groupName)
        {
            string username = "svcgtw";
            string password = "7KJuswxQnLXwWM3znp";
            var ad = new DirectoryEntry($"WinNT://{server},computer", username, password);
            try
            {
                DirectoryEntry newGroup = ad.Children.Find(groupName, "group");
                return newGroup;
            }
            catch (Exception ex)
            {
                throw;
            }
        }
        public bool CleanFromOrphanedSids(DirectoryEntry localGroup, string groupName, string serverName)
        {
            string username = "svcgtw";
            string password = "7KJuswxQnLXwWM3znp";
            try
            {
                bool success;
                success = RemoveOrphanedSids(localGroup);
                return success;
            }
            catch (Exception ex)
            {
                //_logger.LogError(ex, $"Error while removing orphaned SIDs from group: '{groupName}' on gateway: '{serverName}'.");
                return false;
            }
        }
        private bool RemoveOrphanedSids(DirectoryEntry groupPrincipal)
        {
            var success = true;
            foreach (var memberObj in (IEnumerable)groupPrincipal.Invoke("Members", null))
            {
                var member = new DirectoryEntry(memberObj);
                if (!member.Name.StartsWith(Constants.OrphanedSid)) continue;
                try
                {
                    //_logger.LogDebug($"Removing SID: '{member.Name}'.");
                    groupPrincipal.Invoke("Remove", $"WinNT://{member.Name}");
                }
                catch (Exception ex)
                {
                    //_logger.LogWarning(ex, $"Failed removing SID: '{member.Name}'.");
                    success = false;
                }
            }

            return success;
        }
        private List<string> AddNewGroups(string serverName, ICollection<LocalGroup> groupsToAdd)
        {
            //_logger.LogInformation($"Adding {groupsToAdd.Count} new groups to the gateway '{serverName}'.");
            //_reporter.Info(serverName, $"Adding {groupsToAdd.Count} new groups.");
            var addedGroups = new List<string>();
            int counter = 0;
            foreach (var lg in groupsToAdd)
            {
                var formattedGroupName = FormatModifiedValue(lg.Name);
                //_reporter.Info(serverName, $"Adding group '{formattedGroupName}'.");
                if (AddNewGroupWithContent(serverName, lg))
                    addedGroups.Add(lg.Name);
                counter++;
                if(counter > 3) break;
            }
            //_reporter.Info(serverName, $"Finished adding {addedGroups.Count} new groups.");
            return addedGroups;
        }
        private bool AddNewGroupWithContent(string server, LocalGroup lg)
        {
            //string groupName = FormatModifiedValue(lg.Name);
            var success = true;
            var newGroup = AddEmptyGroup(lg.Name, server);
            if (newGroup is not null)
            {
                if (!SyncMember(newGroup, lg, server))
                    success = false;
                if (!SyncComputers(newGroup, lg, server))
                    success = false;
            }
            else
            {
                success = false;
                //_reporter.Warn(server, $"Failed adding new group: '{groupName}' and its contents.");
            }
            newGroup.CommitChanges();
            //if (success) _reporter.IncrementAddedGroups(server);
            return success;
        }


        private bool SyncComputers(DirectoryEntry newGroup, LocalGroup lg, string server)
        {
            //string groupName = FormatModifiedValue(lg.Name);
            var success = true;
            var computersData = lg.ComputersObj.Names.Zip(lg.ComputersObj.Flags, (i, j) => new { Name = i, Flag = j });
            foreach (var computer in computersData)
            {
                //string computerName = FormatModifiedValue(computer);
                if (computer.Flag == LocalGroupFlag.Delete)
                    success = success && DeleteComputer(server, lg.Name, computer.Name, newGroup);
                else if (computer.Flag == LocalGroupFlag.Add)
                    success = success && AddComputer(server, lg.Name, computer.Name, newGroup);
            }
            //if (!success) _reporter.Warn(server, $"Failed synchronizing computers for group: '{groupName}'.");
            return success;
        }
        private bool DeleteComputer(string server, string groupName, string computerName, DirectoryEntry newGroup)
        {
            if (RemoveComputerFromLocalGroup(computerName, groupName, server, newGroup)) return true;
            //_logger.LogDebug($"Failed removing computer: '{computerName}' from group: '{groupName}' on gateway: '{server}'.");
            return false;
        }

        private bool AddComputer(string server, string groupName, string computerName, DirectoryEntry newGroup)
        {
            if (AddComputerToLocalGroup(computerName, groupName, server, newGroup)) return true;
            //_logger.LogDebug($"Failed adding new computer: '{computerName}' to group: '{groupName}' on gateway: '{server}'.");
            return false;
        }
        public bool RemoveComputerFromLocalGroup(string computerName, string groupName, string serverName, DirectoryEntry groupEntry)
        {
            var success = true;
            string username = "svcgtw";
            string password = "7KJuswxQnLXwWM3znp";
            try
            {
                try
                {
                    groupEntry.Invoke("Remove", $"WinNT://CERN/{computerName},computer");
                    groupEntry.CommitChanges();
                }
                catch (System.Reflection.TargetInvocationException ex)
                {
                    Console.WriteLine(ex.Message);
                }
                success = true;

            }
            catch (Exception ex)
            {
                //_logger.LogError(ex, $"Error while adding member '{memberName}' to group '{groupName}' on gateway '{serverName}'.");
                success = false;
            }

            return success;
        }

        public bool AddComputerToLocalGroup(string computerName, string groupName, string serverName, DirectoryEntry groupEntry)
        {
            bool success;
            string username = "svcgtw";
            string password = "7KJuswxQnLXwWM3znp";
            //_logger.LogDebug($"Adding new computer '{computerName}' to the group '{groupName}' on gateway '{serverName}'.");
            try
            {
                try
                {
                    groupEntry.Invoke("Add", $"WinNT://CERN/{computerName},computer");
                    groupEntry.CommitChanges();
                }
                catch (System.Reflection.TargetInvocationException ex)
                {
                    Console.WriteLine(ex.Message);
                }
                success = true;

            }
            catch (Exception ex)
            {
                //_logger.LogError(ex, $"Error while adding member '{memberName}' to group '{groupName}' on gateway '{serverName}'.");
                success = false;
            }

            return success;
        }
        private bool SyncMember(DirectoryEntry newGroup, LocalGroup lg, string server)
        {
            var success = true;
            //string groupName = FormatModifiedValue(lg.Name);

            var membersData = lg.MembersObj.Names.Zip(lg.MembersObj.Flags, (i, j) => new { Name = i, Flag = j });

            foreach (var member in membersData.Where(m => !m.Name.StartsWith(Constants.OrphanedSid)))
            {
                //string memberName = FormatModifiedValue(member);
                //if (ShouldBeDeleted(member))
                //    success = DeleteMember(server, lg.Name, memberName);
                //else if (ShouldBeAdded(member))
                //    success = AddMember(server, lg.Name, memberName);
                if (member.Flag == LocalGroupFlag.Delete)
                    success = DeleteMember(server, lg.Name, member.Name, newGroup);
                else if (member.Flag == LocalGroupFlag.Add)
                    success = AddMember(server, lg.Name, member.Name, newGroup);
            }
            //if (!success) _reporter.Warn(server, $"Failed synchronizing members for group '{groupName}'.");
            return success;
        }
        
        private bool DeleteMember(string server, string groupName, string memberName, DirectoryEntry newGroup)
        {
            if (RemoveMemberFromLocalGroup(memberName, groupName, server, newGroup)) return true;
            //_logger.LogDebug($"Failed removing member: '{memberName}' from group: '{groupName}' on gateway: '{server}'.");
            return false;
        }

        private bool AddMember(string server, string groupName, string memberName, DirectoryEntry newGroup)
        {
            if (AddMemberToLocalGroup(memberName, groupName, server, newGroup)) return true;
            //_logger.LogDebug($"Failed adding new member: '{memberName}' to group: '{groupName}' on gateway: '{server}'.");
            return false;
        }
        public bool RemoveMemberFromLocalGroup(string memberName, string groupName, string serverName, DirectoryEntry groupEntry)
        {
            bool success;
            string username = "svcgtw";
            string password = "7KJuswxQnLXwWM3znp";
            //_logger.LogDebug($"Removing member: '{memberName}' from group: '{groupName}' on gateway: '{serverName}'.");
            try
            {
                try
                {
                    groupEntry.Invoke("Remove", $"WinNT://CERN/{memberName},user");
                    groupEntry.CommitChanges();
                }
                catch (System.Reflection.TargetInvocationException ex)
                { 
                    Console.WriteLine(ex.Message);
                }
                success = true;

            }
            catch (Exception ex)
            {
                //_logger.LogError(ex, $"Error while adding member '{memberName}' to group '{groupName}' on gateway '{serverName}'.");
                success = false;
            }

            return success;
        }

        public bool AddMemberToLocalGroup(string memberName, string groupName, string serverName, DirectoryEntry groupEntry)
        {
            bool success;
            string username = "svcgtw";
            string password = "7KJuswxQnLXwWM3znp";
            //_logger.LogDebug($"Adding new member '{memberName}' to the group '{groupName}' on gateway '{serverName}'.");
            try
            {
                try
                {
                    groupEntry.Invoke("Add", $"WinNT://CERN/{memberName},user");
                    groupEntry.CommitChanges();
                }
                catch (System.Reflection.TargetInvocationException ex)
                { 
                    Console.WriteLine(ex.Message);
                }
                success = true;

            }
            catch (Exception ex)
            {
                //_logger.LogError(ex, $"Error while adding member '{memberName}' to group '{groupName}' on gateway '{serverName}'.");
                success = false;
            }

            return success;
        }
        public bool AddMemberToLocalGroup(string memberName, string groupName, string serverName)
        {
            bool success;
            string username = "svcgtw";
            string password = "7KJuswxQnLXwWM3znp";
            //_logger.LogDebug($"Adding new member '{memberName}' to the group '{groupName}' on gateway '{serverName}'.");
            try
            {
                using (var pc = new PrincipalContext(ContextType.Machine, serverName))
                {
                    var gp = GroupPrincipal.FindByIdentity(pc, groupName);
                    if (gp == null)
                    {
                        //_logger.LogDebug($"There is no group: '{groupName}' on gateway: '{serverName}'.");
                        return false;
                    }
                    var groupEntry = (DirectoryEntry)gp.GetUnderlyingObject();

                    if (!ExistsInGroup(gp, memberName))
                        groupEntry.Invoke("Add", $"WinNT://CERN/{memberName},user");
                    else
                        //_logger.LogDebug($"'{memberName}' is already in the group '{groupName}' on gateway '{serverName}'.");
                        Console.WriteLine("already");
                }
                success = true;

            }
            catch (Exception ex)
            {
                //_logger.LogError(ex, $"Error while adding member '{memberName}' to group '{groupName}' on gateway '{serverName}'.");
                success = false;
            }

            return success;
        }
        private bool ExistsInGroup(GroupPrincipal gp, string name)
        {
            var allNames = gp.Members.Select(mem => mem.SamAccountName.ToLower());
            return allNames.Contains(name.ToLower());
        }
        public bool ShouldBeDeleted(string value)
        {
            return value.StartsWith("-");
        }

        public bool ShouldBeAdded(string value)
        {
            return value.StartsWith("+");
        }
        public DirectoryEntry AddEmptyGroup(string groupName, string server)
        {
            var success = true;
            DirectoryEntry newGroup = null;
            string username = "svcgtw";
            string password = "7KJuswxQnLXwWM3znp";
            bool groupExists = false;

            //_logger.LogDebug($"Adding new group: '{groupName}' on gateway: '{server}'.");
            try
            {
                var ad = new DirectoryEntry($"WinNT://{server},computer", username, password);
                try
                {
                    newGroup = ad.Children.Find(groupName, "group");
                    groupExists = true;
                }
                catch (System.Runtime.InteropServices.COMException ex)
                {
                    if (ex.ErrorCode == -2147022675) // Group not found.
                    {
                        groupExists = false;
                    }
                    else
                    {
                        throw;
                    }
                }
                if (!groupExists)
                {
                    newGroup = ad.Children.Add(groupName, "group");
                    newGroup.CommitChanges();
                }
            }
            catch (Exception ex)
            {
                //_logger.LogError(ex, $"Error adding new group: '{groupName}' on gateway: '{server}'.");
                newGroup = null;
            }

            return newGroup;
        }
        private void DeleteGroups(string serverName, List<LocalGroup> groupsToDelete)
        {
            Logger.Info($"Deleting {groupsToDelete.Count} groups on gateway '{serverName}'.");
            //_reporter.Info(serverName, $"Deleting {groupsToDelete.Count} groups.");
            groupsToDelete.ForEach(lg => DeleteGroup(serverName, lg.Name)); // delete each group with '-' in the name
            //_reporter.Info(serverName, "Finished deleting groups.");
        }
        private void DeleteGroup(string server, string localGroup)
        {
            string groupName = FormatModifiedValue(localGroup);
            //_reporter.Info(server, $"Removing group '{groupName}'.");
            DeleteGroup2(localGroup, server);
            //if (_groupManager.DeleteGroup2(groupName, server))
            //    _reporter.IncrementDeletedGroups(server);
            //else
            //    _reporter.Warn(server, $"Failed removing group '{groupName}' from the gateway.");
        }
        public bool DeleteGroup2(string groupName, string server)
        {
            var success = true;
            string username = "svcgtw";
            string password = "7KJuswxQnLXwWM3znp";
            bool groupExists = false;
            DirectoryEntry newGroup = null;

            //Logger.LogDebug($"Removing group '{groupName}' from gateway '{server}'.");
            try
            {
                //using (var machineContext = new PrincipalContext(ContextType.Machine, server, null, username, password))
                //{
                //    var groupPr = GroupPrincipal.FindByIdentity(machineContext, groupName);
                //    groupPr?.Delete();
                //}
                var ad = new DirectoryEntry($"WinNT://{server},computer", username, password);
                try
                {
                    
                    newGroup = ad.Children.Find(groupName, "group");
                    groupExists = true;
                }
                catch (System.Runtime.InteropServices.COMException ex)
                {
                    if (ex.ErrorCode == -2147022675) // Group not found.
                    {
                        groupExists = false;
                    }
                    else
                    {
                        throw;
                    }
                }
                if (groupExists && newGroup is not null)
                {
                    ad.Children.Remove(newGroup);
                    //newGroup.CommitChanges();
                    success = true;
                }
            }
            catch (Exception ex)
            {
                success = false;
                //_logger.LogError(ex, $"Error while deleting group: '{groupName}' from gateway: '{server}'.");
            }

            return success;
        }
        private string FormatModifiedValue(string value)
        {
            if (value.StartsWith("-") || value.StartsWith("+"))
                return value.Substring(1);
            return value;
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
                    if (i > 6) break;
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

        public class GatewayConfig
        {
            public string ServerName { get; set; }
            public List<LocalGroup> LocalGroups { get; } = new List<LocalGroup>();

            public void Add(LocalGroup localGroup)
            {
                LocalGroups.Add(localGroup);
            }

            public void Add(List<LocalGroup> localGroups)
            {
                LocalGroups.AddRange(localGroups);
            }

            public GatewayConfig(string serverName)
            {
                ServerName = serverName;
            }
            public GatewayConfig() { }

            public GatewayConfig(string serverName, IEnumerable<LocalGroup> localGroups)
            {
                ServerName = serverName;
                LocalGroups.AddRange(localGroups);
            }

            public override string ToString()
            {
                return JsonSerializer.Serialize(this);
            }
        }

        private GatewayConfig GetConfigDiscrepancy(string serverName)
        {
            GatewayConfig modelCfg = ReadValidConfigDbModel();
            GatewayConfig gatewayCfg = ReadGatewayConfigFromFile(serverName);
            return CompareWithModel(gatewayCfg, modelCfg);
        }
        public GatewayConfig ReadValidConfigDbModel()
        {
            //_logger.LogDebug("Getting valid config model.");
            //var raps = _mySql.GetGatewayModel();
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
        public GatewayConfig ReadGatewayConfigFromFile(string serverName)
        {
            //_logger.LogDebug($"Reading config for gateway: '{serverName}' from file.");
            //_reporter.Info(serverName, "Loading gateway config into memory.");
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
                Logger.Debug($"Reading local groups for '{serverName}' from file.");
                localGroups.AddRange(Newtonsoft.Json.JsonConvert.DeserializeObject<List<LocalGroup>>(content)); // deserialize mora kad se cita iz jsona
                return localGroups;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error while reading local groups of server: '{serverName}'");
                //_reporter.Error(serverName, $"Loading failed - {ex.Message}");
                throw;
            }
        }
        public GatewayConfig CompareWithModel(GatewayConfig gatewayCfg, GatewayConfig modelCfg)
        {
            Logger.Debug($"Comparing gateway '{gatewayCfg.ServerName}' config to DB model.");
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
                LocalGroup lgDiff;
                if (IsInConfig(gtLocalGroup.Name, modelCfg))
                {
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
        private IEnumerable<string> GetListDiscrepancy(ICollection<string> modelList, ICollection<string> otherList)
        {
            var result = otherList
                .Where(el => !el.StartsWith(Constants.OrphanedSid))
                .Select(el => el.ToLower())
                .Select(el => IsInListIgnoreCase(el, modelList) ? el : $"-{el}").ToList();
            result.AddRange(from el in modelList where !IsInListIgnoreCase(el, otherList) select $"+{el.ToLower()}");
            result.AddRange(otherList.Where(el => el.StartsWith(Constants.OrphanedSid)));
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
                Logger.Warn(ex, $"Failed saving gateway: '{diff.ServerName}' discrepancy config to file.");
            }
        }
        private List<string> GetAllGatewayGroupsAfterSynchronization(GatewayConfig discrepancy, List<string> addedGroups)
        {
            var alreadyExistingGroups = discrepancy.LocalGroups
                                                    .Where(lg => lg.Name.StartsWith("LG-"))
                                                    .Select(lg => lg.Name).ToList();
            return alreadyExistingGroups.Concat(addedGroups).ToList();
        }
        public void SynchronizeRaps(string serverName, List<string> allGatewayGroups, List<string> gatewayRaps)
        {
            var modelRapNames = allGatewayGroups.Select(LgNameToRapName).ToList();
            DeleteObsoleteRaps(serverName, modelRapNames, gatewayRaps);
            AddMissingRaps(serverName, modelRapNames, gatewayRaps);
        }
        private static string LgNameToRapName(string lgName)
        {
            return lgName.Replace("LG-", "RAP_");
        }
        private void DeleteObsoleteRaps(string serverName, List<string> modelRapNames, List<string> gatewayRaps)
        {
            var obsoleteRapNames = gatewayRaps.Except(modelRapNames).ToList();
            //_reporter.SetShouldDeleteRaps(serverName, obsoleteRapNames.Count);
            //_reporter.Info(serverName, $"Deleting {obsoleteRapNames.Count} RAPs from the gateway.");
            //_logger.LogInformation($"Deleting {obsoleteRapNames.Count} RAPs from the gateway '{serverName}'.");
            if (obsoleteRapNames.Count > 0)
                TryDeletingRaps(serverName, obsoleteRapNames);
            //_reporter.Info(serverName, "Finished deleting RAPs.");
            //_logger.LogInformation($"Finished deleting RAPs from the gateway '{serverName}'.");
        }

        private void AddMissingRaps(string serverName, List<string> modelRapNames, List<string> gatewayRapNames)
        {
            var missingRapNames = modelRapNames.Except(gatewayRapNames).ToList();
            //_reporter.SetShouldAddRaps(serverName, missingRapNames.Count);
            //_reporter.Info(serverName, $"Adding {missingRapNames.Count} RAPs to the gateway.");
            //_logger.LogInformation($"Adding {missingRapNames.Count} RAPs to the gateway '{serverName}'.");
            AddMissingRaps(serverName, missingRapNames);
            //_reporter.Info(serverName, "Finished adding RAPs.");
            //_logger.LogInformation($"Finished adding {missingRapNames.Count} RAPs to the gateway '{serverName}'.");
        }
        private void TryDeletingRaps(string serverName, List<string> obsoleteRapNames)
        {
            bool finished = false;
            int counter = 0;
            var toDelete = new List<string>(obsoleteRapNames);
            while (!(counter == 3 || finished))
            {
                var response = DeleteRapsFromGateway(serverName, toDelete);
                Console.WriteLine($"Deleting raps, try #{counter + 1}"); //TODO delete
                //_logger.LogDebug($"Deleting raps, try #{counter + 1}");
                foreach (var res in response)
                {
                    if (res.Deleted)
                    {
                        //_reporter.IncrementDeletedRaps(serverName);
                        Console.WriteLine($"Deleted '{res.RapName}'.");
                        //_logger.LogDebug($"Deleted '{res.RapName}'.");
                        if (toDelete.Count == 0) finished = true;
                    }
                    else
                    {
                        Console.WriteLine($"Failed deleting '{res.RapName}'."); //TODO delete
                        //_logger.LogDebug($"Failed deleting '{res.RapName}'.");
                    }
                }
                toDelete = toDelete.Except(response.Where(r => r.Deleted).Select(r => r.RapName)).ToList();
                counter++;
            }
        }
        public bool AddMissingRaps(string serverName, List<string> missingRapNames)
        {
            var sHost = $@"\\{serverName}";
            try
            {
                var oConn = new ConnectionOptions();
                oConn.Impersonation = ImpersonationLevel.Impersonate;
                oConn.Authentication = AuthenticationLevel.PacketPrivacy;

                var oMScope = new ManagementScope(sHost + NamespacePath, oConn);
                oMScope.Options.Authentication = AuthenticationLevel.PacketPrivacy;
                oMScope.Options.Impersonation = ImpersonationLevel.Impersonate;

                var oMPath = new ManagementPath();
                oMPath.ClassName = "Win32_TSGatewayResourceAuthorizationPolicy";
                oMPath.NamespacePath = NamespacePath;

                oMScope.Connect();

                ManagementClass processClass = new ManagementClass(oMScope, oMPath, null);

                ManagementBaseObject inParameters = processClass.GetMethodParameters("Create");
                var mnvc = new ManagementNamedValueCollection();
                var imo = new InvokeMethodOptions();
                imo.Context = mnvc;
                processClass.Get();
                var i = 0;
                inParameters["Description"] = "";
                inParameters["Enabled"] = true;
                inParameters["ResourceGroupType"] = "CG";
                inParameters["ProtocolNames"] = "RDP";
                inParameters["PortNumbers"] = "3389";
                foreach (var rapName in missingRapNames)
                {
                    //_reporter.Info(serverName, $"Adding '{rapName}'.");
                    //_logger.LogInformation($"Adding new RAP '{rapName}' to the gateway '{serverName}'.");
                    var groupName = ConvertToLgName(rapName);
                    inParameters["Name"] = "" + rapName;
                    inParameters["ResourceGroupName"] = groupName;
                    inParameters["UserGroupNames"] = groupName;

                    ManagementBaseObject outParameters = processClass.InvokeMethod("Create", inParameters, imo);

                    if ((uint)outParameters["ReturnValue"] == 0)
                    {
                        Console.WriteLine($"{rapName} created. {++i}/{missingRapNames.Count}"); //TODO delete
                        //_logger.LogInformation($"RAP '{rapName}' added to the gateway '{serverName}'.");
                        //_reporter.IncrementAddedRaps(serverName);
                    }
                    //else
                    //{
                    //    _reporter.Warn(serverName, $"Failed adding new RAP '{rapName}'. Error code: '{(uint)outParameters["ReturnValue"]}'.");
                    //    _logger.LogWarning($"Error creating RAP: '{rapName}'. Reason: {(uint)outParameters["ReturnValue"]}.");
                    //}
                }
                return true;
            }
            catch (System.Exception ex)
            {
                //_logger.LogError(ex, $"Error when adding new RAPs to the gateway '{serverName}'.");
                //_reporter.Error(serverName, $"Exception when adding missing RAPs to the gateway. Details: {ex.Message}");
                return false;
            }
        }
        private string ConvertToLgName(string rapName)
        {
            return rapName.Replace("RAP_", "LG-");
        }
        public List<RapsDeletionResponse> DeleteRapsFromGateway(string serverName, List<string> rapNamesToDelete)
        {
            var result = new List<RapsDeletionResponse>();
            try
            {
                string where = CreateWhereClause(rapNamesToDelete);
                string osQuery =
                    "SELECT * FROM Win32_TSGatewayResourceAuthorizationPolicy " + where;
                CimSession mySession = CimSession.Create(serverName);
                IEnumerable<CimInstance> queryInstance = mySession.QueryInstances(NamespacePath, "WQL", osQuery);
                //_logger.LogDebug($"Querying '{serverName}' for {rapNamesToDelete.Count} RAPs to delete.");
                foreach (CimInstance rapInstance in queryInstance)
                {
                    var rapName = rapInstance.CimInstanceProperties["Name"].Value.ToString();
                    if (!rapNamesToDelete.Contains(rapName)) continue;

                    var rapDeletion = DeleteRap(mySession, rapInstance, rapName);
                    result.Add(rapDeletion);
                }
            }
            catch (Exception ex)
            {
                //_logger.LogError($"Error while getting rap names from gateway: '{serverName}'.Ex: {ex}");
            }

            return result;
        }
        private string CreateWhereClause(IEnumerable<string> names)
        {
            var enumerable = names.ToList();
            if (enumerable.Count == 0)
                return "";
            var sb = new StringBuilder();
            sb.Append("WHERE");
            for (var i = 0; i < enumerable.Count; i++)
            {
                var name = enumerable[i];
                sb.Append(" (Name=\"" + name + "\")");
                if (i != enumerable.Count - 1)
                    sb.Append(" or");
            }
            return sb.ToString();
        }
        private RapsDeletionResponse DeleteRap(CimSession mySession, CimInstance rapInstance, string rapName)
        {
            var rapDeletion = new RapsDeletionResponse(rapName);
            try
            {
                var result = mySession.InvokeMethod(rapInstance, "Delete", null);
                if (int.Parse(result.ReturnValue.Value.ToString()) == 0)
                {
                    rapDeletion.Deleted = true;
                    //_logger.LogDebug($"Deleted RAP '{rapName}'.");
                }
            }
            catch (Exception ex)
            {
                //_logger.LogWarning(ex, $"Error deleting rap '{rapName}'.");
            }

            return rapDeletion;
        }

    }

    public class RapsDeletionResponse
    {
        public string RapName { get; set; }
        public bool Deleted { get; set; }

        public override string ToString()
        {
            return JsonSerializer.Serialize(this);
        }

        public RapsDeletionResponse(string name)
        {
            RapName = name;
        }
    }
}
