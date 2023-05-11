using System.DirectoryServices;
using System.Management;
using Microsoft.Management.Infrastructure;
using Microsoft.Management.Infrastructure.Options;
using System.Security;
using System.Text.Json;
using System.Text;
using RemoteDesktopCleaner.Loggers;
using RemoteDesktopCleaner.Caching;


namespace RemoteDesktopCleaner.BackgroundServices
{
    public class GatewayRapSynchronizer : IGatewayRapSynchronizer
    {
        private const string AdSearchGroupPath = "WinNT://{0}/{1},group";
        private const string NamespacePath = @"\root\CIMV2\TerminalServices";
        private readonly DirectoryEntry _rootDir = new DirectoryEntry("LDAP://DC=cern,DC=ch");

        public GatewayRapSynchronizer() { }

        public List<string> GetGatewaysRapNamesAsync(string serverName)
        {
            LoggerSingleton.General.Info($"Getting RAP/Policy names from gateway '{serverName}'.");
            LoggerSingleton.SynchronizedRaps.Info($"Getting RAP/Policy names from gateway '{serverName}'.");
            try
            {
                return GetRapNamesAsync(serverName);
            }
            catch (Exception ex)
            {
                LoggerSingleton.SynchronizedRaps.Error(ex, $"Failed getting RAP/Policy names from gateway '{serverName}'.");
                LoggerSingleton.General.Error(ex, $"Failed getting RAP/Policy names from gateway '{serverName}'.");
                throw;
            }
        }
        public List<string> GetRapNamesAsync(string serverName)
        {
            bool cacheFlag = true;
            if (cacheFlag)
                return Cacher.LoadCacheFromFile();
            else
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
            string _oldGatewayServerHost = $@"\\{serverName}.cern.ch";
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
                LoggerSingleton.SynchronizedRaps.Info($"Started querying RAP/Policy names from gateway '{serverName}'.");
                var i = 1;
                foreach (CimInstance x in queryInstance) 
                {
                    var policyName = x.CimInstanceProperties["Name"].Value.ToString();
                    LoggerSingleton.SynchronizedRaps.Debug($"{i} - Querying RAP/Policy {policyName} from gateway '{serverName}'.");
                    rapNames.Add(policyName);
                    i++;
                }
                LoggerSingleton.SynchronizedRaps.Info($"Finished querying RAP/Policy names from gateway '{serverName}'.");

                Cacher.SaveCacheToFile(rapNames);
                return rapNames;
            }
            catch (Exception ex)
            {
                LoggerSingleton.General.Fatal(ex, $"Error while getting rap names from gateway: '{serverName}'. Ex: {ex}");
                LoggerSingleton.SynchronizedRaps.Fatal(ex, $"Error while getting rap names from gateway: '{serverName}'. Ex: {ex}");
            }

            return new List<string>();
        }

        public void SynchronizeRaps(string serverName, List<string> allGatewayGroups, List<string> toDeleteGatweayGroups, List<string> gatewayRaps)
        {
            LoggerSingleton.General.Info($"Starting Policy synchronisation of server: '{serverName}'.");
            LoggerSingleton.SynchronizedRaps.Info($"Starting Policy synchronisation of server: '{serverName}'.");
            var modelRapNamesAdd = allGatewayGroups.Select(LgNameToRapName).ToList();
            var modelRapNamesDelete = toDeleteGatweayGroups.Select(LgNameToRapName).ToList();
            AddMissingRaps(serverName, modelRapNamesAdd, gatewayRaps);
            DeleteObsoleteRaps(serverName, modelRapNamesDelete, gatewayRaps);
            LoggerSingleton.General.Info($"Finished Policy synchronisation of server: '{serverName}'.");
            LoggerSingleton.SynchronizedRaps.Info($"Finished Policy synchronisation of server: '{serverName}'.");
        }
        private static string LgNameToRapName(string lgName)
        {
            return lgName.Replace("LG-", "RAP_");
        }
        private void DeleteObsoleteRaps(string serverName, List<string> modelRapNames, List<string> gatewayRaps)
        {
            var obsoleteRapNames = gatewayRaps.Intersect(modelRapNames).ToList();
            //_reporter.SetShouldDeleteRaps(serverName, obsoleteRapNames.Count);
            LoggerSingleton.General.Info($"Server:{serverName} Deleting {obsoleteRapNames.Count} RAPs from the gateway.");
            LoggerSingleton.SynchronizedRaps.Info($"Deleting {obsoleteRapNames.Count} RAPs from the gateway '{serverName}'.");
            if (obsoleteRapNames.Count > 0)
                TryDeletingRaps(serverName, obsoleteRapNames);
            //_reporter.Info(serverName, "Finished deleting RAPs.");
            LoggerSingleton.General.Info($"Finished deleting RAPs from the gateway '{serverName}'.");
            LoggerSingleton.SynchronizedRaps.Info($"Finished deleting RAPs from the gateway '{serverName}'.");
        }

