using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RemoteDesktopCleaner.ServiceCommunicatorNamespace
{
    class UrlProvider : IUrlProvider
    {
        public string GetCoreBridgeUrl()
        {
            return ConfigurationManager.AppSettings["corebridge-url"];
        }
    }
}
