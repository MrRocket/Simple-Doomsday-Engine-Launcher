using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Simple_Doomsday_Engine_Launcher.Models;

public class WadDownloader
{
    private static readonly HttpClient _httpClient = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate })
    {
        Timeout = TimeSpan.FromMinutes(10)
    };

    static WadDownloader()
    {
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
    }

    private static readonly string[] Mirrors = new[]
    {
        "https://static.allfearthesentinel.com/wads/",
        "https://doomshack.org",
        "http://grandpachuck.org",
        "https://doomleague.org",
        "https://doomshack.org",
        "https://dogsoft.net",
        "https://doomshack.org",
        "https://firestick.games",
        "https://euroboros.net",
        "https://audrealms.org",
        "https://captainpollutiontv.de",
        "https://doomshack.org",
        "https://doomshack.org",
        "https://dogsoft.net",
        "https://dogsoft.net",
        "https://worldsbe.st",
        "https://worldsbe.stwads/",
        "https://worldsbe.stdownload.php?file=%s",
        "https://wad-archive.com",
        "http://fu-berlin.de",
        "https://bunnny.org",
        "https://archive.org",
        "https://archive.org",
        "https://archive.org",
        "https://allfearthesentinel.net",
        "https://allfearthesentinel.com",
        "https://fapnow.xyz",
        "https://allfearthesentinel.com",
        "https://the-sentinel.net",
        "https://allfearthesentinel.net",
        "https://best-ever.org",
        "https://doomshack.org",
        "https://frozensun.org",
        "https://euro-sentinel.net",
        "https://idgames.com",
        "https://dogsoft.net",
        "http://allfearthesentinel.com",
        "https://wadhost.net",
        "https://doomshack.orggetwad.php?search=%s",
        "https://doomleague.orggetwad.php?search=%s",
        "https://doomworld.com",
        "https://quaddicted.com",
        "https://infania.net",
        "https://syringanetworks.net",
        "https://mancubus.net"
    };

    public event Action<string, long, long>? ProgressChanged;
    public event Action<string>? StatusChanged;

    public async Task<bool> DownloadWadAsync(string filename, string destinationFolder, string expectedHash = "")
    {
        try
        {
            if (filename.Equals("SIGIL_SHREDS.wad", StringComparison.OrdinalIgnoreCase) ||
                filename.Equals("DOOM.wad", StringComparison.OrdinalIgnoreCase) ||
                filename.Equals("DOOM2.wad", StringComparison.OrdinalIgnoreCase))
            {
                StatusChanged?.Invoke($"Skipping download of {filename} (User must provide it manually).");
                return false;
            }

            if (!Directory.Exists(destinationFolder))
            {
                try { Directory.CreateDirectory(destinationFolder); } catch { }
            }

            string destPath = Path.Combine(destinationFolder, filename);
            string partPath = destPath + ".part";

            if (File.Exists(destPath))
            {
                if (string.IsNullOrEmpty(expectedHash)) return true;
                if (VerifyHash(destPath, expectedHash)) return true;

                try { File.Delete(destPath); } catch { }
            }

            StatusChanged?.Invoke($"Searching for {filename}...");
            var searchNames = new[] { filename, filename.ToLower() };

            foreach (var mirror in Mirrors)
            {
                foreach (var nameToTry in searchNames)
                {
                    try
                    {
                        string url = mirror.Contains("%s")
                            ? mirror.Replace("%s", nameToTry)
                            : mirror.TrimEnd('/') + "/" + nameToTry;

                        StatusChanged?.Invoke($"Trying {mirror}...");

                        using var request = new HttpRequestMessage(HttpMethod.Get, url);
                        request.Headers.Referrer = new Uri("https://allfearthesentinel.com");

                        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

                        if (response.IsSuccessStatusCode)
                        {
                            var contentType = response.Content.Headers.ContentType?.MediaType?.ToLower();
                            if (contentType != null && contentType.Contains("text/html")) continue;

                            long? totalBytes = response.Content.Headers.ContentLength;
                            if (totalBytes.HasValue && totalBytes.Value < 100) continue;

                            using (var stream = await response.Content.ReadAsStreamAsync())
                            using (var fileStream = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None))
                            {
                                byte[] buffer = new byte[16384];
                                long totalRead = 0;
                                int read;
                                DateTime lastUpdate = DateTime.MinValue;

                                while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                                {
                                    await fileStream.WriteAsync(buffer, 0, read);
                                    totalRead += read;

                                    if ((DateTime.Now - lastUpdate).TotalMilliseconds > 100)
                                    {
                                        ProgressChanged?.Invoke(filename, totalRead, totalBytes ?? 0);
                                        lastUpdate = DateTime.Now;
                                    }
                                }

                                ProgressChanged?.Invoke(filename, totalRead, totalBytes ?? 0);
                            }

                            if (File.Exists(partPath))
                            {
                                if (!string.IsNullOrEmpty(expectedHash))
                                {
                                    if (!VerifyHash(partPath, expectedHash))
                                    {
                                        File.Delete(partPath);
                                        continue;
                                    }
                                }
                                else if (new FileInfo(partPath).Length < 100)
                                {
                                    File.Delete(partPath);
                                    continue;
                                }

                                if (File.Exists(destPath)) File.Delete(destPath);
                                File.Move(partPath, destPath);

                                StatusChanged?.Invoke($"Downloaded {filename} successfully.");
                                return true;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[WadDownloader] Mirror {mirror} failed: {ex.Message}");
                    }
                }
            }

            StatusChanged?.Invoke($"Failed to find {filename} on any mirror.");
            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WadDownloader] Critical error: {ex.Message}");
            return false;
        }
    }
    public async Task<bool> DownloadWadSimpleAsync(string filename, string destinationFolder)
    {
        // Simply forward the request to your fully realized, secure method
        // This removes code duplication and guarantees mirror lookups behave exactly the same way
        return await DownloadWadAsync(filename, destinationFolder, expectedHash: "");
    }

    public static class Crc32
    {
        private static readonly uint[] Table;

        static Crc32()
        {
            Table = new uint[256];
            const uint poly = 0xEDB88320;

            for (uint i = 0; i < Table.Length; i++)
            {
                uint crc = i;
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 1) == 1)
                        crc = (crc >> 1) ^ poly;
                    else
                        crc >>= 1;
                }
                Table[i] = crc;
            }
        }

        public static string Compute(string filePath)
        {
            using var stream = File.OpenRead(filePath);
            uint crc = 0xFFFFFFFF;

            int b;
            while ((b = stream.ReadByte()) != -1)
            {
                crc = (crc >> 8) ^ Table[(crc ^ (byte)b) & 0xFF];
            }

            crc ^= 0xFFFFFFFF;
            return crc.ToString("x8");
        }
    }

    public static bool VerifyHash(string filePath, string expectedHash)
    {
        try
        {
            if (string.IsNullOrEmpty(expectedHash))
                return true;

            string actual = Crc32.Compute(filePath).ToLower();
            string expected = expectedHash.ToLower();

            if (actual != expected)
            {
                Debug.WriteLine($"[CRC FAIL] {Path.GetFileName(filePath)}"); Debug.WriteLine($"Expected: {expected}"); Debug.WriteLine($"Actual:   {actual}");
            }
            return actual == expected;
        }
        catch { return false; }
    }
}