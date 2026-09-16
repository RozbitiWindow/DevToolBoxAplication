using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Network tab: TCP port scan of a host (single port, list or range) and a list
/// of ports that are listening on this machine, with the owning process.
/// </summary>
public class NetworkPanel : MonoBehaviour
{
    [Header("Scan")]
    [SerializeField] private TMP_InputField hostInput;
    [SerializeField] private TMP_InputField portsInput;
    [SerializeField] private Button scanButton;
    [SerializeField] private Button stopButton;
    [SerializeField] private Button copyButton;
    [SerializeField] private TMP_Text scanStatus;
    [SerializeField] private TMP_Text scanOutput;
    [SerializeField] private ScrollRect scanScroll;
    // Windows takes ~2 s to report a refused loopback connect, so throughput is
    // workers / timeout: 512 / 0.4 s ~ 1300 ports/s. Raise the timeout for slow WAN hosts.
    [SerializeField] private int timeoutMs = 400;
    [SerializeField] private int maxParallel = 512;

    [Header("Local ports")]
    [SerializeField] private Button refreshLocalButton;
    [SerializeField] private TMP_InputField localFilterInput;
    [SerializeField] private TMP_Text localOutput;
    [SerializeField] private TMP_Text localStatus;

    private const string ColorOpen = "#7AFFB0";
    private const string ColorDim = "#9A8FBF";
    private const string ColorErr = "#FF7A7A";
    private const string ColorCmd = "#C9B8FF";

    private static readonly Dictionary<int, string> KnownServices = new Dictionary<int, string>
    {
        {20,"ftp-data"},{21,"ftp"},{22,"ssh"},{23,"telnet"},{25,"smtp"},{53,"dns"},{67,"dhcp"},{80,"http"},{110,"pop3"},
        {123,"ntp"},{135,"msrpc"},{137,"netbios"},{139,"netbios-ssn"},{143,"imap"},{161,"snmp"},{389,"ldap"},{443,"https"},
        {445,"smb"},{465,"smtps"},{587,"smtp-submission"},{631,"ipp"},{636,"ldaps"},{993,"imaps"},{995,"pop3s"},{1080,"socks"},
        {1433,"mssql"},{1521,"oracle"},{1883,"mqtt"},{2375,"docker"},{2376,"docker-tls"},{3000,"dev-server"},{3306,"mysql"},
        {3389,"rdp"},{4200,"angular"},{5000,"dev-server"},{5173,"vite"},{5432,"postgres"},{5672,"amqp"},{5900,"vnc"},
        {6379,"redis"},{6443,"kubernetes"},{7000,"dev"},{8000,"http-alt"},{8080,"http-proxy"},{8090,"http-alt"},{8443,"https-alt"},
        {8888,"jupyter"},{9000,"php-fpm/portainer"},{9090,"prometheus"},{9200,"elasticsearch"},{11434,"ollama"},{25565,"minecraft"},
        {27017,"mongodb"}
    };

    private static readonly int[] CommonPorts =
        { 21, 22, 23, 25, 53, 80, 110, 135, 139, 143, 443, 445, 993, 995, 1433, 1521, 1883, 2375, 3000, 3306, 3389, 4200, 5000,
          5173, 5432, 5672, 5900, 6379, 8000, 8080, 8090, 8443, 8888, 9000, 9090, 9200, 11434, 27017 };

    private readonly ConcurrentQueue<string> pending = new ConcurrentQueue<string>();
    private readonly List<string> openLines = new List<string>();
    private CancellationTokenSource cts;
    private int scanned, total, openCount;
    private bool scanning, dirty;
    private Stopwatch stopwatch;
    private ShellRunner.Job localJob;
    private readonly List<string> netstatLines = new List<string>();

    private void Awake()
    {
        if (scanButton != null) scanButton.onClick.AddListener(StartScan);
        if (stopButton != null) stopButton.onClick.AddListener(StopScan);
        if (copyButton != null) copyButton.onClick.AddListener(() => { if (openLines.Count > 0) GUIUtility.systemCopyBuffer = string.Join("\n", openLines); });
        if (refreshLocalButton != null) refreshLocalButton.onClick.AddListener(RefreshLocal);
        if (localFilterInput != null) localFilterInput.onValueChanged.AddListener(_ => RenderLocal());
        if (hostInput != null) hostInput.onSubmit.AddListener(_ => StartScan());
        if (portsInput != null) portsInput.onSubmit.AddListener(_ => StartScan());
        if (hostInput != null && string.IsNullOrEmpty(hostInput.text)) hostInput.text = "127.0.0.1";
        if (portsInput != null && string.IsNullOrEmpty(portsInput.text)) portsInput.text = "common";
        SetScanStatus("Host + ports (e.g. 8080, 1-1024, 22,80,443 or 'common'), then Scan.", ColorDim);
    }

