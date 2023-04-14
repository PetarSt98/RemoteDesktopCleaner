using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http.Headers;
using System.Web.Http;
using Owin;
using RemoteDesktopCleaner.BackgroundServices;
//using RemoteDesktopAccessCleaner.DataAccessLayer.MsSql;
//using RemoteDesktopAccessCleaner.DataAccessLayer.MySql;
//using RemoteDesktopAccessCleaner.Modules.ActiveDirectory;
//using RemoteDesktopAccessCleaner.Modules.ConfigComparison;
//using RemoteDesktopAccessCleaner.Modules.ConfigProvider;
//using RemoteDesktopAccessCleaner.Modules.ConfigValidation;
//using RemoteDesktopAccessCleaner.Modules.Emails;
//using RemoteDesktopAccessCleaner.Modules.FileArchival;
//using RemoteDesktopAccessCleaner.Modules.Gateway;
//using RemoteDesktopAccessCleaner.Modules.Gateway.GroupManagement;
//using RemoteDesktopAccessCleaner.Modules.PolicyValidation;
using RemoteDesktopCleaner.ServiceCommunicatorNamespace;
//using RemoteDesktopAccessCleaner.Report;
//using Swashbuckle.Application;
using Unity; // deprecated
using Unity.Lifetime;
using Unity.WebApi;

namespace RemoteDesktopCleaner
{
    class Startup
    {
        public static UnityContainer Container;
        public void Configuration(IAppBuilder app)
        {
            HttpConfiguration config = new HttpConfiguration(); // creating config attribute
            //config.MapHttpAttributeRoutes();
            config.Routes.MapHttpRoute( // basic routing config
                name: "DefaultApi",
                routeTemplate: "api/{controller}/{action}",
                defaults: new { id = RouteParameter.Optional }
            );
            //#config.EnableSwagger(x => x.SingleApiVersion("v1", "RDAP_Cleaner")).EnableSwaggerUi(); // Setup UI
            Container = new UnityContainer(); // creating containter for regitring and resolving objects
            //Container.RegisterType<IAdDataRetriever, AdDataRetriever>(new HierarchicalLifetimeManager()); // HierarchicalLifetimeManager means it will create signleton
            //Container.RegisterType<IConfigComparer, ConfigComparer>(new HierarchicalLifetimeManager());
            //Container.RegisterType<IConfigReader, ConfigReader>(new HierarchicalLifetimeManager());
            Container.RegisterType<IConfigValidator, ConfigValidator>(new HierarchicalLifetimeManager());
            //Container.RegisterType<IEmailService, EmailService>(new HierarchicalLifetimeManager());
            //Container.RegisterType<IFileArchiver, FileArchiver>(new HierarchicalLifetimeManager());
            //Container.RegisterType<IGatewayProxy, GatewayProxy>(new HierarchicalLifetimeManager());
            //Container.RegisterType<IGatewayScanner, GatewayScanner>(new HierarchicalLifetimeManager());
            //Container.RegisterType<IGatewayAdProxy, GatewayAdProxy>(new HierarchicalLifetimeManager());
            //Container.RegisterType<IGroupManager, GroupManager>(new HierarchicalLifetimeManager());
            //Container.RegisterType<IGroupSynchronizer, GroupSynchronizer>(new HierarchicalLifetimeManager());
            //Container.RegisterType<IMySqlProxy, MySqlProxy>(new HierarchicalLifetimeManager());
            //Container.RegisterType<IMsSqlProxy, MsSqlProxy>(new HierarchicalLifetimeManager());
            //Container.RegisterType<IPolicyValidator, PolicyValidator>(new HierarchicalLifetimeManager());
            //Container.RegisterType<IRapSynchronizer, RapSynchronizer>(new ContainerControlledLifetimeManager());
            //Container.RegisterType<IReporter, Reporter>(new ContainerControlledLifetimeManager());
            Container.RegisterType<IServiceCommunicator, ServiceCommunicator>(new HierarchicalLifetimeManager());
            Container.RegisterType<ISynchronizer, Synchronizer>(new HierarchicalLifetimeManager());
            //Container.RegisterType<ITerminalServerGatewayConnector, TerminalServerGatewayConnector>(new HierarchicalLifetimeManager());
            Container.RegisterType<IUrlProvider, UrlProvider>(new HierarchicalLifetimeManager());
            config.DependencyResolver = new UnityDependencyResolver(Container); // creating the resolver
            config.Formatters.JsonFormatter.SupportedMediaTypes.Add(new MediaTypeHeaderValue("application/json")); // Response in JSON file instead of default XML
            app.UseCors(Microsoft.Owin.Cors.CorsOptions.AllowAll); // allows a web page or application to make requests to a different
                                                                   //
                                                                   //
                                                                   // than the one that served the web page
            app.UseWebApi(config);
            // containter will create needed object by resolving hierarchy of constructors
        }
    }
}