        private void AddMissingRaps(string serverName, List<string> modelRapNames, List<string> gatewayRapNames)
        {
            var missingRapNames = modelRapNames.Except(gatewayRapNames).ToList();
            //_reporter.SetShouldAddRaps(serverName, missingRapNames.Count);
            //_reporter.Info(serverName, $"Adding {missingRapNames.Count} RAPs to the gateway.");
            LoggerSingleton.General.Info($"Adding {missingRapNames.Count} RAPs to the gateway '{serverName}'.");
            LoggerSingleton.SynchronizedRaps.Info($"Adding {missingRapNames.Count} RAPs to the gateway '{serverName}'.");
            AddMissingRaps(serverName, missingRapNames);
            //_reporter.Info(serverName, "Finished adding RAPs.");
            LoggerSingleton.General.Info($"Finished adding {missingRapNames.Count} RAPs to the gateway '{serverName}'.");
            LoggerSingleton.SynchronizedRaps.Info($"Finished adding {missingRapNames.Count} RAPs to the gateway '{serverName}'.");
        }
        private void TryDeletingRaps(string serverName, List<string> obsoleteRapNames)
        {
            bool finished = false;
            int counter = 0;
            var toDelete = new List<string>(obsoleteRapNames);
            while (!(counter == 3 || finished))
            {
                if (toDelete.Count == 0) break;
                var response = DeleteRapsFromGateway(serverName, toDelete);
                Console.WriteLine($"Deleting raps, try #{counter + 1}"); //TODO delete
                LoggerSingleton.SynchronizedRaps.Debug($"Deleting raps, try #{counter + 1}");
                foreach (var res in response)
                {
                    if (res.Deleted)
                    {
                        //_reporter.IncrementDeletedRaps(serverName);
                        Console.WriteLine($"Deleted '{res.RapName}'.");
                        LoggerSingleton.SynchronizedRaps.Debug($"Deleted '{res.RapName}'.");
                        if (toDelete.Count == 0) finished = true;
                    }
                    else
                    {
                        Console.WriteLine($"Failed deleting '{res.RapName}'."); //TODO delete
                        LoggerSingleton.SynchronizedRaps.Error($"Failed deleting '{res.RapName}'.");
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
                oConn.Username = "svcgtw";
                oConn.Password = "7KJuswxQnLXwWM3znp";
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
                    LoggerSingleton.SynchronizedRaps.Info($"Adding new RAP '{rapName}' to the gateway '{serverName}'.");
                    var groupName = ConvertToLgName(rapName);
                    inParameters["Name"] = "" + rapName;
                    inParameters["ResourceGroupName"] = groupName;
                    inParameters["UserGroupNames"] = groupName;

                    ManagementBaseObject outParameters = processClass.InvokeMethod("Create", inParameters, imo);

                    if ((uint)outParameters["ReturnValue"] == 0)
                    {
                        Console.WriteLine($"{rapName} created. {++i}/{missingRapNames.Count}"); //TODO delete
                        LoggerSingleton.SynchronizedRaps.Info($"RAP '{rapName}' added to the gateway '{serverName}'.");
                        //_reporter.IncrementAddedRaps(serverName);
                    }
                    else
                    {
                        if ((uint)outParameters["ReturnValue"] == 2147749913)
                            LoggerSingleton.SynchronizedRaps.Warn($"Error creating RAP: '{rapName}'. Reason: Already exists.");
                        else
                            LoggerSingleton.SynchronizedRaps.Error($"Error creating RAP: '{rapName}'. Reason: {(uint)outParameters["ReturnValue"]}.");
                    }
                }
                return true;
            }
            catch (System.Exception ex)
            {
                LoggerSingleton.SynchronizedRaps.Error(ex, $"Error when adding new RAPs to the gateway '{serverName}'.");
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
            var username = "svcgtw"; // replace with your username
            var password = "7KJuswxQnLXwWM3znp"; // replace with your password
            var securepassword = new SecureString();
            foreach (char c in password)
                securepassword.AppendChar(c);
            const string AdSearchGroupPath = "WinNT://{0}/{1},group";
            const string NamespacePath = @"\root\CIMV2\TerminalServices";
            string _oldGatewayServerHost = $@"\\{serverName}.cern.ch";
            try
            {
                string where = CreateWhereClause(rapNamesToDelete);
                //string osQuery =
                //    "SELECT * FROM Win32_TSGatewayResourceAuthorizationPolicy " + where;
                string osQuery = "SELECT * FROM Win32_TSGatewayResourceAuthorizationPolicy";

                CimCredential Credentials = new CimCredential(PasswordAuthenticationMechanism.Default, "cern.ch", username, securepassword);

                WSManSessionOptions SessionOptions = new WSManSessionOptions();
                SessionOptions.AddDestinationCredentials(Credentials);
                CimSession mySession = CimSession.Create(serverName, SessionOptions);
                IEnumerable<CimInstance> queryInstance = mySession.QueryInstances(_oldGatewayServerHost + NamespacePath, "WQL", osQuery);
                IEnumerable<CimInstance> filteredInstances = queryInstance.Where(instance => rapNamesToDelete.Contains(instance.CimInstanceProperties["Name"].Value.ToString()));
                foreach (CimInstance rapInstance in filteredInstances)
                {
                    var rapName = rapInstance.CimInstanceProperties["Name"].Value.ToString();
                    if (!rapNamesToDelete.Contains(rapName)) continue;
                    var rapDeletion = DeleteRap(mySession, rapInstance, rapName);
                    result.Add(rapDeletion);
                }
            }
            catch (Exception ex)
            {
                LoggerSingleton.SynchronizedRaps.Error($"Error while getting rap names from gateway: '{serverName}'.Ex: {ex}");
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
                    LoggerSingleton.SynchronizedRaps.Info($"Deleted RAP '{rapName}'.");
                }
            }
            catch (CimException ex) // catch only CIM exceptions
            {
                if (ex.Message.Contains("NotFound")) // check if the error message contains "NotFound"
                {
                    LoggerSingleton.SynchronizedRaps.Warn($"Could not find RAP '{rapName}' to delete."); // log a warning
                    rapDeletion.Deleted = false;
                }
                else // handle all other CIM exceptions
                {
                    LoggerSingleton.SynchronizedRaps.Error(ex, $"Error deleting RAP '{rapName}'.");
                }
            }
            catch (Exception ex)
            {
                LoggerSingleton.SynchronizedRaps.Error(ex, $"Error deleting rap '{rapName}'.");
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

