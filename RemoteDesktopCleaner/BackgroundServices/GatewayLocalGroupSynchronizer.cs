using System.DirectoryServices;
using System.Collections;
using System.Text.Json;
using RemoteDesktopCleaner.Loggers;
using SynchronizerLibrary.CommonServices;


namespace RemoteDesktopCleaner.BackgroundServices.Obsolete
{
    public class GatewayLocalGroupSynchronizer : IGatewayLocalGroupSynchronizer
    {
        private const string AdSearchGroupPath = "WinNT://{0}/{1},group";
        private readonly DirectoryEntry _rootDir = new DirectoryEntry("LDAP://DC=cern,DC=ch");
        public GatewayLocalGroupSynchronizer()
        {
        }
        public bool DownloadGatewayConfig(string serverName)
        {
            return true;
            LoggerSingleton.General.Info($"Started fetching Local Groups from the server {serverName}");
            return ReadRemoteGatewayConfig(serverName);
        }
        private bool ReadRemoteGatewayConfig(string serverName)
        {
            try
            {
                LoggerSingleton.General.Info(serverName, "Downloading the gateway config.");
                LoggerSingleton.SynchronizedLocalGroups.Info(serverName, "Downloading the gateway config.");
                var localGroups = new List<LocalGroup>();
                var server = $"{serverName}.cern.ch";
                var dstDir = AppConfig.GetInfoDir();
                var path = dstDir + @"\" + serverName + ".json";
                var localGroupNames = GetAllLocalGroups(server);
                var i = 1;
                foreach (var lg in localGroupNames)
                {
                    Console.Write($"\rDownloading {serverName} config - {i + 1}/{localGroupNames.Count} - {100 * (i + 1) / localGroupNames.Count}%"); //TODO delete
                    LoggerSingleton.SynchronizedLocalGroups.Debug($"\r {i} - Downloading {serverName} config - {i + 1}/{localGroupNames.Count} - {100 * (i + 1) / localGroupNames.Count}%");
                    var members = GetGroupMembers(lg, serverName + ".cern.ch");
                    localGroups.Add(new LocalGroup(lg, members));
                    i++;

                    //if (i > 100) break;
                }

                File.WriteAllText(path, JsonSerializer.Serialize(localGroups));
                LoggerSingleton.General.Info(serverName, "Gateway config downloaded.");
                LoggerSingleton.SynchronizedLocalGroups.Info(serverName, "Gateway config downloaded.");
                return true;
            }
            catch (Exception ex)
            {
                LoggerSingleton.General.Fatal($"{ex.ToString()} Error while reading gateway: '{serverName}' config.");
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
                LoggerSingleton.General.Fatal($"{ex.ToString()} Error getting all local groups on gateway: '{serverName}'.");
            }

            return localGroups;
        }