    private void OnEnable()
    {
        if (netstatLines.Count == 0) RefreshLocal();
    }

    // A running scan keeps going while another tab is open; results are drained when we come back.
    private void OnDestroy()
    {
        cts?.Cancel();
    }

    private void Update()
    {
        while (pending.TryDequeue(out string line)) { openLines.Add(line); dirty = true; }

        if (scanning)
        {
            SetScanStatus("Scanning " + scanned + "/" + total + "  -  " + openCount + " open  -  " + stopwatch.Elapsed.TotalSeconds.ToString("0.0") + " s", ColorDim);
            if (scanned >= total && pending.IsEmpty) FinishScan(false);
        }

        if (dirty)
        {
            dirty = false;
            if (scanOutput != null) scanOutput.text = string.Join("\n", openLines);
            if (scanScroll != null) scanScroll.verticalNormalizedPosition = 0f;
        }
    }

    // ---------- scan ----------

    public void StartScan()
    {
        if (scanning) return;
        string host = hostInput != null ? hostInput.text.Trim() : "";
        if (host.Length == 0) { SetScanStatus("Enter a host or IP.", ColorErr); return; }

        List<int> ports = ParsePorts(portsInput != null ? portsInput.text : "common", out string error);
        if (ports == null) { SetScanStatus(error, ColorErr); return; }

        openLines.Clear();
        dirty = true;
        scanned = 0; total = ports.Count; openCount = 0;
        scanning = true;
        stopwatch = Stopwatch.StartNew();
        cts = new CancellationTokenSource();
        SetScanStatus("Resolving " + host + "...", ColorDim);

        Task.Run(() => ScanAsync(host, ports, cts.Token));
    }

    public void StopScan()
    {
        if (!scanning) return;
        cts?.Cancel();
        FinishScan(true);
    }

    private void FinishScan(bool cancelled)
    {
        scanning = false;
        stopwatch?.Stop();
        string summary = (cancelled ? "Stopped" : "Done") + "  -  " + openCount + " open of " + scanned + " scanned in " + (stopwatch != null ? stopwatch.Elapsed.TotalSeconds.ToString("0.0") : "?") + " s";
        SetScanStatus(summary, openCount > 0 ? ColorOpen : ColorDim);
        if (openCount == 0 && !cancelled) pending.Enqueue("<color=" + ColorDim + ">No open TCP ports found.</color>");
    }

    private async Task ScanAsync(string host, List<int> ports, CancellationToken token)
    {
        IPAddress address;
        try
        {
            if (!IPAddress.TryParse(host, out address))
            {
                IPAddress[] all = await Dns.GetHostAddressesAsync(host);
                address = Array.Find(all, a => a.AddressFamily == AddressFamily.InterNetwork) ?? all[0];
            }
        }
        catch (Exception e)
        {
            pending.Enqueue("<color=" + ColorErr + ">Could not resolve '" + host + "': " + e.Message + "</color>");
            Interlocked.Exchange(ref scanned, total);
            return;
        }

        pending.Enqueue("<color=" + ColorDim + ">Scanning " + address + " (" + ports.Count + " ports, timeout " + timeoutMs + " ms)</color>");

        // Dedicated worker threads instead of the thread pool: Mono grows the pool
        // slowly (~2 threads/s), which throttled the scan to ~200 ports/s.
        var queue = new ConcurrentQueue<int>(ports);
        int workers = Mathf.Clamp(maxParallel, 1, Mathf.Max(1, ports.Count));
        var threads = new List<Thread>(workers);
        for (int i = 0; i < workers; i++)
        {
            var t = new Thread(() =>
            {
                while (!token.IsCancellationRequested && queue.TryDequeue(out int port))
                {
                    try
                    {
                        if (IsOpen(address, port))
                        {
                            Interlocked.Increment(ref openCount);
                            string service = KnownServices.TryGetValue(port, out string s) ? "  " + s : "";
                            pending.Enqueue("<color=" + ColorOpen + ">" + address + ":" + port + "</color><color=" + ColorDim + ">" + service + "</color>");
                        }
                    }
                    finally { Interlocked.Increment(ref scanned); }
                }
            }) { IsBackground = true, Name = "PortScan-" + i };
            threads.Add(t);
            t.Start();
        }
        foreach (Thread t in threads) t.Join();
        if (token.IsCancellationRequested) Interlocked.Exchange(ref scanned, total);
    }

