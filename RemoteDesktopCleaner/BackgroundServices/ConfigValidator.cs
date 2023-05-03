using NLog;
using System.DirectoryServices;
using System.DirectoryServices.AccountManagement;
using RemoteDesktopCleaner.Data;
using System.Diagnostics;
using RemoteDesktopCleaner.Exceptions;


namespace RemoteDesktopCleaner.BackgroundServices
{
    public class ConfigValidator : IConfigValidator
    {
        private const string DomainName = "CERN";
        private static readonly Logger LoggerGeneral = LogManager.GetLogger("logfileGeneral");
        private static readonly Logger LoggerRaps = LogManager.GetLogger("logfileMarkedObsoleteRaps");
        private const string GlobalAdminGroup = @"CERN\NICE Local Administrators Managers";
        private const string PrimaryAccountGroup = @"CERN\cern-accounts-primary";
        private readonly List<string> _allowedNetworkDomains = new List<string> { "GPN", "LCG", "ITS", "CLOUD-EXP" };
        private const string username = "pstojkov";
        private const string password = "GeForce9800GT.";
        private PrincipalSearchResult<Principal> members;
        private PrincipalContext domainContext;


        public ConfigValidator()
        {
            domainContext = new PrincipalContext(ContextType.Domain, DomainName);

            LoggerGeneral.Info("Getting all Admin names");
            var niceLocalAdminGroupPrincipal = GroupPrincipal.FindByIdentity(domainContext, GlobalAdminGroup);
            if (niceLocalAdminGroupPrincipal != null)
            {
                members = niceLocalAdminGroupPrincipal.GetMembers(true);
            }
            else
            {
                members = null;
                throw new NoAccesToDomain();
            }
        }

        public bool MarkObsoleteData()
        {
            LoggerGeneral.Info("Started validation of  DB RAPs and corresponding Resources.");
            Console.WriteLine("Started validation of  DB RAPs and corresponding Resources.");
            var raps = new List<rap>();
            try
            {
                using (var db = new RapContext())
                { 
                    raps.AddRange(GetRaps(db));
                    LoggerGeneral.Info($"Queried {raps.Count} RAPs from DB. Starting validation.");
                    var i = 1;
                    using (var domainContext = new PrincipalContext(ContextType.Domain, DomainName))
                    {
                        IsEGroupInActiveDirectory(domainContext, PrimaryAccountGroup);
                    }
                    foreach (var rapRow in raps)
                    {
                        Console.Write($"\r{i}/{raps.Count} - {100 * i / raps.Count}% ");
                        i++;

                        if (!ValidateRap(rapRow.login))
                        {
                            LoggerRaps.Info($"Login '{rapRow.login}' not present in Active Directory, mark it for deletion.");
                            Console.WriteLine($"Login '{rapRow.login}' not present in Active Directory.");
                            MarkToDelete(rapRow);
                            foreach (var resource in rapRow.rap_resource)
                            {
                                LoggerRaps.Info($"Removing resources from Login '{rapRow.login}'.");
                                Console.WriteLine($"Removing resources from  Login '{rapRow.login}'.");
                                MarkToDelete(resource);
                            }
                        }
                        else
                        {
                            ValidateRapResources(rapRow);
                            if (HasNoValidResources(rapRow))
                            {
                                LoggerRaps.Info($"Login '{rapRow.login}' has no valid resources.");
                                Console.WriteLine($"Login '{rapRow.login}' has no valid resources, mark it for deletion.");
                                MarkToDelete(rapRow);
                            }
                        }
                    }

                    LoggerGeneral.Info("Finished validation of  DB RAPs and corresponding Resources.");
                    Console.WriteLine("Finished validation of  DB RAPs and corresponding Resources.");

                    db.SaveChanges();
                    domainContext.Dispose();
                    return true;
                }
            }
            catch (Exception ex)
            {
                LoggerGeneral.Fatal(ex, "Error while validating model configuration.");
                return false;
            }
        }
        private void IsEGroupInActiveDirectory(PrincipalContext domainContext, string groupName)
        {
            var primaryAccountsGroupPrincipal = new GroupPrincipal(domainContext, groupName);
            if (primaryAccountsGroupPrincipal == null)
            {
                var errorMessage = $"(10) E-Group '{groupName}' not found in Active Directory.";
                LoggerRaps.Warn(errorMessage);
                throw (new ValidatorException(errorMessage));
            }
        }

        private bool HasNoValidResources(rap rapRow)
        {
            return rapRow.rap_resource.All(res => res.toDelete || !res.access || res.invalid.HasValue && res.invalid.Value);
        }