        public List<string> GetGroupMembers(string groupName, string serverName, ObjectClass memberType = ObjectClass.All)
        {
            var downloadedMembers = new List<string>();
            try
            {
                if (string.IsNullOrEmpty(groupName))
                {
                    LoggerSingleton.SynchronizedLocalGroups.Error("Group name not specified.");
                    throw new Exception("Group name not specified.");
                }
                    
                using (var groupEntry = new DirectoryEntry(string.Format(AdSearchGroupPath, serverName, groupName)))
                {
                    if (groupEntry == null) throw new Exception($"Group '{groupName}' not found on gateway: '{serverName}'.");

                    foreach (var member in (IEnumerable)groupEntry.Invoke("Members"))
                    {
                        string memberName = GetGroupMember(member, memberType);
                        if (memberName != null)
                        {
                            LoggerSingleton.SynchronizedLocalGroups.Debug($"Downloaded member: {memberName} from LG: {groupName} from server: {serverName}");
                            downloadedMembers.Add(memberName);
                        }
                    }
                }

            }
            catch (Exception ex)
            {
                LoggerSingleton.General.Fatal($"{ex.ToString()} Error while getting members of group: '{groupName}' from gateway: '{serverName}'");
            }

            return downloadedMembers;
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

        public List<string> SyncLocalGroups(LocalGroupsChanges changedLocalGroups, string serverName)
        {
            LoggerSingleton.General.Info(serverName, $"There are {changedLocalGroups.LocalGroupsToAdd.Count + changedLocalGroups.LocalGroupsToDelete.Count + changedLocalGroups.LocalGroupsToUpdate.Count} groups to synchronize.");
            LoggerSingleton.General.Info(serverName, $"There are {changedLocalGroups.LocalGroupsToAdd.Count } groups to add to server {serverName}.");
            LoggerSingleton.General.Info(serverName, $"There are {changedLocalGroups.LocalGroupsToDelete.Count } groups to delete from server {serverName}.");
            LoggerSingleton.General.Info(serverName, $"There are {changedLocalGroups.LocalGroupsToUpdate.Count } groups to be updated by editing members/devices on server {serverName}.");

            DeleteGroups(serverName, changedLocalGroups.LocalGroupsToDelete);
            var addedGroups = AddNewGroups(serverName, changedLocalGroups.LocalGroupsToAdd);
            SyncModifiedGroups(serverName, changedLocalGroups.LocalGroupsToUpdate);

            LoggerSingleton.General.Info($"Finished synchronizing groups on server {serverName}.");
            LoggerSingleton.SynchronizedLocalGroups.Info($"Finished synchronizing groups on server {serverName}.");

            return addedGroups;
        }

        private void DeleteGroups(string serverName, List<LocalGroup> groupsToDelete)
        {
            LoggerSingleton.General.Info($"Started deleting {groupsToDelete.Count} groups on gateway '{serverName}'.");
            //_reporter.Info(serverName, $"Deleting {groupsToDelete.Count} groups.");
            groupsToDelete.ForEach(lg => DeleteGroup(serverName, lg.Name)); // delete each group with '-' in the name
            LoggerSingleton.General.Info(serverName, "Finished deleting groups.");
        }

        private void DeleteGroup(string server, string localGroup)
        {
            LoggerSingleton.SynchronizedLocalGroups.Info(server, $"Removing group '{localGroup}'.");

            if (DeleteGroup2(localGroup, server))
                LoggerSingleton.SynchronizedLocalGroups.Info($"Local Group successfully deleted {localGroup} on server {server}");
            else
                LoggerSingleton.SynchronizedLocalGroups.Error($"Local Group unsuccessfully deleted {localGroup} on server {server}");
        }

        public bool DeleteGroup2(string groupName, string server)
        {
            var success = true;
            string username = "svcgtw";
            string password = "7KJuswxQnLXwWM3znp";
            bool groupExists = false;
            DirectoryEntry newGroup = null;

            LoggerSingleton.SynchronizedLocalGroups.Info($"Removing group '{groupName}' from gateway '{server}'.");
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
                if (groupExists && newGroup is not null)
                {
                    ad.Children.Remove(newGroup);
                    success = true;
                }
            }
            catch (Exception ex)
            {
                success = false;
                LoggerSingleton.SynchronizedLocalGroups.Error(ex, $"Error while deleting group: '{groupName}' from gateway: '{server}'.");
            }

            return success;
        }

        private List<string> AddNewGroups(string serverName, ICollection<LocalGroup> groupsToAdd)
        {
            LoggerSingleton.General.Info($"Adding {groupsToAdd.Count} new groups to the gateway '{serverName}'.");
            var addedGroups = new List<string>();
            var i = 1;
            foreach (var lg in groupsToAdd)
            {
                LoggerSingleton.SynchronizedLocalGroups.Info(serverName, $"Adding group '{lg.Name}'.");
                if (AddNewGroupWithContent(serverName, lg))
                    addedGroups.Add(lg.Name);
                i++;
                //if (i > 100) break;
            }
            LoggerSingleton.General.Info(serverName, $"Finished adding {addedGroups.Count} new groups.");
            LoggerSingleton.SynchronizedLocalGroups.Info(serverName, $"Finished adding {addedGroups.Count} new groups.");
            return addedGroups;
        }

        private bool AddNewGroupWithContent(string server, LocalGroup lg)
        {
            //string groupName = FormatModifiedValue(lg.Name);
            var success = true;
            LoggerSingleton.SynchronizedLocalGroups.Info($"Adding empty group {lg.Name}");
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
                LoggerSingleton.SynchronizedLocalGroups.Error(server, $"Failed adding new group: '{lg.Name}' and its contents.");
            }
            newGroup.CommitChanges();
            return success;
        }

        public DirectoryEntry AddEmptyGroup(string groupName, string server)
        {
            var success = true;
            DirectoryEntry newGroup = null;
            string username = "svcgtw";
            string password = "7KJuswxQnLXwWM3znp";
            bool groupExists = false;

            LoggerSingleton.SynchronizedLocalGroups.Debug($"Adding new group: '{groupName}' on gateway: '{server}'.");
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
                    if (ex.ErrorCode == -2147022675 || ex.ErrorCode == -2147022676) // Group not found.
                    {
                        LoggerSingleton.SynchronizedLocalGroups.Debug($"Group already exists: '{groupName}' on gateway: '{server}'.");
                        groupExists = false;
                    }
                    else
                    {
                        LoggerSingleton.SynchronizedLocalGroups.Warn($"Different exception message than Already existing group: '{groupName}' on gateway: '{server}'.");
                        groupExists = false;
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
                LoggerSingleton.SynchronizedLocalGroups.Error(ex, $"Error adding empty group: '{groupName}' on gateway: '{server}'.");
                newGroup = null;
            }

            return newGroup;
        }

