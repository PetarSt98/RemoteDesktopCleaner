﻿using System.DirectoryServices;
using System.DirectoryServices.AccountManagement;
using SynchronizerLibrary.Data;
using RemoteDesktopCleaner.Exceptions;
using SynchronizerLibrary.Loggers;
using SynchronizerLibrary.SOAPservices;
using RemoteDesktopCleaner.BackgroundServices.ConfigurationValidation;

namespace RemoteDesktopCleaner.BackgroundServices
{
    public class ConfigValidator : IConfigValidator
    {
        private const string DomainName = "CERN";
        private const string GlobalAdminGroup = @"CERN\NICE Local Administrators Managers";
        private const string PrimaryAccountGroup = @"CERN\cern-accounts-primary";
        private readonly List<string> _allowedNetworkDomains = new List<string> { "GPN", "LCG", "ITS", "CLOUD-EXP" };
        private const string username = "pstojkov";
        private const string password = "GeForce9800GT.";
        private PrincipalSearchResult<Principal> members;
        private PrincipalContext domainContext;
        private CleaningEmailingService cleaningEmailingService;

        public ConfigValidator()
        {
            try
            {
                domainContext = new PrincipalContext(ContextType.Domain, DomainName);

                LoggerSingleton.General.Info("Getting all Admin names");
                var niceLocalAdminGroupPrincipal = GroupPrincipal.FindByIdentity(domainContext, GlobalAdminGroup);
                if (niceLocalAdminGroupPrincipal != null)
                {
                    members = niceLocalAdminGroupPrincipal.GetMembers(true);
                }
                else
                {
                    members = null;
                    LoggerSingleton.General.Fatal("Unable to reach Domain.");
                    throw new NoAccesToDomain();
                }
            }
            catch (Exception ex)
            {
                members = null;
            }
        }

        #region Mark obsolete data in database
        private void IsEGroupInActiveDirectory(PrincipalContext domainContext, string groupName)
        {
            var primaryAccountsGroupPrincipal = new GroupPrincipal(domainContext, groupName);
            if (primaryAccountsGroupPrincipal == null)
            {
                var errorMessage = $"(10) E-Group '{groupName}' not found in Active Directory.";
                LoggerSingleton.Raps.Warn(errorMessage);
                throw (new ValidatorException(errorMessage));
            }
        }

        public bool MarkObsoleteData()
        {
            bool cacheFlag = false;
            if (cacheFlag)
            {
                Console.WriteLine("Skipping (using cached) Data Base");
                return true;
            }
            LoggerSingleton.General.Info("Started validation of  DB RAPs and corresponding Resources.");
            Console.WriteLine("Started validation of DB RAPs and corresponding Resources.");
            var raps = new List<rap>();
            try
            {
                using (var db = new RapContext())
                {
                    raps.AddRange(GetRaps(db));
                    LoggerSingleton.General.Info($"Queried {raps.Count} RAPs from DB. Starting validation.");
                    var i = 1;
                    using (var domainContext = new PrincipalContext(ContextType.Domain, DomainName))
                    {
                        IsEGroupInActiveDirectory(domainContext, PrimaryAccountGroup);
                    }
                    foreach (var rapRow in raps)
                    {
                        LoggerSingleton.Raps.Debug($"{i} - Rap login to be processed {rapRow.login}");
                        Console.Write($"\r{i}/{raps.Count} - {100 * i / raps.Count}% ");
                        i++;

                        if (!ValidateRap(rapRow.login))
                        {
                            LoggerSingleton.Raps.Info($"Login '{rapRow.login}' not present in Active Directory, mark it for deletion.");
                            Console.WriteLine($"Login '{rapRow.login}' not present in Active Directory.");
                            MarkToDelete(rapRow);
                            foreach (var resource in rapRow.rap_resource)
                            {
                                LoggerSingleton.Raps.Info($"Removing resources from Login '{rapRow.login}'.");
                                Console.WriteLine($"Removing resources from  Login '{rapRow.login}'.");
                                MarkToDelete(resource);
                            }
                        }
                        else
                        {
                            ValidateRapResources(rapRow);
                            if (false && HasNoValidResources(rapRow)) // Temp disabling this step till confirmed if needed
                            {
                                LoggerSingleton.Raps.Info($"Login '{rapRow.login}' has no valid resources.");
                                Console.WriteLine($"Login '{rapRow.login}' has no valid resources, mark it for deletion.");
                                MarkToDelete(rapRow);
                            }
                        }
                    }

                    LoggerSingleton.General.Info("Finished validation of  DB RAPs and corresponding Resources.");
                    Console.WriteLine("Finished validation of  DB RAPs and corresponding Resources.");

                    db.SaveChanges();

                    try
                    {
                        NotifyAdmins(db);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to send cleaning report email: {ex.Message}");
                    }

                    domainContext.Dispose();
                    return true;
                }
            }
            catch (Exception ex)
            {
                LoggerSingleton.General.Fatal(ex, "Error while validating model configuration.");
                return false;
            }
        }