    private bool IsOpen(IPAddress address, int port)
    {
        using (var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp))
        {
            try
            {
                IAsyncResult result = socket.BeginConnect(address, port, null, null);
                if (!result.AsyncWaitHandle.WaitOne(timeoutMs, false)) return false; // filtered / no answer
                socket.EndConnect(result);                                          // throws when refused
                return socket.Connected;
            }
            catch (SocketException) { return false; }
            catch (ObjectDisposedException) { return false; }
        }
    }

    // "80", "1-1024", "22,80,443", "common" (or a mix, comma separated)
    private static List<int> ParsePorts(string text, out string error)
    {
        error = null;
        var result = new SortedSet<int>();
        foreach (string rawPart in (text ?? "").Split(',', ';', ' '))
        {
            string part = rawPart.Trim();
            if (part.Length == 0) continue;
            if (part.Equals("common", StringComparison.OrdinalIgnoreCase)) { foreach (int p in CommonPorts) result.Add(p); continue; }
            if (part.Equals("all", StringComparison.OrdinalIgnoreCase)) { for (int p = 1; p <= 65535; p++) result.Add(p); continue; }

            int dash = part.IndexOf('-');
            if (dash > 0)
            {
                if (!int.TryParse(part.Substring(0, dash), out int from) || !int.TryParse(part.Substring(dash + 1), out int to) || from < 1 || to > 65535 || from > to)
                { error = "Invalid range '" + part + "'"; return null; }
                for (int p = from; p <= to; p++) result.Add(p);
            }
            else
            {
                if (!int.TryParse(part, out int p) || p < 1 || p > 65535) { error = "Invalid port '" + part + "'"; return null; }
                result.Add(p);
            }
        }
        if (result.Count == 0) { error = "No ports given."; return null; }
        return new List<int>(result);
    }

    // ---------- local listening ports ----------

    public void RefreshLocal()
    {
        if (localJob != null && localJob.IsRunning) return;
        netstatLines.Clear();
        if (localStatus != null) localStatus.text = "<color=" + ColorDim + ">Reading netstat...</color>";

        string cmd = ShellRunner.IsWindows ? "netstat -ano -p tcp" : "ss -ltnp";
        localJob = ShellRunner.Instance.Run(cmd, null,
            (line, isErr) => { if (!isErr) netstatLines.Add(line); },
            code =>
            {
                if (ShellRunner.IsWindows) ResolveProcessNames();
                else RenderLocal();
            });
    }

    // netstat gives PIDs only; map them to names with one tasklist call.
    private void ResolveProcessNames()
    {
        var names = new Dictionary<string, string>();
        ShellRunner.Instance.Run("tasklist /fo csv /nh", null,
            (line, isErr) =>
            {
                if (isErr) return;
                string[] cols = line.Split(new[] { "\",\"" }, StringSplitOptions.None);
                if (cols.Length >= 2) names[cols[1].Trim('"')] = cols[0].Trim('"');
            },
            code => { processNames = names; RenderLocal(); });
    }

    private Dictionary<string, string> processNames = new Dictionary<string, string>();

    private void RenderLocal()
    {
        if (localOutput == null) return;
        string filter = localFilterInput != null ? localFilterInput.text.Trim() : "";
        var sb = new StringBuilder();
        int count = 0;

        foreach (string raw in netstatLines)
        {
            string line = raw.Trim();
            if (ShellRunner.IsWindows)
            {
                if (!line.StartsWith("TCP") || !line.Contains("LISTENING")) continue;
                string[] c = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (c.Length < 5) continue;
                string local = c[1], pid = c[4];
                string proc = processNames.TryGetValue(pid, out string n) ? n : "?";
                string entry = local.PadRight(24) + pid.PadLeft(6) + "  " + proc;
                if (filter.Length > 0 && entry.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                int colon = local.LastIndexOf(':');
                string portStr = colon >= 0 ? local.Substring(colon + 1) : "";
                string service = int.TryParse(portStr, out int p) && KnownServices.TryGetValue(p, out string s) ? "  <color=" + ColorDim + ">" + s + "</color>" : "";
                sb.AppendLine("<color=" + ColorOpen + ">" + local.PadRight(24) + "</color>" + pid.PadLeft(6) + "  " + proc + service);
            }
            else
            {
                if (!line.StartsWith("LISTEN")) continue;
                if (filter.Length > 0 && line.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                sb.AppendLine(line);
            }
            count++;
        }

        localOutput.text = sb.Length > 0 ? sb.ToString().TrimEnd() : "<color=" + ColorDim + ">Nothing is listening (or netstat unavailable).</color>";
        if (localStatus != null) localStatus.text = "<color=" + ColorDim + ">" + count + " listening TCP ports" + (filter.Length > 0 ? " matching '" + filter + "'" : "") + "</color>";
    }

    private void SetScanStatus(string text, string color)
    {
        if (scanStatus != null) scanStatus.text = "<color=" + color + ">" + text + "</color>";
    }
}