        private bool SyncMember(DirectoryEntry newGroup, LocalGroup lg, string server)
        {
            var success = true;
            var general_success = true;

            var membersData = lg.MembersObj.Names.Zip(lg.MembersObj.Flags, (i, j) => new { Name = i, Flag = j });

            foreach (var member in membersData.Where(m => !m.Name.StartsWith(Constants.OrphanedSid)))
            {
                if (member.Flag == LocalGroupFlag.Delete)
                    success = DeleteMember(server, lg.Name, member.Name, newGroup);
                else if (member.Flag == LocalGroupFlag.Add)
                    success = AddMember(server, lg.Name, member.Name, newGroup);
                general_success = general_success && success;
            }
            if (!general_success) LoggerSingleton.SynchronizedLocalGroups.Error(server, $"Failed synchronizing members for group '{ lg.Name}'.");
            return general_success;
        }

        private bool DeleteMember(string server, string groupName, string memberName, DirectoryEntry newGroup)
        {
            if (RemoveMemberFromLocalGroup(memberName, groupName, server, newGroup)) return true;
            LoggerSingleton.SynchronizedLocalGroups.Error($"Failed removing member: '{memberName}' from group: '{groupName}' on gateway: '{server}'.");
            return false;
        }

        private bool AddMember(string server, string groupName, string memberName, DirectoryEntry newGroup)
        {
            if (AddMemberToLocalGroup(memberName, groupName, server, newGroup)) return true;
            LoggerSingleton.SynchronizedLocalGroups.Error($"Failed adding new member: '{memberName}' to group: '{groupName}' on gateway: '{server}'.");
            return false;
        }

        public bool RemoveMemberFromLocalGroup(string memberName, string groupName, string serverName, DirectoryEntry groupEntry)
        {
            bool success;
            LoggerSingleton.SynchronizedLocalGroups.Info($"Removing member: '{memberName}' from group: '{groupName}' on gateway: '{serverName}'.");
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
                LoggerSingleton.SynchronizedLocalGroups.Error(ex, $"Error while adding member '{memberName}' to group '{groupName}' on gateway '{serverName}'.");
                success = false;
            }