        private void ValidateRapResources(rap rapRow)
        {
            foreach (var resource in rapRow.rap_resource)
            {
                var policyValidationResult = ValidatePolicy(rapRow.login, resource.resourceOwner, resource.resourceName, resource); // resource
                ConsumeValidationResult(rapRow, resource, policyValidationResult);
            }
        }
        private void ConsumeValidationResult(rap rapRow, rap_resource resource, PolicyValidationResult result)
        {
            if (result.FailureDetail == FailureDetail.ValidationException)
            {
                LoggerRaps.Warn($"RAP-Resource '{rapRow.name}'-'{resource.resourceName}' skipped: {result.Message}");
                return;
            }
            if (result.FailureDetail == FailureDetail.ComputerNotFound)
                MarkToDelete(resource);
            else if (result.FailureDetail == FailureDetail.LoginNotFound)
            {
                DisableResource(resource);
                SetRapEnabledFalseIfNoAccessibleResources(rapRow);
            }

            if (resource.invalid != result.Invalid)
                resource.invalid = result.Invalid;
        }
        private void SetRapEnabledFalseIfNoAccessibleResources(rap rap)
        {
            LoggerRaps.Warn("Setting Rap Enabled flag to False if there is not accessible resources");
            int accessibleResources =
                (from rapRes in rap.rap_resource
                 where rapRes.access
                 select rapRes).Count();
            if (accessibleResources == 0)
                rap.enabled = false;
        }
        private void MarkToDelete(rap_resource resource)
        {
            LoggerRaps.Info($"Marking resource '{resource.resourceName}' of RAP '{resource.RAPName}' to delete.");
            resource.toDelete = true;
        }
        private void DisableResource(rap_resource resource)
        {
            LoggerRaps.Info($"Disabling resource '{resource.resourceName}' of RAP '{resource.RAPName}'.");
            resource.access = false;
            resource.synchronized = false;
            // delete it
        }
        private PolicyValidationResult ValidatePolicy(string login, string rapOwner, string computerName, rap_resource resource)
        {
            UserPrincipal rapOwnerPrincipal = null;
           
            
            var validationResult = new PolicyValidationResult(false);
            try
            {

                rapOwner = RemoveDomainFromRapOwner(rapOwner);
                LoggerRaps.Info($"Validating user '{login}', RAP owner '{rapOwner}', computer '{computerName}'.");

                using (var domainContext = new PrincipalContext(ContextType.Domain, DomainName))
                {
                    ComputerExistsInActiveDirectory(computerName);
                    rapOwnerPrincipal = GetRapOwnerFromActiveDirectory(domainContext, rapOwner);

                    if (IsPolicyAnException(rapOwner, login, computerName, resource))
                        return new PolicyValidationResult(true);

                    Dictionary<string, string> deviceInfo = ExecutePowerShellScript(computerName, username, password);
                    bool bNetworkOk = CheckDeviceDomainInterfaces(deviceInfo);
                    bool isNiceMember = IsUserNiceGroupMember(domainContext, rapOwnerPrincipal);
                    if (!isNiceMember)
                    {
                        var msg = $"User Account '{login}' not found in the nice local administrator managers group.";
                        LoggerRaps.Warn(msg);
                    }
                    else if (bNetworkOk)
                        return new PolicyValidationResult(true);

                    bool isUserAllowed = IsRapOwnerResponsible(domainContext, rapOwnerPrincipal, deviceInfo);
                    switch (isUserAllowed)
                    {
                        case false:
                            var msg =
                                $"RAP owner '{rapOwner}' is not the responsible/user for the computer '{computerName}'.";
                            LoggerRaps.Warn(msg);
                            //messages.Append(msg);
                            break;
                        case true when bNetworkOk:
                            return new PolicyValidationResult(true);
                        default:
                            LoggerRaps.Warn($"Account '{rapOwner}' is not allowed to manage computer '{computerName}'.");
                            //throw new InvalidPolicyException();
                            break;
                    }
                }

            }
            catch (ValidatorException validatorEx)
            {
                LoggerRaps.Warn($"Error while validating policy: {validatorEx.Message}.");
                validationResult.FailureDetail = FailureDetail.ValidationException;
            }
            catch (ComputerNotFoundInActiveDirectoryException ex)
            {
                validationResult.FailureDetail = FailureDetail.ComputerNotFound;
            }
            catch (LoginNotFoundInActiveDirectoryException ex)
            {
                validationResult.FailureDetail = FailureDetail.LoginNotFound;

            }
            catch (Exception ex)
            {
                LoggerRaps.Warn("Unexpected exception occurred, unable to validate.");
                validationResult.FailureDetail = FailureDetail.ValidationException;
                LoggerRaps.Error(ex.ToString());
            }
            finally
            {
                rapOwnerPrincipal?.Dispose();
            }

            return validationResult;
        }

