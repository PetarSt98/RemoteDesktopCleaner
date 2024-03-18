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

        public static string GetRemovalGateway()
        {
            try
            {
                return ConfigurationManager.AppSettings["init-cleanup-gateway"];
            }
            catch (Exception ex)
            {
                LoggerSingleton.General.Error(ex, $"Failed getting removal gateways machines in use from config file.");
                return "";
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
    }
}
