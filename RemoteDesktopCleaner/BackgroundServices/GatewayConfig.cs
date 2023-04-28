using System.Text.Json;


namespace RemoteDesktopCleaner.BackgroundServices
{
    public class GatewayConfig
    {
        public string ServerName { get; set; }
        public List<LocalGroup> LocalGroups { get; } = new List<LocalGroup>();

        public void Add(LocalGroup localGroup)
        {
            LocalGroups.Add(localGroup);
        }

        public void Add(List<LocalGroup> localGroups)
        {
            LocalGroups.AddRange(localGroups);
        }

        public GatewayConfig(string serverName)
        {
            ServerName = serverName;
        }
        public GatewayConfig() { }

        public GatewayConfig(string serverName, IEnumerable<LocalGroup> localGroups)
        {
            ServerName = serverName;
            LocalGroups.AddRange(localGroups);
        }

        public override string ToString()
        {
            return JsonSerializer.Serialize(this);
        }
    }
}
