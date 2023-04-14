using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NLog;
//using re.Models;
//using RemoteDesktopAccessCleaner.Models.Requests;

namespace RemoteDesktopCleaner.ServiceCommunicatorNamespace
{
    class ServiceCommunicator : IServiceCommunicator
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly IUrlProvider _urlProvider;
        private static readonly HttpClient Client = new HttpClient();

        public ServiceCommunicator(IUrlProvider urlProvider)
        {
            _urlProvider = urlProvider;
            Client.Timeout = TimeSpan.FromHours(3);
        }

        //public async Task<List<string>> GetGatewayRapNames(string serverName)
        //{
        //    try
        //    {
        //        string endpoint = $"{_urlProvider.GetCoreBridgeUrl()}/api/raps/names/{serverName}";
        //        HttpResponseMessage response = await Client.GetAsync(endpoint); //  get all raps from the server
        //        if (!response.IsSuccessStatusCode)
        //            throw new Exception($"Response from {endpoint} not successful.");
        //        return JsonConvert.DeserializeObject<List<string>>(response.Content.ReadAsStringAsync().Result);
        //    }
        //    catch (Exception ex)
        //    {
        //        Console.WriteLine(ex);
        //        return new List<string>();
        //    }
        //}

        //public async Task<List<RapsDeletionResponse>> DeleteRapsFromServer(string serverName, List<string> rapsToDelete)
        //{
        //    List<RapsDeletionResponse> result = new List<RapsDeletionResponse>();
        //    try
        //    {
        //        Console.WriteLine($"Requesting deletion of {rapsToDelete.Count} RAPs on server '{serverName}'.");
        //        string endpoint = $"{_urlProvider.GetCoreBridgeUrl()}/api/raps/delete-raps"; // get localhost:5000/ contact the REST api which deletes the RAP
        //        var request = new RapsDeletionRequest(serverName, rapsToDelete); // create object for deleting raps with server name and rap name
        //        using (var content = new StringContent(System.Text.Json.JsonSerializer.Serialize(request), Encoding.UTF8, "application/json"))
        //        { // serialize the request/object - convert to json
        //            using (var response = await Client.PostAsync(endpoint, content))
        //            { // send request to delete raps with object
        //                response.EnsureSuccessStatusCode(); // check status
        //                Console.WriteLine("Got successfull response.");
        //                result.AddRange(JsonConvert.DeserializeObject<List<RapsDeletionResponse>>(response.Content.ReadAsStringAsync().Result)); // check which deletions were succesful
        //                Console.WriteLine($"Raps in response: '{result.Count}'.");
        //            }
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        Console.WriteLine($"Failed requesting deletion of RAPs on server '{serverName}'.");
        //        Console.WriteLine(ex);
        //    }
        //    return result;
        //}
    }
}
