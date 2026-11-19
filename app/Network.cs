using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace D4;

/// <summary>Состояние сети. Kind: wifi, ethernet, other или none.</summary>
public sealed record NetStatus(bool Connected, string Kind, string Adapter, string? Ip, long SpeedMbps);

public sealed record WifiNet(string Ssid, int Signal);

public static class NetInfo
{
    /// <summary>Находит активное подключение (с шлюзом), предпочитая Ethernet, затем Wi-Fi.</summary>
    public static NetStatus Read()
    {
        try
        {
            var candidates = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up
                         && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                         && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                .Select(n => (Nic: n, Props: n.GetIPProperties()))
                .Where(x => x.Props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)))
                .OrderBy(x => x.Nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? 0
                            : x.Nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? 1 : 2)
                .ToList();

            if (candidates.Count == 0) return new NetStatus(false, "none", "", null, 0);

            var (nic, props) = candidates[0];
            string kind = nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? "wifi"
                        : nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? "ethernet" : "other";
            var ip = props.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString();
            return new NetStatus(true, kind, nic.Name, ip, nic.Speed / 1_000_000);
        }
        catch
        {
            return new NetStatus(false, "none", "", null, 0);
        }
    }

    /// <summary>Запускает netsh и возвращает его вывод. Вызывать не из потока интерфейса.</summary>
    static string RunNetsh(string args)
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var psi = new ProcessStartInfo("netsh", args)
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage),
            };
            using var p = Process.Start(psi);
            if (p == null) return "";
            var text = p.StandardOutput.ReadToEnd();
            p.WaitForExit(4000);
            return text;
        }
        catch
        {
            return "";
        }
    }

    static bool IsPercent(string value) => Regex.IsMatch(value, @"^\d{1,3}\s*%$");

    static int ParsePercent(string value) => int.Parse(Regex.Match(value, @"\d+").Value, CultureInfo.InvariantCulture);

    /// <summary>Имя текущей Wi-Fi сети и уровень сигнала.</summary>
    public static (string? Ssid, int? Signal) ReadWifi()
    {
        string? ssid = null;
        int? signal = null;
        foreach (var line in RunNetsh("wlan show interfaces").Split('\n'))
        {
            int i = line.IndexOf(':');
            if (i < 0) continue;
            var key = line.Substring(0, i).Trim();
            var value = line.Substring(i + 1).Trim();
            if (key == "SSID" && ssid == null) ssid = value;
            else if (signal == null && IsPercent(value)) signal = ParsePercent(value);
        }
        return (ssid, signal);
    }

    /// <summary>Доступные Wi-Fi сети поблизости, самые сильные первыми.</summary>
    public static List<WifiNet> ListWifi()
    {
        var best = new Dictionary<string, int>();
        string? ssid = null;
        foreach (var line in RunNetsh("wlan show networks mode=bssid").Split('\n'))
        {
            int i = line.IndexOf(':');
            if (i < 0) continue;
            var key = line.Substring(0, i).Trim();
            var value = line.Substring(i + 1).Trim();
            if (key.StartsWith("SSID", StringComparison.Ordinal))
            {
                ssid = value.Length > 0 ? value : null; // скрытые сети пропускаем
            }
            else if (ssid != null && IsPercent(value))
            {
                int sig = ParsePercent(value);
                if (!best.TryGetValue(ssid, out var prev) || sig > prev) best[ssid] = sig;
            }
        }
        return best.Select(kv => new WifiNet(kv.Key, kv.Value)).OrderByDescending(n => n.Signal).ToList();
    }

    /// <summary>Имена Wi-Fi сетей, пароли от которых уже сохранены в Windows.</summary>
    public static HashSet<string> SavedProfiles()
    {
        var set = new HashSet<string>();
        foreach (var line in RunNetsh("wlan show profiles").Split('\n'))
        {
            if (line.Length == 0 || !char.IsWhiteSpace(line[0])) continue;
            int i = line.IndexOf(':');
            if (i < 0) continue;
            var value = line.Substring(i + 1).Trim();
            if (value.Length > 0) set.Add(value);
        }
        return set;
    }

    public static void WifiConnect(string ssid)
    {
        if (ssid.Contains('"')) return;
        RunNetsh("wlan connect name=\"" + ssid + "\"");
    }

    public static void WifiDisconnect() => RunNetsh("wlan disconnect");
}