        private void NotifyAdmins(RapContext db)
        {
            var policiesToBeDeleted = db.raps.Where(r => (r.toDelete)).Select(r => r.name).ToList();
            var resourcesUsersToBeDeleted = db.rap_resource.Where(rr => (rr.toDelete)).Select(rr => rr.RAPName).ToList();
            var resourcesComputersToBeDeleted = db.rap_resource.Where(rr => (rr.toDelete)).Select(rr => rr.resourceName).ToList();

            cleaningEmailingService = new CleaningEmailingService(policiesToBeDeleted, CleaningEmailingService.FileType.RAP);
            string reportRap = cleaningEmailingService.GenerateTextFile();

            cleaningEmailingService = new CleaningEmailingService(resourcesUsersToBeDeleted, resourcesComputersToBeDeleted, CleaningEmailingService.FileType.RAP_Resource);
            string reportRapResource = cleaningEmailingService.GenerateTextFile();

            cleaningEmailingService.ConcatenateReports(reportRap, reportRapResource);
            cleaningEmailingService.SendEmail();
        }

        #endregion

        #region Validate RAP_Resources
        private void ValidateRapResources(rap rapRow)
        {
            PolicyValidationResult policyValidationResult;

            foreach (var resource in rapRow.rap_resource)
            {
                LoggerSingleton.Raps.Debug($"Rap {rapRow.login} resource to be processed: owner{resource.resourceOwner}, name: {resource.resourceName}");
                if (CheckResourceValidity(resource))
                {
                    policyValidationResult = ValidatePolicy(rapRow.login, resource.resourceOwner, resource.resourceName, resource);
                    
                }
                else
                {
                    LoggerSingleton.Raps.Warn($"RAP_Resource {resource.RAPName} {resource.resourceName} has invalid data");
                    policyValidationResult = new PolicyValidationResult(true, FailureDetail.LoginNotFound);
                }
                ConsumeValidationResult(rapRow, resource, policyValidationResult);
            }
        }

        private bool CheckResourceValidity(rap_resource resource)
        {
            if (resource == null) return false;
            if (resource.resourceOwner == null) return false;
            if (resource.RAPName == null) return false;
            if (resource.resourceName == null) return false;
            return true;
        }

        private void ConsumeValidationResult(rap rapRow, rap_resource resource, PolicyValidationResult result)
        {
            if (result.FailureDetail == FailureDetail.NoFailure)
            {
                resource.invalid = false;
                return;
            }

            if (result.FailureDetail == FailureDetail.ValidationException)
            {
                if (result.Invalid)
                {
                    LoggerSingleton.Raps.Warn($"RAP-Resource '{rapRow.name}'-'{resource.resourceName}' marked as invalid: {result.Message}");
                    resource.invalid = result.Invalid;
                }
                else
                    LoggerSingleton.Raps.Info($"RAP-Resource '{rapRow.name}'-'{resource.resourceName}' skipped: {result.Message}");
                return;
            }
            if (result.FailureDetail == FailureDetail.ComputerNotFound)
                MarkToDelete(resource);
            else if (result.FailureDetail == FailureDetail.LoginNotFound)
            {
                DisableResource(resource);
                SetRapEnabledFalseIfNoAccessibleResources(rapRow); // Nice feature but overengineered
            }

            if (resource.invalid != result.Invalid)
                resource.invalid = result.Invalid;
        }
        private void SetRapEnabledFalseIfNoAccessibleResources(rap rap)
        {
            LoggerSingleton.Raps.Warn("Setting Rap Enabled flag to False if there is not accessible resources");
            int accessibleResources =
                (from rapRes in rap.rap_resource
                 where rapRes.access
                 select rapRes).Count();
            if (accessibleResources == 0)
                rap.enabled = false;
        }