        private bool IsRapOwnerResponsible(PrincipalContext domainContext, UserPrincipal rapOwnerPrincipal, Dictionary<string, string> deviceInfo)
        {
            return IsRapOwnerResponsibleGroupMember(domainContext, rapOwnerPrincipal, deviceInfo) || RapOwnerOrUserResponsible(rapOwnerPrincipal, deviceInfo);
        }

        private bool IsRapOwnerResponsibleGroupMember(PrincipalContext domainContext, UserPrincipal rapOwnerPrincipal, Dictionary<string, string> device)
        {
            var responsibleGroup = GroupPrincipal.FindByIdentity(domainContext, device["ResponsiblePersonName"]);
            if (responsibleGroup == null) return false;
            return responsibleGroup.GetMembers()
                .Where(member => member.UserPrincipalName != null)
                .Any(member => member.UserPrincipalName.Equals(rapOwnerPrincipal.EmailAddress, StringComparison.OrdinalIgnoreCase));
        }

        private bool RapOwnerOrUserResponsible(UserPrincipal rapOwnerPrincipal, Dictionary<string, string> oDevice)
        {
            return CheckPerson(oDevice["ResponsiblePersonEmail"], rapOwnerPrincipal.EmailAddress) || CheckPerson(oDevice["ResponsiblePersonEmail"], rapOwnerPrincipal.EmailAddress);
        }

        private bool CheckPerson(string email, string rapOwnerEmail)
        {
            return email != null && email.Equals(rapOwnerEmail, StringComparison.OrdinalIgnoreCase);
        }

        private bool CheckDeviceDomainInterfaces(Dictionary<string, string> DeviceInfo)
        {
            // Check for computers domain interfaces. Only computers that have access to GPN or LCG domain are allowed to be managed.
            try
            {
                if (DeviceInfo == null) throw new ArgumentNullException(nameof(DeviceInfo));
                var oInterfaces = DeviceInfo["Interfaces"];

                if (!string.IsNullOrEmpty(DeviceInfo["Interfaces"]))
                { // dodati da bude lista
                    LoggerRaps.Debug($"Checking interfaces in case of anomaly: {DeviceInfo["Interfaces"]}");
                    if (IsNetworkDomainNameAllowed(DeviceInfo["NetworkDomainName"])) // checks if condition is fullfiled
                        return true;
                }
                else // If interfaces is null, then computer belongs to the GPN domain
                    return true;
            }
            catch (Exception)
            {
                var errorMessage = "Unable to connect to the LAN WebService, computer validation couldn't be done.";
                LoggerRaps.Warn("Unable to connect to the LAN WebService, computer validation couldn't be done.");
                throw new ValidatorException(errorMessage);
                //return false;
            }

            return false;
        }

        private bool IsUserNiceGroupMember(PrincipalContext domainContext, UserPrincipal rapOwnerPrincipal)
        {
            if (members != null)
            {
           
                if (members.Contains(rapOwnerPrincipal)) // videti sto ovo ne radi
                    return true;
            }

            //Check if the RAP Owner is not member of Nice Group Manager and is not a primary account
            if (!rapOwnerPrincipal.IsMemberOf(domainContext, IdentityType.SamAccountName, PrimaryAccountGroup))
            {
                return false;
            }
            var errorMessage =
                $"Rap owner '{rapOwnerPrincipal.SamAccountName}' does not belong to primary accounts group.";
            LoggerRaps.Warn(errorMessage);
            throw (new ValidatorException(errorMessage));
            //return false;
        }

        private bool IsNetworkDomainNameAllowed(string NetworkDomainName)
        {
            if (NetworkDomainName.Length == 0) return false;
            return _allowedNetworkDomains.Contains(NetworkDomainName.ToUpper());
        }

