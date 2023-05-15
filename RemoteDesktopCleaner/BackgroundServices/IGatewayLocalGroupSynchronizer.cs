namespace RemoteDesktopCleaner.BackgroundServices.Obsolete
{
    public interface IGatewayLocalGroupSynchronizer
    {
        bool DownloadGatewayConfig(string serverName);
        List<string>  SyncLocalGroups(LocalGroupsChanges changedLocalGroups, string serverName);
    }
}
