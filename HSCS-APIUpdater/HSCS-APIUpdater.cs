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
    private readonly ServerInfo _serverInfo;
    private readonly string _registerUrl;
    private readonly string _updateUrl;
    private readonly string _pingUrl;
    private readonly string _masterUrl;
    private readonly ACServerConfiguration _serverConfiguration;
    private readonly HSCS_APIUpdaterConfiguration _pluginConfiguration;
    private readonly EntryCarManager _entryCarManager;
    private bool _registered = false;

    public HSCS_APIUpdater(ACServerConfiguration serverConfiguration, HSCS_APIUpdaterConfiguration configuration,
        EntryCarManager entryCarManager) {
        _serverConfiguration = serverConfiguration;
        _pluginConfiguration = configuration;
        _entryCarManager = entryCarManager;
        _serverInfo = new ServerInfo
        {
            server_name = _serverConfiguration.Server.Name,
            http_port = _serverConfiguration.Server.HttpPort,
            track_name = _serverConfiguration.Server.Track,
            clients = new Dictionary<string, ServerClient>()
        };

        if (string.IsNullOrEmpty(configuration.ServerAddress)) {
            Log.Fatal("[HSCS-APIUpdater] ServerAddress is not set in the configuration file");
            Environment.Exit(1);
        }

        if (string.IsNullOrEmpty(_pluginConfiguration.APIKey)) {
            Log.Fatal("[HSCS-APIUpdater] APIKey is not set in the configuration file");
            Environment.Exit(1);
        }

        if (configuration.ServerPort == 0) {
            Log.Fatal("[HSCS-APIUpdater] ServerPort is not set");
            Environment.Exit(1);
        }

        _masterUrl = $"http://{configuration.ServerAddress}:{configuration.ServerPort}/api/v1";

        _registerUrl = $"http://{configuration.ServerAddress}:{configuration.ServerPort}/api/v1/register";
        _updateUrl = $"http://{configuration.ServerAddress}:{configuration.ServerPort}/api/v1/update";
        _pingUrl = $"http://{configuration.ServerAddress}:{configuration.ServerPort}/api/v1/ping";

        _entryCarManager.ClientConnected += (ACTcpClient client, EventArgs args) => {
            if (!_registered) return;
            client.FirstUpdateSent += OnClientLoaded;
        };

        _entryCarManager.ClientDisconnected += OnClientDisconnected;

        RegisterServer();
    }

    private async Task PingServer() {
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

        if (!_registered) RegisterServer();

        _registered = true;

        pingClient.Dispose();
    }

    private void RegisterServer() {
        if (_registered) return;

        // Register server
        Log.Information("[HSCS-APIUpdater] Registering server with HSCS API");

        HttpClient client = new HttpClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_pluginConfiguration.APIKey}");

        foreach (var car in _entryCarManager.EntryCars) {
            if (car.Client == null || car.Client.Guid == null) continue;
            if (car.Client.Name!.Contains("Traffic")) continue;
            if (_serverInfo.clients!.ContainsKey(car.Client.Guid)) continue;

            _serverInfo.clients!.Add(car.Client.Guid,
                new ServerClient {name = car.Client.Name, steam_id = car.Client.Guid, car = car.Model}
            );

            Log.Information($"[HSCS-APIUpdater] Adding client {car.Client.Name} : {car.Client.Guid}");
        }

        JObject json = JObject.FromObject(_serverInfo);

        var content = new StringContent(json.ToString(), Encoding.UTF8, "application/json");
        var req = client.PostAsync(_registerUrl + "/" + _serverConfiguration.Server.UdpPort, content).Result;

        if (!req.IsSuccessStatusCode) {
            string error = req.Content.ReadAsStringAsync().Result;
            Log.Error("[HSCS-APIUpdater] Failed to register server: {0}", error);
            client.Dispose();
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
        if (client.Name!.Contains("Traffic") || client.Guid == null) return;

        if (_serverInfo.clients!.ContainsKey(client.Guid)) {
            _serverInfo.clients.Remove(client.Guid);
        }

        _serverInfo.clients.Add(client.Guid,
            new ServerClient {name = client.Name, steam_id = client.Guid, car = client.EntryCar.Model}
        );
        
        Log.Information($"[HSCS-APIUpdater] Adding client {client.Name} : {client.Guid}");

        HttpClient httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_pluginConfiguration.APIKey}");

        var json = JObject.FromObject(_serverInfo.clients[client.Guid]);
        var content = new StringContent(json.ToString(), Encoding.UTF8, "application/json");
        var req = httpClient.PatchAsync($"{_masterUrl}/add_client/{_serverConfiguration.Server.UdpPort}", content)
            .Result;

        if (!req.IsSuccessStatusCode) {
            string error = req.Content.ReadAsStringAsync().Result;
            Log.Error("[HSCS-APIUpdater] Failed to send server info to HSCS API: {0}", error);
        }

        httpClient.Dispose();
    }

    private void OnClientDisconnected(ACTcpClient client, EventArgs args) {
        if (!_registered || client.Guid == null || _serverInfo.clients == null) return;
        if (!_serverInfo.clients.ContainsKey(client.Guid)) {
            Log.Warning("[HSCS-APIUpdater] Attempted to remove non-existent client {0}", client.Guid);
            return;
        }
        
        Log.Information($"[HSCS-APIUpdater] Removing client {client.Name} : {client.Guid}");

        HttpClient httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_pluginConfiguration.APIKey}");

        JObject json = JObject.FromObject(_serverInfo.clients[client.Guid]);
        var content = new StringContent(json.ToString(), Encoding.UTF8, "application/json");
        var req = httpClient.PatchAsync($"{_masterUrl}/remove_client/{_serverConfiguration.Server.UdpPort}", content)
            .Result;

        if (!req.IsSuccessStatusCode) {
            string error = req.Content.ReadAsStringAsync().Result;
            Log.Error("[HSCS-APIUpdater] Failed to send server info to HSCS API: {0}", error);
        }
        
        _serverInfo.clients.Remove(client.Guid);

        httpClient.Dispose();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        while (!stoppingToken.IsCancellationRequested) {
            try {
                await PingServer();
            } catch (Exception e) {
                Log.Error("[HSCS-APIUpdater] Exception: {0}", e.Message);
            } finally {
                await Task.Delay(TimeSpan.FromSeconds(4), stoppingToken);
            }
        }
    }
}