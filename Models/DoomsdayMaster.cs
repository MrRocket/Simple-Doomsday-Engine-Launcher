using Avalonia.Media.Imaging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;


namespace Simple_Doomsday_Engine_Launcher.Models
{
    public class DoomsdayServerInfo
    {
        public string IP { get; set; }
        public int Port { get; set; }
        public Bitmap GameImage { get; set; }
        public string ServerName { get; set; } = "Doomsday Server";
        public string Engine { get; set; } = "Doomsday";
        public string Version { get; set; } = "--";
        public string Game { get; set; } = "--";
        public string Map { get; set; } = "--";
        public string Addons { get; set; } = "--";
        public int Players { get; set; } = 0;
        public int MaxPlayers { get; set; } = 0;
        public string Ping { get; set; } = "--";
    }

    public static class DoomsdayMaster
    {
        private const string MasterUrl = "http://api.dengine.net/1/master_server?op=list";

        public static async Task<List<DoomsdayServerInfo>> GetServersAsync()
        {
            var servers = new List<DoomsdayServerInfo>();
            try
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "DoomConnectorX/1.0");
                    client.Timeout = TimeSpan.FromSeconds(10);

                    string json = await client.GetStringAsync(MasterUrl);
                    if (string.IsNullOrWhiteSpace(json) || json == "null")
                        return servers;

                    var entries = JsonConvert.DeserializeObject<List<DoomsdayEntry>>(json);
                    if (entries == null) return servers;

                    Debug.WriteLine($"[Doomsday] Master returned {entries.Count} servers");

                    foreach (var e in entries)
                    {
                        if (string.IsNullOrWhiteSpace(e.host) || e.port <= 0) continue;

                        // 🔥 FIX: Build addons string from pkgs, KEEP .wad extension
                        string addons = "--";
                        if (e.pkgs != null && e.pkgs.Count > 0)
                        {
                            var pkgNames = e.pkgs
                                .Select(p =>
                                {
                                    // Remove version suffix (everything after underscore)
                                    int underscore = p.IndexOf('_');
                                    string name = underscore > 0 ? p.Substring(0, underscore) : p;

                                    // Extract just the filename part (after last dot in package path)
                                    int lastSep = name.LastIndexOf('.');
                                    string filename = lastSep >= 0 ? name.Substring(lastSep + 1) : name;

                                    // 🔥 ADD .wad extension if not already present
                                    if (!filename.EndsWith(".wad", StringComparison.OrdinalIgnoreCase))
                                    {
                                        filename += ".wad";
                                    }

                                    return filename;
                                })
                                .Where(n => !string.IsNullOrWhiteSpace(n) &&
                                            !n.Equals("doom.wad", StringComparison.OrdinalIgnoreCase) &&
                                            !n.Equals("doom2.wad", StringComparison.OrdinalIgnoreCase) &&
                                            !n.Equals("ultimate.wad", StringComparison.OrdinalIgnoreCase) &&
                                            !n.Equals("plutonia.wad", StringComparison.OrdinalIgnoreCase) &&
                                            !n.Equals("tnt.wad", StringComparison.OrdinalIgnoreCase) &&
                                            !n.Equals("freedm.wad", StringComparison.OrdinalIgnoreCase) &&
                                            !n.Equals("phase1.wad", StringComparison.OrdinalIgnoreCase) &&
                                            !n.Equals("phase2.wad", StringComparison.OrdinalIgnoreCase) &&
                                            !n.StartsWith("net.dengine", StringComparison.OrdinalIgnoreCase))
                                .ToList();

                            if (pkgNames.Count > 0)
                                addons = string.Join(", ", pkgNames);
                        }

                        // Determine version from plugin field e.g. "jdoom 2.3.2"
                        string version = "--";
                        if (!string.IsNullOrWhiteSpace(e.plugin))
                        {
                            var parts = e.plugin.Split(' ');
                            if (parts.Length > 1) version = parts[1];
                        }

                        var info = new DoomsdayServerInfo
                        {
                            IP = e.host,
                            Port = e.port,
                            ServerName = !string.IsNullOrWhiteSpace(e.name) ? e.name : $"{e.host}:{e.port}",
                            Engine = "Doomsday",
                            Version = !string.IsNullOrWhiteSpace(e.ver) ? e.ver : "--",
                            Game = !string.IsNullOrWhiteSpace(e.game) ? e.game : version,
                            Map = !string.IsNullOrWhiteSpace(e.map) ? e.map : "--",
                            Addons = addons,
                            Players = e.pnum,
                            MaxPlayers = e.pmax,
                            Ping = "--"
                        };

                        servers.Add(info);
                        Debug.WriteLine($"[Doomsday] {info.IP}:{info.Port} — {info.ServerName} | {info.Game} | {info.Map} | Addons={info.Addons} | Players={info.Players}/{info.MaxPlayers}");
                    }
                }

                // Ping each server in parallel using ICMP
                var semaphore = new SemaphoreSlim(20);
                var tasks = new List<Task>();

                foreach (var server in servers)
                {
                    var s = server;
                    tasks.Add(Task.Run(async () =>
                    {
                        await semaphore.WaitAsync();
                        try
                        {
                            s.Ping = await GetIcmpPingAsync(s.IP);
                        }
                        finally { semaphore.Release(); }
                    }));
                }

                await Task.WhenAll(tasks);
                Debug.WriteLine("[Doomsday] Ping queries complete.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[Doomsday] GetServersAsync error: " + ex.Message);
            }

            return servers;
        }

        private static async Task<string> GetIcmpPingAsync(string ip)
        {
            try
            {
                using (var ping = new Ping())
                {
                    var reply = await ping.SendPingAsync(ip, 2000);
                    if (reply.Status == IPStatus.Success)
                        return reply.RoundtripTime.ToString();
                }
            }
            catch { }
            return "--";
        }

        // JSON model matching the API response fields
        private class DoomsdayEntry
        {
            public string host { get; set; }
            public int port { get; set; }
            public string name { get; set; }
            public string desc { get; set; }
            public string ver { get; set; }
            public string plugin { get; set; }
            public List<string> pkgs { get; set; }
            public string game { get; set; }
            public string cfg { get; set; }
            public string map { get; set; }
            public int pnum { get; set; }
            public int pmax { get; set; }
        }
    }
}