namespace RemoteDesktopCleaner.BackgroundServices.Obsolete
{
    public interface IGatewayRapSynchronizer
    {
        List<string> GetGatewaysRapNamesAsync(string serverName);
        void SynchronizeRaps(string serverName, List<string> allGatewayGroups, List<string> toDeleteGatweayGroups, List<string> gatewayRaps);
    }
}
