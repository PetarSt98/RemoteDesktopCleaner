using System.Configuration;
using NLog;


namespace RemoteDesktopCleaner
{
    public static class AppConfig
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public static List<string> GetGatewaysInUse()
        {
            try
            {
                return ConfigurationManager.AppSettings["gateways"].Split(',').ToList();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed getting gateways machines in use from config file.");
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
