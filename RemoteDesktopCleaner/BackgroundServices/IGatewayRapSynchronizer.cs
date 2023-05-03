﻿namespace RemoteDesktopCleaner.BackgroundServices
{
    public interface IGatewayRapSynchronizer
    {
        List<string> GetGatewaysRapNamesAsync(string serverName);
        void SynchronizeRaps(string serverName, List<string> allGatewayGroups, List<string> gatewayRaps);
    }
}