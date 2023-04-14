using System;
using System.Collections.Generic;
using System.Linq;
using System.Configuration;
using NLog;
using MySql.Data.MySqlClient;
using MySql.Data;
using System.Collections.Generic;
using System.DirectoryServices;
using System.DirectoryServices.AccountManagement;
using System.DirectoryServices.ActiveDirectory;
using RemoteDesktopCleaner.Data;
//using System.Data.Entity;
//using MySql.Data.Entity;
//using RemoteDesktopAccessCleaner.DataAccessLayer;
//using RemoteDesktopAccessCleaner.Models;
//using RemoteDesktopAccessCleaner.Models.EF;
//using RemoteDesktopAccessCleaner.Modules.ActiveDirectory;
//using RemoteDesktopAccessCleaner.Modules.ConfigProvider;
//using RemoteDesktopAccessCleaner.Modules.PolicyValidation;

namespace RemoteDesktopCleaner.BackgroundServices
{
    public class ConfigValidator : IConfigValidator
    {
        private const string DomainName = "CERN";
        private const string DomainName2 = "cern.ch";
        private const string GlobalAdminGroup = @"CERN\NICE Local Administrators Managers";
        private const string PrimaryAccountGroup = @"CERN\cern-accounts-primary";
        //private readonly IMySqlProxy _mySqlProxy;
        private readonly List<string> _allowedNetworkDomains = new List<string> { "GPN", "LCG", "ITS", "CLOUD-EXP" };
        private const string username = "pstojkov";
        private const string password = "GeForce9800GT.";

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        //    private readonly IConfigReader _configReader;
        //    private readonly IAdDataRetriever _adQueryWorker;
        //    private readonly IPolicyValidator _policyValidator;

        //    public ConfigValidator(IConfigReader configReader, IAdDataRetriever adQueryWorker, IPolicyValidator policyValidator)
        //    {
        //        _configReader = configReader;
        //        _adQueryWorker = adQueryWorker;
        //        _policyValidator = policyValidator;
        //    }
        public ConfigValidator()
        {

        }