            return success;
        }

        public bool AddMemberToLocalGroup(string memberName, string groupName, string serverName, DirectoryEntry groupEntry)
        {
            bool success;
            string username = "svcgtw";
            string password = "7KJuswxQnLXwWM3znp";
            LoggerSingleton.SynchronizedLocalGroups.Info($"Adding new member '{memberName}' to the group '{groupName}' on gateway '{serverName}'.");
            try
            {
                try
                {
                    groupEntry.Invoke("Add", $"WinNT://CERN/{memberName},user");
                    groupEntry.CommitChanges();
                }
                catch (System.Reflection.TargetInvocationException ex)
                {
                    LoggerSingleton.SynchronizedLocalGroups.Warn($"Member already exists '{memberName}' to the group '{groupName}' on gateway '{serverName}'.");
                    Console.WriteLine(ex.Message);
                }
                success = true;

            }
            catch (Exception ex)
            {
                LoggerSingleton.SynchronizedLocalGroups.Error(ex, $"Error while adding member '{memberName}' to group '{groupName}' on gateway '{serverName}'.");
                success = false;
            }

            return success;
        }

        private bool DeleteComputer(string server, string groupName, string computerName, DirectoryEntry newGroup)
        {
            if (RemoveComputerFromLocalGroup(computerName, groupName, server, newGroup)) return true;
            LoggerSingleton.SynchronizedLocalGroups.Error($"Failed removing computer: '{computerName}' from group: '{groupName}' on gateway: '{server}'.");
            return false;
        }

        private bool AddComputer(string server, string groupName, string computerName, DirectoryEntry newGroup)
        {
            if (AddComputerToLocalGroup(computerName, groupName, server, newGroup)) return true;
            LoggerSingleton.SynchronizedLocalGroups.Error($"Failed adding new computer: '{computerName}' to group: '{groupName}' on gateway: '{server}'.");
            return false;
        }

        public bool AddComputerToLocalGroup(string computerName, string groupName, string serverName, DirectoryEntry groupEntry)
        {
            bool success;
            LoggerSingleton.SynchronizedLocalGroups.Info($"Adding new computer '{computerName}' to the group '{groupName}' on gateway '{serverName}'.");
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
                LoggerSingleton.SynchronizedLocalGroups.Error(ex, $"Error while adding member '{computerName}' to group '{groupName}' on gateway '{serverName}'.");
                success = false;
            }

            return success;
        }

        public bool RemoveComputerFromLocalGroup(string computerName, string groupName, string serverName, DirectoryEntry groupEntry)
        {
            var success = true;
            string username = "svcgtw";
            string password = "7KJuswxQnLXwWM3znp";
            LoggerSingleton.SynchronizedLocalGroups.Info($"Deleting computer '{computerName}' from the group '{groupName}' on gateway '{serverName}'.");
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
                LoggerSingleton.SynchronizedLocalGroups.Error(ex, $"Error while adding member '{computerName}' to group '{groupName}' on gateway '{serverName}'.");
                success = false;
            }

            return success;
        }

        private bool SyncComputers(DirectoryEntry newGroup, LocalGroup lg, string server)
        {
            //string groupName = FormatModifiedValue(lg.Name);
            var success = true;
            var generalSuccess = true;
            var computersData = lg.ComputersObj.Names.Zip(lg.ComputersObj.Flags, (i, j) => new { Name = i, Flag = j });
            foreach (var computer in computersData)
            {
                if (computer.Flag == LocalGroupFlag.Delete)
                    success = success && DeleteComputer(server, lg.Name, computer.Name, newGroup);
                else if (computer.Flag == LocalGroupFlag.Add)
                    success = success && AddComputer(server, lg.Name, computer.Name, newGroup);
                generalSuccess = generalSuccess && success;
            }
            if (!generalSuccess) LoggerSingleton.SynchronizedLocalGroups.Error(server, $"Failed synchronizing computers for group: '{lg.Name}'.");
            return generalSuccess;
        }

        private void SyncModifiedGroups(string serverName, List<LocalGroup> modifiedGroups)
        {
            LoggerSingleton.General.Info(serverName, $"Synchronizing content of {modifiedGroups.Count} groups.");
            modifiedGroups.ForEach(lg => SyncGroupContent(lg, serverName));
        }

        private void SyncGroupContent(LocalGroup lg, string server)
        {
            LoggerSingleton.SynchronizedLocalGroups.Info(server, $"Synchronizing {lg.Name}.");
            var success = true;
            var localGroup = GetLocalGroup(server, lg.Name);
            if (CleanFromOrphanedSids(localGroup, lg, server)) // ovo popraviti jer nesto nije ok
            {
                if (!SyncMember(localGroup, lg, server))
                    success = false;

                if (!SyncComputers(localGroup, lg, server))
                    success = false;
            }
            else
            {
                LoggerSingleton.SynchronizedLocalGroups.Error($"Error while cleaning group: '{lg.Name}' from orphaned SIDs, further synchronization on this group is skipped.");
                success = false;
            }
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

        public bool CleanFromOrphanedSids(DirectoryEntry localGroup, LocalGroup lg, string serverName)
        {
            try
            {
                bool success;
                success = RemoveOrphanedSids(localGroup, lg);
                return success;
            }
            catch (Exception ex)
            {
                LoggerSingleton.SynchronizedLocalGroups.Error(ex, $"Error while removing orphaned SIDs from group: '{lg.Name}' on gateway: '{serverName}'.");
                return false;
            }
        }

        private bool RemoveOrphanedSids(DirectoryEntry groupPrincipal, LocalGroup lg)
        {
            var success = true;
            var globalSuccess = true;
            var membersData = lg.MembersObj.Names.Zip(lg.MembersObj.Flags, (i, j) => new { Name = i, Flag = j });

            foreach (var member in membersData)
            {
                if (!member.Name.StartsWith(Constants.OrphanedSid)) continue;
                try
                {
                    LoggerSingleton.SynchronizedLocalGroups.Info($"Removing SID: '{member.Name}'.");
                    groupPrincipal.Invoke("Remove", $"WinNT://{member.Name}");
                }
                catch (Exception ex)
                {
                    LoggerSingleton.SynchronizedLocalGroups.Error(ex, $"Failed removing SID: '{member.Name}'.");
                    success = false;
                }

                globalSuccess = globalSuccess && success;
            }

            return globalSuccess;
        }
    }
}
