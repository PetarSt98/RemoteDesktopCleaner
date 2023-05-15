using System.Configuration;
using NLog;
using SynchronizerLibrary.Loggers;


namespace RemoteDesktopCleaner
{
    public static class AppConfig
    {
        public static List<string> GetGatewaysInUse()
        {
            try
            {
                return ConfigurationManager.AppSettings["gateways"].Split(',').ToList();
            }
            catch (Exception ex)
            {
                LoggerSingleton.General.Error(ex, $"Failed getting gateways machines in use from config file.");
                return new List<string>();
            }
        }

        public static string GetSyncLogSuffix()
        {
            return "-sync.log";
        }

        public static string GetInfoDir()
        {
            //return @"C:\Users\olindena\cernbox\WINDOWS\Desktop\TSGatewayWebServ\info";
            return ConfigurationManager.AppSettings["info-directory"];
        }

        public static string GetSyncLogDir()
        {
            return $@"{GetInfoDir()}\SyncLogs";
        }

        public static string GetAdminsEmail()
        {
            return ConfigurationManager.AppSettings["admins-email"];
        }
    }
}