        static Dictionary<string, string> ExecutePowerShellScript(string setName, string userName, string password)
        {
            try
            {
                // Set the path to the script file
                string scriptPath = $@"{Directory.GetParent(Environment.CurrentDirectory).Parent.Parent.FullName}\PowerShellScripts\SOAPNetworkService.ps1";
                // Create a new ProcessStartInfo object with the necessary parameters
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-ExecutionPolicy Bypass -File \"{scriptPath}\" -SetName1 \"{setName}\" -UserName1 \"{userName}\" -Password1 \"{password}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                // Start the process and read the output
                using Process process = new Process { StartInfo = startInfo };
                process.Start();

                string output = process.StandardOutput.ReadToEnd();
                string errors = process.StandardError.ReadToEnd();

                if (output.Length == 0 || errors.Length > 0) throw new ComputerNotFoundInActiveDirectoryException();

                Dictionary<string, string> result = ConvertStringToDictionary(output);
                process.WaitForExit();

                return result;
            }
            catch (ComputerNotFoundInActiveDirectoryException ex)
            {
                Console.WriteLine("Unable to use SOAP operations");
                LoggerGeneral.Fatal("Unable to use SOAP operations");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                return null;
            }
        }

        private bool IsPolicyAnException(string rapOwner, string userName, string computerName, rap_resource resource)
        {
            if (resource?.exception == null || !resource.exception.Value) return false;
            var message = $"RAP owner '{rapOwner}' and computer '{computerName}' is an exception.";
            LoggerRaps.Warn(message);
            return true;
        }

        private UserPrincipal GetRapOwnerFromActiveDirectory(PrincipalContext domainContext, string rapOwner)
        {
            var rapOwnerPrincipal = UserPrincipal.FindByIdentity(domainContext, rapOwner);
            if (rapOwnerPrincipal != null) return rapOwnerPrincipal;

            var errorMessage = $"RAP owner '{rapOwner}' not found in Active Directory.";
            //return null;
            LoggerRaps.Warn(errorMessage);
            throw new ValidatorException(errorMessage);
        }

        private string RemoveDomainFromRapOwner(string rapOwner)
        {
            if (rapOwner.StartsWith(DomainName))
            {
                return rapOwner.Substring(DomainName.Length + 1);
            }
            else
            {
                return rapOwner;
            }
        }

        public static Dictionary<string, string> ConvertStringToDictionary(string input)
        {
            Dictionary<string, string> result = new Dictionary<string, string>();
            string[] lines = input.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string line in lines)
            {
                int separatorIndex = line.IndexOf(':');
                if (separatorIndex > 0)
                {
                    string key = line.Substring(0, separatorIndex).Trim();
                    string value = line.Substring(separatorIndex + 1).Trim();
                    result[key] = value;
                }
            }

            return result;
        }

        private void ComputerExistsInActiveDirectory(string computerName)
        {
            var domainEntry = new DirectoryEntry("LDAP://cern.ch/DC=cern,DC=ch");
            var domainSearcher = new DirectorySearcher(domainEntry);
            domainSearcher.Filter = $"(&(cn={computerName})(objectClass=computer)(objectCategory=computer))";
            domainSearcher.PropertiesToLoad.Add("cn"); // debug only
            var computerResult = domainSearcher.FindOne();
            if (computerResult == null)
            {
                LoggerRaps.Warn($"Computer '{computerName}' not found in Active Directory.");
                throw new ComputerNotFoundInActiveDirectoryException();
            }
        }

        private IEnumerable<rap> GetRaps(RapContext db)
        {
            var results = new List<rap>();
            try
            {
                results.AddRange(db.raps.Include("rap_resource").ToList());
            }
            catch (Exception)
            {
                LoggerGeneral.Fatal("Failed query.");
                Console.WriteLine("Failed query.");
            }
            return results;
        }

        private bool ValidateRap(string login)
        {
            return IsUserInActiveDirectory(login) || IsGroupInActiveDirectory(login);
        }
        private bool IsUserInActiveDirectory(string login)
        {
            UserPrincipal result;
            try
            {
                using (var context = new PrincipalContext(ContextType.Domain, DomainName))
                {
                    result = UserPrincipal.FindByIdentity(context, login);
                }
                return result != null;
            }
            catch
            {
                LoggerRaps.Warn($"User {login} is not in Active Directory");
                return false;
            }
               
        }
        private bool IsGroupInActiveDirectory(string login)
        {
            GroupPrincipal result;
            try
            {
                using (var context = new PrincipalContext(ContextType.Domain))
                {
                    result = GroupPrincipal.FindByIdentity(context, login);
                }
                return result != null;
            }
            catch
            {
                LoggerRaps.Warn($"Group {login} is not in Active Directory");
                return false;
            }
        }

        private void MarkToDelete(rap rapRow)
        {
            LoggerRaps.Info($"Marking rap '{rapRow.login}' to delete.");
            rapRow.toDelete = true;
        }

    }
}
