using NLog;
using NLog.Config;
using NLog.Targets;


namespace RemoteDesktopCleaner.Loggers
{
    public class LoggerSingleton
    {
        // Private static instances of the loggers
        private static readonly Logger LoggerGeneral = LogManager.GetLogger("logfileGeneral");
        private static readonly Logger LoggerRaps = LogManager.GetLogger("logfileMarkedObsoleteRaps");
        private static readonly Logger LoggerSynchronizedRAPs = LogManager.GetLogger("logfileSynchronizedRAPs");
        private static readonly Logger LoggerSynchronizedLocalGroups = LogManager.GetLogger("logfileSynchronizedLocalGroups");

        // Static constructor to initialize the loggers
        static LoggerSingleton()
        {
            string configFilePath = $@"{Directory.GetParent(Environment.CurrentDirectory).Parent.Parent.FullName}\nlog.config";
            LogManager.Configuration = new XmlLoggingConfiguration(configFilePath);

            // Reload the NLog configuration
            LogManager.ReconfigExistingLoggers();

            // Get the path to the .log file for each target
            foreach (var target in LogManager.Configuration.AllTargets)
            {
                if (target is FileTarget fileTarget)
                {
                    string logFilePath = fileTarget.FileName.Render(new LogEventInfo());
                    Console.WriteLine($"Log file path for {fileTarget.Name}: {logFilePath}");
                }
            }

            string timestamp = DateTime.Now.ToString("yyyy_MM_dd_HH_mm_ss");
            GlobalDiagnosticsContext.Set("logTimestamp", timestamp);
        }

        // Private constructor to prevent instantiation
        private LoggerSingleton()
        {
        }

        // Public static properties to access the unique logger instances
        public static Logger General
        {
            get
            {
                return LoggerGeneral;
            }
        }

        public static Logger Raps
        {
            get
            {
                return LoggerRaps;
            }
        }
        public static Logger SynchronizedRaps
        {
            get
            {
                return LoggerSynchronizedRAPs;
            }
        }

        public static Logger SynchronizedLocalGroups
        {
            get
            {
                return LoggerSynchronizedLocalGroups;
            }
        }
    }
}