        #region Validate Policy
        private PolicyValidationResult ValidatePolicy(string login, string rapOwner, string computerName, rap_resource resource)
        {
            UserPrincipal rapOwnerPrincipal = null;
            var validationResult = new PolicyValidationResult(false);
            try
            {

                rapOwner = RemoveDomainFromRapOwner(rapOwner);
                LoggerSingleton.Raps.Info($"Validating user '{login}', RAP owner '{rapOwner}', computer '{computerName}'.");

                using (var domainContext = new PrincipalContext(ContextType.Domain, DomainName))
                {
                    ComputerExistsInActiveDirectory(computerName);
                    
                    // Checks if owner is valid, if not, marks device as invalid. Pablo said to remove this as it is not needed
                    // GetRapOwnerFromActiveDirectory(domainContext, rapOwner);

                    validationResult.Invalid = false;
                    validationResult.Message = "All ok";
                    validationResult.FailureDetail = FailureDetail.NoFailure;

                    // Checks if owner is valid, if not, marks device as invalid. Pablo said to remove this as it is not needed

                    //if (IsPolicyAnException(rapOwner, login, computerName, resource))
                    //    return new PolicyValidationResult(true);

                    //Dictionary<string, string> deviceInfo = Task.Run(() => SOAPMethods.ExecutePowerShellSOAPScript(computerName, rapOwner.Replace("RAP_", ""))).Result;

                    //bool bNetworkOk = CheckDeviceDomainInterfaces(deviceInfo);
                    //bool isNiceMember = IsUserNiceGroupMember(domainContext, rapOwnerPrincipal);
                    //if (!isNiceMember)
                    //{
                    //    var msg = $"User Account '{login}' not found in the nice local administrator managers group.";
                    //    LoggerSingleton.Raps.Warn(msg);
                    //    Console.WriteLine(msg);
                    //}
                    //else if (bNetworkOk)
                    //    return new PolicyValidationResult(false, "Good resource");

                    //bool isUserAllowed = IsRapOwnerResponsible(domainContext, rapOwnerPrincipal, deviceInfo);
                    //switch (isUserAllowed)
                    //{
                    //    case false:
                    //        var msg =
                    //            $"RAP owner '{rapOwner}' is not the responsible/user for the computer '{computerName}'.";
                    //        LoggerSingleton.Raps.Warn(msg);
                    //        break;
                    //    //case true when bNetworkOk:
                    //    //    return new PolicyValidationResult(false, "Rap owner is responsible, network domain name allowed ");
                    //    //case true when !bNetworkOk:
                    //    //    return new PolicyValidationResult(true, "Rap owner is responsible, network domain name not allowed ");
                    //    case true:
                    //        return new PolicyValidationResult(true, "Rap owner is responsible");
                    //    default:
                    //        LoggerSingleton.Raps.Warn($"Account '{rapOwner}' is not allowed to manage computer '{computerName}'.");
                    //        //throw new InvalidPolicyException();
                    //        break;
                    //}

                }

            }
            catch (ValidatorException validatorEx)
            {
                LoggerSingleton.Raps.Warn($"Warning while validating policy: {validatorEx.Message}");
                validationResult.FailureDetail = FailureDetail.ValidationException;
                validationResult.Invalid = true;
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
                LoggerSingleton.Raps.Warn("Unexpected exception occurred, unable to validate.");
                validationResult.FailureDetail = FailureDetail.ValidationException;
                LoggerSingleton.Raps.Error(ex.ToString());
            }
            finally
            {
                rapOwnerPrincipal?.Dispose();
            }

            return validationResult;
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

        public static void ComputerExistsInActiveDirectory(string computerName)
        {
            var domainEntry = new DirectoryEntry("LDAP://cern.ch/DC=cern,DC=ch");
            var domainSearcher = new DirectorySearcher(domainEntry);
            domainSearcher.Filter = $"(&(cn={computerName})(objectClass=computer)(objectCategory=computer))";
            domainSearcher.PropertiesToLoad.Add("cn"); // debug only
            var computerResult = domainSearcher.FindOne();
            if (computerResult == null)
            {
                LoggerSingleton.Raps.Warn($"Computer '{computerName}' not found in Active Directory.");
                throw new ComputerNotFoundInActiveDirectoryException();
            }
        }

        private bool IsPolicyAnException(string rapOwner, string userName, string computerName, rap_resource resource)
        {
            if (resource?.exception == null || !resource.exception.Value) return false;
            var message = $"RAP owner '{rapOwner}' and computer '{computerName}' is an exception.";
            LoggerSingleton.Raps.Warn(message);
            return true;
        }

        private void GetRapOwnerFromActiveDirectory(PrincipalContext domainContext, string rapOwner)
        {
            try
            {
                var rapOwnerPrincipal = UserPrincipal.FindByIdentity(domainContext, rapOwner);
                if (rapOwnerPrincipal != null) return;
                var rapOwnerPrincipalGroup = GroupPrincipal.FindByIdentity(domainContext, rapOwner);
                if (rapOwnerPrincipalGroup != null) return;


            }
            catch (Exception ex)
            {
                LoggerSingleton.Raps.Warn(ex.Message);
                Console.WriteLine(ex.Message);
            }
            var errorMessage = $"RAP owner '{rapOwner}' not found in Active Directory.";
            LoggerSingleton.Raps.Warn(errorMessage);
            throw new ValidatorException(errorMessage);
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
            try
            {
                if (DeviceInfo == null) throw new ArgumentNullException(nameof(DeviceInfo));
                var oInterfaces = DeviceInfo["Interfaces"];

                if (!string.IsNullOrEmpty(DeviceInfo["Interfaces"]))
                {
                    LoggerSingleton.Raps.Debug($"Checking interfaces in case of anomaly: {DeviceInfo["Interfaces"]}");
                    if (IsNetworkDomainNameAllowed(DeviceInfo["NetworkDomainName"]))
                        return true;
                }
                else
                    return true;
            }
            catch (Exception)
            {
                var errorMessage = "Unable to connect to the LAN WebService, computer validation couldn't be done.";
                LoggerSingleton.Raps.Error("Unable to connect to the LAN WebService, computer validation couldn't be done.");
                throw new ValidatorException(errorMessage);
            }

            return false;
        }

        private bool IsUserNiceGroupMember(PrincipalContext domainContext, UserPrincipal rapOwnerPrincipal)
        {
            if (members != null)
            {

                if (members.Contains(rapOwnerPrincipal))
                    return true;
            }

            if (rapOwnerPrincipal.IsMemberOf(domainContext, IdentityType.SamAccountName, PrimaryAccountGroup))
            {
                return false;
            }
            var errorMessage =
                $"Rap owner '{rapOwnerPrincipal.SamAccountName}' does not belong to primary accounts group.";
            LoggerSingleton.Raps.Warn(errorMessage);
            throw (new ValidatorException(errorMessage));
        }

        private bool IsNetworkDomainNameAllowed(string NetworkDomainName)
        {
            if (NetworkDomainName.Length == 0) return false;
            return _allowedNetworkDomains.Contains(NetworkDomainName.ToUpper());
        }

        #endregion

        #endregion

        #region Validating RAPs
        public static bool ValidateRap(string login)
        {
            return IsUserInActiveDirectory(login) || IsGroupInActiveDirectory(login);
        }
        public static bool IsUserInActiveDirectory(string login)
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
                LoggerSingleton.Raps.Warn($"User {login} is not in Active Directory");
                return false;
            }
               
        }
        public static bool IsGroupInActiveDirectory(string login)
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
                LoggerSingleton.Raps.Warn($"Group {login} is not in Active Directory");
                return false;
            }
        }
        #endregion

        #region Database operations
        private void MarkToDelete(rap rapRow)
        {
            LoggerSingleton.Raps.Info($"Marking rap '{rapRow.login}' to delete.");
            rapRow.toDelete = true;
        }

        private bool HasNoValidResources(rap rapRow)
        {
            return rapRow.rap_resource.All(res => res.toDelete || !res.access || res.invalid.HasValue && res.invalid.Value);
        }

        private void MarkToDelete(rap_resource resource)
        {
            LoggerSingleton.Raps.Info($"Marking resource '{resource.resourceName}' of RAP '{resource.RAPName}' to delete.");
            resource.toDelete = true;
        }

        private void DisableResource(rap_resource resource)
        {
            LoggerSingleton.Raps.Info($"Disabling resource '{resource.resourceName}' of RAP '{resource.RAPName}'.");
            resource.access = false;
            resource.synchronized = false;
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
                LoggerSingleton.General.Fatal("Failed query.");
                Console.WriteLine("Failed query.");
            }
            return results;
        }

        static public void UpdateDatabase(RapContext db)
        {
            LoggerSingleton.General.Info("Saving changes into database (marked raps/rap_resources to be deleted)");
            LoggerSingleton.Raps.Info("Saving changes into database (marked raps/rap_resources to be deleted)");

            db.SaveChanges();

            var rapsToDelete = db.raps.Where(r => r.toDelete == true).ToList();
            db.raps.RemoveRange(rapsToDelete);

            var rapResourcesToDelete = db.rap_resource.Where(rr => rr.toDelete == true).ToList();
            db.rap_resource.RemoveRange(rapResourcesToDelete);

            LoggerSingleton.General.Info("Deleting obsolete RAPs and RAP_Resources from MySQL database");
            LoggerSingleton.Raps.Info("Deleting obsolete RAPs and RAP_Resources from MySQL database");

            db.SaveChanges();
        }
        #endregion
    }
}
