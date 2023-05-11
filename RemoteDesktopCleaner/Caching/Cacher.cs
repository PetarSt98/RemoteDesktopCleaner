using System.Text.Json;

namespace RemoteDesktopCleaner.Caching
{
    public class Cacher
    {
        private const string CacheFilePath = "rapNamesCache.json";

        static public void SaveCacheToFile(List<string> _rapNamesCache)
        {
            File.WriteAllText(CacheFilePath, JsonSerializer.Serialize(_rapNamesCache));
        }

        static public List<string> LoadCacheFromFile()
        {
            if (File.Exists(CacheFilePath))
            {
                List<string> _rapNamesCache = new List<string>();
                var content = File.ReadAllText(CacheFilePath);
                _rapNamesCache.AddRange(Newtonsoft.Json.JsonConvert.DeserializeObject<List<string>>(content));
                return _rapNamesCache;
            }
            else
                return null;
        }

    }
}