        public bool MarkObsoleteData()
        {
            Logger.Info("Validating DB RAPs and corresponding Resources.");
            Console.WriteLine("Validating DB RAPs and corresponding Resources.");
            var raps = new List<rap>(); // getting raps which rows will be deleted
            try
            {
                using (var db = new RapContext())
                { // instancing the database object
                  //var rape = db.raps.FirstOrDefault();
                    raps.AddRange(GetRaps(db)); // getting raps
                    Logger.Info($"Queried {raps.Count} RAPs from DB. Starting validation.");
                    var i = 1;
                    var counter = 0;

                    //IsEGroupInActiveDirectory(domainContext, PrimaryAccountGroup);

                    foreach (var rapRow in raps)
                    {
                        Console.Write($"\r{i}/{raps.Count} - {100 * i / raps.Count}% ");
                        i++;

                        if (!ValidateRap(rapRow.login))
                        { // ovde se proveri da li je user i grupa u aktivnom direktorijumu
                            Logger.Debug($"Login '{rapRow.login}' not present in Active Directory.");
                            Console.WriteLine($"Login '{rapRow.login}' not present in Active Directory.");
                            MarkToDelete(rapRow);
                            counter++;
                            continue;
                        }

                        //CheckResourcesValidity(rapRow); // brisem resource ako rap ne valja?
                        //if (HasNoValidResources(rapRow))
                        //{
                        //    Logger.Debug($"Login '{rapRow.login}' has no valid resources.");
                        //    Console.WriteLine($"Login '{rapRow.login}' has no valid resources.");
                        //    MarkToDelete(rapRow);
                        //    counter++;
                        //}
                        if (counter > 3) break;
                    }

                    db.SaveChanges();
                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.Fatal(ex, "Error while validating model configuration.");
                return false;
            }
        }
        //private void IsEGroupInActiveDirectory(PrincipalContext domainContext, string groupName)
        //{
        //    var primaryAccountsGroupPrincipal = new GroupPrincipal(domainContext, groupName);
        //    if (primaryAccountsGroupPrincipal == null)
        //    {
        //        var errorMessage = $"(10) E-Group '{groupName}' not found in Active Directory.";
        //        throw (new ValidatorException(errorMessage));
        //    }
        //}
        //private void ValidateRapResources(rap rapRow)
        //{
        //    foreach (var resource in rapRow.rap_resource)
        //    {
        //        var policyValidationResult = ValidatePolicy(rapRow.login, resource.resourceOwner, resource.resourceName, resource); // resource
        //        ConsumeValidationResult(rapRow, resource, policyValidationResult);
        //    }
        //}
        //private void ConsumeValidationResult(rap rapRow, rap_resource resource, PolicyValidationResult result)
        //{
        //    if (result.FailureDetail == FailureDetail.ValidationException)
        //    {
        //        Logger.Warn($"RAP-Resource '{rapRow.name}'-'{resource.resourceName}' skipped: {result.Message}");
        //        return;
        //    }
        //    if (result.FailureDetail == FailureDetail.ComputerNotFound)
        //        MarkToDelete(resource);
        //    else if (result.FailureDetail == FailureDetail.LoginNotFound)
        //    {
        //        DisableResource(resource);
        //        SetRapEnabledFalseIfNoAccessibleResources(rapRow);
        //    }

        //    if (resource.invalid != result.Invalid)
        //        resource.invalid = result.Invalid;
        //}

        //private PolicyValidationResult ValidatePolicy(string login, string rapOwner, string computerName, rap_resource resource)
        //{
        //    UserPrincipal rapOwnerPrincipal = null;
        //    //ch.cern.network.SOAPNetworkService soap = null;
        //    //var validationResult = new PolicyValidationResult();
        //    //var messages = new StringBuilder();
        //    try
        //    {

        //        rapOwner = RemoveDomainFromRapOwner(rapOwner);
        //        Logger.Info($"Validating user '{login}', RAP owner '{rapOwner}', computer '{computerName}'.");

        //        using (var domainContext = new PrincipalContext(ContextType.Domain, DomainName))
        //        {
        //             // ! I OVO
        //            ComputerExistsInActiveDirectory(computerName);
        //            UserOrGroupExistInAd(domainContext, login); // OVO JE BUKV SUVISNO
        //            rapOwnerPrincipal = GetRapOwnerFromActiveDirectory(domainContext, rapOwner);

        //            if (IsPolicyAnException(rapOwner, login, computerName, resource)) // rewrite this, dodao sam ja resource
        //                return new PolicyValidationResult(false);

        //            soap = new ch.cern.network.SOAPNetworkService(); // ??????????????????
        //            string auth = soap.getAuthToken(username, password, "NICE"); // replace this
        //            soap.AuthValue = new ch.cern.network.Auth();
        //            soap.AuthValue.token = auth;
        //            var deviceInfo = soap.getDeviceInfo(computerName); // auth failure
        //            bool bNetworkOk = CheckDeviceDomainInterfaces(deviceInfo, soap);
        //            bool isNiceMember = IsUserNiceGroupMember(domainContext, rapOwnerPrincipal);
        //            if (!isNiceMember)
        //            {
        //                var msg = $"User Account '{login}' not found in the nice local administrator managers group.";
        //                messages.Append(msg);
        //                Logger.Warn(msg);
        //            }
        //            else if (bNetworkOk)
        //                return new PolicyValidationResult(false);

        //            bool isUserAllowed = IsRapOwnerResponsible(domainContext, rapOwnerPrincipal, deviceInfo); // ovde sam stao
        //            switch (isUserAllowed)
        //            {
        //                case false:
        //                    var msg =
        //                        $"RAP owner '{rapOwner}' is not the responsible/user for the computer '{computerName}'.";
        //                    Logger.Warn(msg);
        //                    messages.Append(msg);
        //                    break;
        //                case true when bNetworkOk:
        //                    return new PolicyValidationResult(false);
        //                default:
        //                    Logger.Warn($"Account '{rapOwner}' is not allowed to manage computer '{computerName}'.");
        //                    throw new InvalidPolicyException();
        //            }
        //        }

        //    }
        //    catch (ValidatorException validatorEx)
        //    {
        //        Logger.Error($"Error while validating policy: {validatorEx.Message}.");
        //        validationResult.FailureDetail = FailureDetail.ValidationException;
        //    }
        //    catch (ComputerNotFoundInActiveDirectoryException ex)
        //    {
        //        validationResult.FailureDetail = FailureDetail.ComputerNotFound;
        //    }
        //    catch (LoginNotFoundInActiveDirectoryException ex)
        //    {
        //        validationResult.FailureDetail = FailureDetail.LoginNotFound;

        //    }
        //    catch (InvalidPolicyException ex)
        //    {
        //    }
        //    catch (Exception ex)
        //    {
        //        Logger.Warn("(00) Unexpected exception occurred, unable to validate.");
        //        //in this case no changes must be done on the policy
        //        validationResult.FailureDetail = FailureDetail.ValidationException;
        //        Logger.Error(ex);
        //    }
        //    finally
        //    {
        //        rapOwnerPrincipal?.Dispose();
        //        soap?.Dispose();
        //    }

        //    return validationResult;
        //}

        private string RemoveDomainFromRapOwner(string rapOwner)
        {
            //var pattern = $@"^{DomainName}\\([\w\W]+)";
            //Match match = Regex.Match(rapOwner, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            //return match.Success ? match.Groups[1].Value : rapOwner;

            if (rapOwner.StartsWith(DomainName))
            {
                return rapOwner.Substring(DomainName.Length + 1);
            }
            else
            {
                return rapOwner;
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
                //using (var context = new PrincipalContext(ContextType.Domain))
                //{
                //    result = UserPrincipal.FindByIdentity(context, login);
                //}
                return result != null;
            }
            catch (Exception ex)
            {
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
            catch (Exception ex)
            {
                return false;
            }
        }

        private void MarkToDelete(rap rapRow)
        {
            Logger.Info($"Marking rap '{rapRow.login}' to delete.");
            rapRow.toDelete = true;
        }

    }
}
