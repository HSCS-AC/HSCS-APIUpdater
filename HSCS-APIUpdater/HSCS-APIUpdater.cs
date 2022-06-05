using System.Net;
using System.Text;
using AssettoServer.Network.Tcp;
using AssettoServer.Server;
using AssettoServer.Server.Configuration;
using AssettoServer.Server.Plugin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json.Linq;
using Serilog;

namespace HSCS_APIUpdater;

class ServerClient {
    public string? name;
    public string? steam_id;
    public string? car;
}

class ServerInfo {
    public string? server_name;
    public int? http_port;
    public string? track_name;
    public Dictionary<string, ServerClient>? clients { get; set; }
}

public class HSCS_APIUpdater : BackgroundService, IAssettoServerAutostart {
    private readonly ServerInfo _serverInfo = new ServerInfo();
    private readonly string _registerUrl = "http://86.160.100.210:8083/api/v1/register";
    private readonly string _updateUrl = "http://86.160.100.210:8083/api/v1/update";
    private readonly string _pingUrl = "http://86.160.100.210:8083/api/v1/ping";
    private readonly ACServerConfiguration _serverConfiguration;
    private readonly EntryCarManager _entryCarManager;
    private bool _registered = false;

    public HSCS_APIUpdater(ACServerConfiguration serverConfiguration, EntryCarManager entryCarManager) {
        _serverConfiguration = serverConfiguration;
        _entryCarManager = entryCarManager;
        _serverInfo = new ServerInfo {
            server_name = _serverConfiguration.Server.Name,
            http_port = _serverConfiguration.Server.HttpPort,
            track_name = _serverConfiguration.Server.Track,
            clients = new Dictionary<string, ServerClient>()
        };

        _entryCarManager.ClientConnected += (ACTcpClient client, EventArgs args) => {
            if (!_registered) return;
            client.FirstUpdateSent += OnClientLoaded;
        };

        _entryCarManager.ClientDisconnected += OnClientDisconnected;
    }

    private async Task RegisterServer() {
        // Ping server to make sure it's up
        CancellationToken cancellationToken = new CancellationToken();
        HttpClient pingClient = new HttpClient();
        Task<HttpResponseMessage> pingTask = pingClient.GetAsync(_pingUrl, cancellationToken);
        try {
            await pingTask.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken);
        } catch (Exception) {
            Log.Error("[HSCS-APIUpdater] Failed to ping HSCS API: timeout");
            _registered = false;
            pingClient.Dispose();
            return;
        }

        pingClient.Dispose();

        if (_registered) return;
        
        Log.Information("[HSCS-APIUpdater] Registering server with HSCS API");

        HttpClient client = new HttpClient();

        foreach (var car in _entryCarManager.EntryCars) {
            if (car.Client == null) continue;
            
            _serverInfo.clients!.Add(car.Client!.Name!,
                new ServerClient {name = car.Client.Name, steam_id = car.Client.Guid, car = car.Model}
            );
            
            Log.Information($"[HSCS-APIUpdater] Adding client {car.Client.Name}");
        }
        
        JObject json = JObject.FromObject(_serverInfo);
        
        var content = new StringContent(json.ToString(), Encoding.UTF8, "application/json");
        var req = client.PostAsync(_registerUrl + "/" + _serverConfiguration.Server.UdpPort, content).Result;
        if (!req.IsSuccessStatusCode) {
            Log.Error("[HSCS-APIUpdater] Failed to register server: {0}", req.StatusCode);
            return;
        }
        
        _registered = true;
        Log.Information("[HSCS-APIUpdater] Successfully registered server");
        
        client.Dispose();
    }

    public JsonResult GetServerInfo() {
        return new JsonResult(_serverInfo);
    }

    private void OnClientLoaded(ACTcpClient client, EventArgs args) {
        _serverInfo.clients.Add(client.Name,
            new ServerClient {name = client.Name, steam_id = client.Guid, car = client.EntryCar.Model}
        );

        HttpClient httpClient = new HttpClient();
        var json = JObject.FromObject(_serverInfo);
        var content = new StringContent(json.ToString(), Encoding.UTF8, "application/json");
        var response = httpClient.PatchAsync(_updateUrl + "/" + _serverConfiguration.Server.UdpPort, content).Result;
        
        if (response.StatusCode != HttpStatusCode.OK) {
            Log.Error("[HSCS-APIUpdater] Failed to send server info to HSCS API: {0}", response.StatusCode);
        }
        
        httpClient.Dispose();
    }

    private void OnClientDisconnected(ACTcpClient client, EventArgs args) {
        if (!_registered || !_serverInfo.clients.ContainsKey(client.Name)) return;

        _serverInfo.clients.Remove(client.Name);
        
        HttpClient httpClient = new HttpClient();
        var json = JObject.FromObject(_serverInfo);
        var content = new StringContent(json.ToString(), Encoding.UTF8, "application/json");
        var response = httpClient.PatchAsync(_updateUrl + "/" + _serverConfiguration.Server.UdpPort, content).Result;
        if (response.StatusCode != HttpStatusCode.OK) {
            Log.Error("[HSCS-APIUpdater] Failed to send server info to HSCS API: {0}", response.StatusCode);
        }
        
        httpClient.Dispose();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        while (!stoppingToken.IsCancellationRequested) {
            try {
                await RegisterServer();
            } catch (Exception e) {
                Log.Error("[HSCS-APIUpdater] Exception: {0}", e.Message);
            } finally {
                await Task.Delay(TimeSpan.FromSeconds(4), stoppingToken);
            }
        }
    }
}