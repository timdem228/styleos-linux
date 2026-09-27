using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace StyleOS
{
    public static class NetworkCommands
    {
        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.Add("User-Agent", $"StyleOS/{Kernel.Version}");
            return client;
        }

        public static async Task Ping(List<string> args)
        {
            var operands = Io.Operands(Io.StripOptionValues(args, "-c"));
            if (operands.Count == 0) { Io.Error("ping", "usage error: Destination address required"); return; }

            string host = operands[0];
            int count = 4;
            int cIndex = args.IndexOf("-c");
            if (cIndex >= 0 && cIndex + 1 < args.Count) int.TryParse(args[cIndex + 1], out count);

            var times = new List<long>();
            int sent = 0, received = 0;

            using (var ping = new System.Net.NetworkInformation.Ping())
            {
                try
                {
                    Console.WriteLine($"PING {host} 56(84) bytes of data.");
                    for (int i = 0; i < count; i++)
                    {
                        sent++;
                        try
                        {
                            var reply = await ping.SendPingAsync(host, 3000);
                            if (reply.Status == IPStatus.Success)
                            {
                                received++;
                                times.Add(reply.RoundtripTime);
                                Console.WriteLine($"64 bytes from {reply.Address}: icmp_seq={i + 1} ttl={reply.Options?.Ttl ?? 64} time={reply.RoundtripTime} ms");
                            }
                            else Console.WriteLine($"From {host} icmp_seq={i + 1} {reply.Status}");
                        }
                        catch (Exception ex) when (ex.InnerException is SocketException)
                        {
                            Io.Error("ping", $"{host}: Name or service not known");
                            return;
                        }

                        if (i < count - 1) await Task.Delay(700);
                    }

                    Console.WriteLine($"\n--- {host} ping statistics ---");
                    int loss = sent == 0 ? 0 : (sent - received) * 100 / sent;
                    Console.WriteLine($"{sent} packets transmitted, {received} received, {loss}% packet loss");
                    if (times.Count > 0)
                        Console.WriteLine($"rtt min/avg/max = {times.Min()}/{times.Average():0.0}/{times.Max()} ms");
                }
                catch (Exception ex) { Io.Error("ping", ex.Message); }
            }
        }

        public static async Task Curl(List<string> args)
        {
            var operands = Io.Operands(args);
            if (operands.Count == 0) { Io.Error("curl", "try 'curl <url>'"); return; }

            string url = Normalize(operands[0]);
            bool headersOnly = Io.HasFlag(args, "-I", "--head");
            bool showHeaders = Io.HasFlag(args, "-i");

            try
            {
                using var response = await Http.GetAsync(url,
                    headersOnly ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead);

                if (headersOnly || showHeaders)
                {
                    Console.WriteLine($"HTTP/{response.Version} {(int)response.StatusCode} {response.ReasonPhrase}");
                    foreach (var header in response.Headers)
                        Console.WriteLine($"{header.Key}: {string.Join(", ", header.Value)}");
                    foreach (var header in response.Content.Headers)
                        Console.WriteLine($"{header.Key}: {string.Join(", ", header.Value)}");
                    if (headersOnly) return;
                    Console.WriteLine();
                }

                Console.WriteLine(await response.Content.ReadAsStringAsync());
            }
            catch (Exception ex) { Io.Error("curl", ex.Message); }
        }

        public static async Task Wget(List<string> args)
        {
            var operands = Io.Operands(Io.StripOptionValues(args, "-O"));
            if (operands.Count == 0) { Io.Error("wget", "missing URL"); return; }

            string url = Normalize(operands[0]);
            string filename;
            try
            {
                filename = Path.GetFileName(new Uri(url).LocalPath);
            }
            catch { Io.Error("wget", $"invalid URL: {operands[0]}"); return; }

            if (string.IsNullOrEmpty(filename)) filename = "index.html";

            int oIndex = args.IndexOf("-O");
            if (oIndex >= 0 && oIndex + 1 < args.Count) filename = args[oIndex + 1];

            string dest = PathUtil.Resolve(filename);

            try
            {
                Console.WriteLine($"--{DateTime.Now:yyyy-MM-dd HH:mm:ss}--  {url}");
                using var response = await Http.GetAsync(url);
                Console.WriteLine($"HTTP request sent, awaiting response... {(int)response.StatusCode} {response.ReasonPhrase}");
                response.EnsureSuccessStatusCode();

                byte[] data = await response.Content.ReadAsByteArrayAsync();
                File.WriteAllBytes(dest, data);

                Console.WriteLine($"Length: {data.Length} [{PathUtil.HumanSize(data.Length)}]");
                Console.WriteLine($"Saving to: '{filename}'\n");
                Console.WriteLine($"100%[===================>] {PathUtil.HumanSize(data.Length),-10}\n");
                Console.WriteLine($"'{filename}' saved [{data.Length}]");
            }
            catch (Exception ex) { Io.Error("wget", ex.Message); }
        }

        private static string Normalize(string url) =>
            url.StartsWith("http://") || url.StartsWith("https://") ? url : "https://" + url;

        public static void IfConfig()
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write(nic.Name);
                Console.ResetColor();
                Console.WriteLine($": flags=4163<UP,BROADCAST,RUNNING,MULTICAST>  mtu {SafeMtu(nic)}");

                var props = nic.GetIPProperties();
                foreach (var ip in props.UnicastAddresses)
                {
                    if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                        Console.WriteLine($"        inet {ip.Address}  netmask {ip.IPv4Mask}");
                    else if (ip.Address.AddressFamily == AddressFamily.InterNetworkV6)
                        Console.WriteLine($"        inet6 {ip.Address}");
                }

                var mac = nic.GetPhysicalAddress().GetAddressBytes();
                if (mac.Length > 0)
                    Console.WriteLine($"        ether {string.Join(":", mac.Select(b => b.ToString("x2")))}  txqueuelen 1000  ({nic.NetworkInterfaceType})");

                try
                {
                    var stats = nic.GetIPStatistics();
                    Console.WriteLine($"        RX packets {stats.NonUnicastPacketsReceived + stats.UnicastPacketsReceived}  bytes {stats.BytesReceived}");
                    Console.WriteLine($"        TX packets {stats.NonUnicastPacketsSent + stats.UnicastPacketsSent}  bytes {stats.BytesSent}");
                }
                catch { }

                Console.WriteLine();
            }
        }

        private static int SafeMtu(NetworkInterface nic)
        {
            try { return nic.GetIPProperties().GetIPv4Properties()?.Mtu ?? 1500; }
            catch { return 1500; }
        }

        public static void Netstat(List<string> args)
        {
            bool listening = Io.HasFlag(args, "-l");
            var props = IPGlobalProperties.GetIPGlobalProperties();

            Console.WriteLine("Active Internet connections");
            Console.WriteLine($"{"Proto",-6}{"Local Address",-26}{"Foreign Address",-26}State");

            try
            {
                if (!listening)
                {
                    foreach (var conn in props.GetActiveTcpConnections().Take(100))
                        Console.WriteLine($"{"tcp",-6}{conn.LocalEndPoint,-26}{conn.RemoteEndPoint,-26}{conn.State}");
                }

                foreach (var listener in props.GetActiveTcpListeners().Take(60))
                    Console.WriteLine($"{"tcp",-6}{listener,-26}{"0.0.0.0:*",-26}LISTEN");
            }
            catch (Exception ex) { Io.Error("netstat", ex.Message); }
        }

        public static async Task Nslookup(List<string> args)
        {
            var operands = Io.Operands(args);
            if (operands.Count == 0) { Io.Error("nslookup", "missing host"); return; }

            try
            {
                var entry = await Dns.GetHostEntryAsync(operands[0]);
                Console.WriteLine($"Name:    {entry.HostName}");
                foreach (var address in entry.AddressList)
                    Console.WriteLine($"Address: {address}");
            }
            catch (Exception ex) { Io.Error("nslookup", $"can't resolve '{operands[0]}': {ex.Message}"); }
        }

        public static async Task Traceroute(List<string> args)
        {
            var operands = Io.Operands(args);
            if (operands.Count == 0) { Io.Error("traceroute", "missing host"); return; }

            string host = operands[0];
            Console.WriteLine($"traceroute to {host}, 30 hops max, 60 byte packets");

            using var ping = new System.Net.NetworkInformation.Ping();
            for (int ttl = 1; ttl <= 30; ttl++)
            {
                try
                {
                    var options = new PingOptions(ttl, true);
                    var reply = await ping.SendPingAsync(host, 3000, new byte[32], options);

                    string address = reply.Address?.ToString() ?? "*";
                    Console.WriteLine($"{ttl,2}  {address,-40} {reply.RoundtripTime} ms");

                    if (reply.Status == IPStatus.Success) break;
                }
                catch (Exception ex)
                {
                    Io.Error("traceroute", ex.Message);
                    break;
                }
            }
        }

        public static void Hostname(List<string> args)
        {
            var operands = Io.Operands(args);
            if (operands.Count > 0)
            {
                if (Kernel.CurrentUser?.IsRoot != true) { Io.Error("hostname", "you must be root to change the host name"); return; }
                Kernel.Config.Hostname = operands[0];
                ConfigManager.SaveConfig();
                ShellEnv.Set("HOSTNAME", operands[0]);
                return;
            }

            if (Io.HasFlag(args, "-I", "-i"))
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up))
                    foreach (var ip in nic.GetIPProperties().UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork))
                        Console.Write(ip.Address + " ");
                Console.WriteLine();
                return;
            }

            Console.WriteLine(ShellEnv.HostName);
        }

        public static void Ssh(List<string> args)
        {
            var operands = Io.Operands(args);
            if (operands.Count == 0) { Io.Error("ssh", "usage: ssh user@host"); return; }
            Io.Error("ssh", $"connect to host {operands[0]}: StyleOS has no ssh client yet");
        }
    }
}
