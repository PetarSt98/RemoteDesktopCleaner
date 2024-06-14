using RemoteDesktopCleaner.Exceptions;
using SynchronizerLibrary.CommonServices.LocalGroups;
using SynchronizerLibrary.Data;
using SynchronizerLibrary.Loggers;
using SynchronizerLibrary.SOAPservices;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace RemoteDesktopCleaner.BackgroundServices.Services
{
    public class MissingLocalGroups
    {
        public string Name { get; set; }
        public List<string> Members { get; set; }
        public List<string> Computers { get; set; }
        public bool ContainsData { get; set; }
        public string Gateway { get; set; }

        public MissingLocalGroups()
        {
            this.Name = "";
            this.Members = new List<string>();
            this.Computers = new List<string>();
            this.ContainsData = false;
            this.Gateway = "";
        }
    }

    public class LGSParser
    {


        private Dictionary<string, List<MissingLocalGroups>> missingLGs;
        private Dictionary<string, Dictionary<string, LocalGroup>> allLGs;
        private List<string> gatewaysToSynchronize;
        private const string CacheFileMissingLocalGroupPrefix = "missingLocalGroupsCache";
        private const string CacheFileExtension = ".json";
        private const string CacheFilePath = "Cache";

        public LGSParser(List<string> gatewaysToSynchronize)
        {
            this.missingLGs = new Dictionary<string, List<MissingLocalGroups>>();
            this.allLGs = new Dictionary<string, Dictionary<string, LocalGroup>>();
            this.gatewaysToSynchronize = gatewaysToSynchronize;

            this.LoadLGs(gatewaysToSynchronize);
        }

        private void AddGatewayServer(string serverName)
        {
            this.missingLGs[serverName] = new List<MissingLocalGroups>();
        }

        private void AddMissingLGConfig(string serverName, MissingLocalGroups missingLocalGroups)
        {
            this.missingLGs[serverName].Add(missingLocalGroups);
        }

        private void LoadLGs(List<string> gatewaysToSynchronize)
        {
            foreach (var gatewayName in gatewaysToSynchronize)
            {
                this.AddGatewayServer(gatewayName);

                var lgGroups = Synchronizer.GetGatewayLocalGroupsFromFile(gatewayName);
                allLGs[gatewayName] = new Dictionary<string, LocalGroup>();
                foreach (var lgGroup in lgGroups)
                {
                    allLGs[gatewayName][lgGroup.Name] = lgGroup;
                }

            }
        }

        private MissingLocalGroups CompareMembers(MissingLocalGroups missingLG, LocalGroup lGContent, string lGName, string serverName)
        {
            foreach (var computer in lGContent.MembersObj.Names)
            {
                if (!allLGs[serverName][lGName].MembersObj.Names.Contains(computer))
                {
                    missingLG.Members.Add(computer);
                    missingLG.ContainsData = true;
                    missingLG.Gateway = serverName;
                }
            }

            return missingLG;
        }

        private MissingLocalGroups CompareComputers(MissingLocalGroups missingLG, LocalGroup lGContent, string lGName, string serverName)
        {
            foreach (var computer in lGContent.ComputersObj.Names)
            {
                if (!allLGs[serverName][lGName].ComputersObj.Names.Contains(computer))
                {
                    missingLG.Computers.Add(computer);
                    missingLG.ContainsData = true;
                    missingLG.Gateway = serverName;
                }
            }

            return missingLG;
        }

        private MissingLocalGroups AddMissingLG(MissingLocalGroups missingLG, LocalGroup lGContent, string serverName)
        {
            missingLG.Members = lGContent.MembersObj.Names;
            missingLG.Computers = lGContent.ComputersObj.Names;
            missingLG.ContainsData = true;
            missingLG.Gateway = serverName;

            return missingLG;
        }

        private void CheckMissingConfig(KeyValuePair<string, LocalGroup> lgGroupPair)
        {
            foreach (var gatewayNameCheck in this.gatewaysToSynchronize)
            {
                MissingLocalGroups missingLG = new MissingLocalGroups();
                missingLG.Name = lgGroupPair.Key;
                if (allLGs[gatewayNameCheck].ContainsKey(lgGroupPair.Key))
                {
                    missingLG = this.CompareMembers(missingLG, lgGroupPair.Value, lgGroupPair.Key, gatewayNameCheck);
                    missingLG = this.CompareComputers(missingLG, lgGroupPair.Value, lgGroupPair.Key, gatewayNameCheck);
                }
                else
                {
                    missingLG = this.AddMissingLG(missingLG, lgGroupPair.Value, gatewayNameCheck);
                }

                if (missingLG.ContainsData)
                {
                    this.AddMissingLGConfig(missingLG.Gateway, missingLG);
                }
            }
        }

        public void ParseMissingLGs()
        {
            foreach (var gatewayName in this.gatewaysToSynchronize)
            {
                foreach (KeyValuePair<string, LocalGroup> lgGroupPair in allLGs[gatewayName])
                {
                    this.CheckMissingConfig(lgGroupPair);
                }
            }
        }

        public void StoreParsedMissigLGs()
        {
            foreach (var key in this.missingLGs.Keys)
            {
                string dateTime = DateTime.Now.ToString("yyyyMMddHHmmss");
                string newCacheFilePath = $".\\{CacheFilePath}\\{CacheFileMissingLocalGroupPrefix}{key}{dateTime}{CacheFileExtension}";


                List<MissingLocalGroups> missingLocalGroupsTemp = new List<MissingLocalGroups>(this.missingLGs[key]);
                File.WriteAllText(newCacheFilePath, JsonSerializer.Serialize(missingLocalGroupsTemp));
            }
        }

        public void CleanData()
        {
            allLGs.Clear();
            missingLGs.Clear();
        }

        static public List<MissingLocalGroups> LoadMissingLocalGroupsCacheFromFile(string serverName)
        {
            var directoryInfo = new DirectoryInfo(Directory.GetCurrentDirectory());
            var newestFile = directoryInfo.GetFiles($".\\{CacheFilePath}\\{CacheFileMissingLocalGroupPrefix}{serverName}*{CacheFileExtension}")
                                            .OrderByDescending(f => f.LastWriteTime)
                                            .FirstOrDefault();

            if (newestFile != null)
            {
                try
                {
                    var content = File.ReadAllText(newestFile.FullName);
                    var serializedContent = JsonSerializer.Deserialize<List<MissingLocalGroups>>(content);
                    return serializedContent;
                }
                catch (JsonException jsonEx)
                {
                    Console.WriteLine("JSON Exception: " + jsonEx.Message);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("General Exception: " + ex.Message);
                }
            }

            return null;
        }

        public void AddMissingLGsToDB()
        {
            foreach (var gatewayName in this.gatewaysToSynchronize)
            {
                foreach (KeyValuePair<string, LocalGroup> lgGroupPair in allLGs[gatewayName])
                {
                    this.CheckUnsyncConfig(lgGroupPair);
                }
            }
        }

        private void CompareDBMembers(LocalGroup lGContent)
        {
            using (var db = new RapContext())
            {
                bool changeflag = false;

                foreach (var member in lGContent.MembersObj.Names)
                {
                    try
                    {


                        Dictionary<string, string> deviceInfo = new Dictionary<string, string>();

                        if (!db.raps.Any(r => r.name == ("RAP_" + member.Replace("LG-", ""))))
                        {
                            changeflag = true;

                            try
                            {
                                if (!ConfigValidator.ValidateRap(member.Replace("LG-", "")))
                                {
                                    continue;
                                }
                            }
                            catch (Exception ex)
                            {
                                continue;
                            }


                            var newRap = new rap
                            {
                                name = "RAP_" + member.Replace("LG-", "").ToLower(),
                                login = member.Replace("LG-", "").ToLower(),
                                resourceGroupName = "LG-" + member.Replace("LG-", "").ToLower(),
                                description = "",
                                port = "3389",
                                enabled = true,
                                resourceGroupDescription = "",
                                synchronized = true,
                                lastModified = DateTime.Now,
                                toDelete = false,
                                unsynchronizedGateways = "",

                            };

                            db.raps.Add(newRap);
                            LoggerSingleton.General.Info($"Added new rap: '{member.ToLower()}'.");
                        } 
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.ToString());
                    }
                }

                if (changeflag)
                {
                    db.SaveChanges();
                }
            }
        }

        private void CompareDBComputers(LocalGroup lGContent)
        {
            using (var db = new RapContext())
            {
                bool changeflag = false;

                foreach (var computer in lGContent.ComputersObj.Names)
                {
                    try
                    {
                        Dictionary<string, string> deviceInfo = new Dictionary<string, string>();

                        if (!db.rap_resource.Any(r => r.resourceName == computer.Replace("$", "") && r.RAPName == ("RAP_" + lGContent.Name.Replace("LG-", ""))))
                        {
                            changeflag = true;

                            try
                            {
                                ConfigValidator.ComputerExistsInActiveDirectory(computer.Replace("$", ""));

                                if (!ConfigValidator.ValidateRap(lGContent.Name.Replace("LG-", "")))
                                {
                                    continue;
                                }
                            }
                            catch (ComputerNotFoundInActiveDirectoryException ex)
                            {
                                continue;
                            }
                            catch (Exception ex)
                            {
                                continue;
                            }

                            bool alias = false;
                            try
                            {
                                deviceInfo = Task.Run(() => SOAPMethods.ExecutePowerShellSOAPScript(computer.Replace("$", ""), lGContent.Name.Replace("LG-", ""))).Result;
                                if (deviceInfo == null)
                                {
                                    throw new Exception("Failed SOAP");
                                }
                            }
                            catch (Exception ex)
                            {
                                alias = true;
                            }

                            string responsibleUsername;

                            if (alias)
                            {
                                responsibleUsername = "CERN\\" + lGContent.Name.Replace("LG-", "");
                            }
                            else
                            {
                                responsibleUsername = "CERN\\" + (deviceInfo["ResponsiblePersonUsername"].Length > 0 ? deviceInfo["ResponsiblePersonUsername"] : deviceInfo["ResponsiblePersonName"]);

                                if (responsibleUsername.Contains(" "))
                                {
                                    responsibleUsername = "CERN\\" + lGContent.Name.Replace("LG-", "");
                                }
                            }

                            var newRapResource = new rap_resource
                            {
                                RAPName = "RAP_" + lGContent.Name.Replace("LG-", "").ToLower(),
                                resourceName = computer.Replace("$", "").ToUpper(),
                                resourceOwner = "CERN\\" + (alias ? lGContent.Name.Replace("LG-", "") : deviceInfo["ResponsiblePersonUsername"]).ToLower(),
                                access = true,
                                synchronized = true,
                                invalid = false,
                                exception = false,
                                createDate = DateTime.Now,
                                updateDate = DateTime.Now,
                                toDelete = false,
                                unsynchronizedGateways = "",
                                alias = alias

                            };

                            db.rap_resource.Add(newRapResource);
                            LoggerSingleton.General.Info($"Added new rap_resource: '{"RAP_" + lGContent.Name.Replace("LG-", "").ToLower()}' : '{computer.Replace("$", "").ToUpper()}'.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.Message);
                    }
                }
                    

                if (changeflag)
                {
                    db.SaveChanges();
                }
            }

        }

        private void AddMissingLGToDB(LocalGroup lGContent)
        {
            this.CompareDBMembers(lGContent);
            this.CompareDBComputers(lGContent);
        }

        private void CheckUnsyncConfig(KeyValuePair<string, LocalGroup> lgGroupPair)
        {
            foreach (var gatewayNameCheck in this.gatewaysToSynchronize)
            {
                if (allLGs[gatewayNameCheck].ContainsKey(lgGroupPair.Key))
                {
                    this.CompareDBMembers(lgGroupPair.Value);
                    this.CompareDBComputers(lgGroupPair.Value);
                }
                else
                {
                    this.AddMissingLGToDB(lgGroupPair.Value);
                }

            }
        }
    }
}
