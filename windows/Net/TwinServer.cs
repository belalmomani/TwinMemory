using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TwinMemory.Core;

namespace TwinMemory.Net;

/// <summary>
/// Local HTTPS server (LAN only, no cloud). Pairing: 8-char one-time code bound to the server certificate fingerprint via HMAC,
/// so a man-in-the-middle with another certificate cannot complete pairing. After pairing every call needs a device token.
/// Transfers are resumable (offset) and verified with SHA-256 before an item is created.
/// </summary>
public class TwinServer
{
    public const int Port = 47821, DiscoveryPort = 47822;
    const long MaxFile = 4L * 1024 * 1024 * 1024;
    const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    static readonly HashSet<string> Kinds = new() { "TEXT", "LINK", "IMAGE", "VIDEO", "DOCUMENT", "FILE" };

    readonly Database _db;
    readonly FileStore _store;
    readonly string _incoming;
    readonly object _lk = new();
    X509Certificate2? _cert;
    WebApplication? _app;
    PairSession? _pair;

    public string Fingerprint { get; private set; } = "";
    public string Name { get; } = Environment.MachineName;
    public DateTime LastSeenUtc { get; private set; } = DateTime.MinValue;
    public string LastDeviceName { get; private set; } = "";
    public bool Running { get; private set; }
    public event Action? DataChanged;
    public string LogPath { get; private set; } = "";

    public void Log(string line)
    {
        try
        {
            if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 1_000_000) File.Delete(LogPath);
            File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {line}{Environment.NewLine}");
        }
        catch { }
    }

    class PairSession { public string Code = ""; public DateTime Expires; public int Attempts; }

    public TwinServer(Database db, FileStore store, string dataDir)
    {
        _db = db; _store = store;
        _incoming = Path.Combine(dataDir, "incoming");
        Directory.CreateDirectory(_incoming);
        _certDir = dataDir;
        LogPath = Path.Combine(dataDir, "server.log");
    }
    readonly string _certDir;

    public async Task StartAsync()
    {
        try
        {
            _cert = CertStore.LoadOrCreate(_certDir);
            Fingerprint = CertStore.Fingerprint(_cert);
            var b = WebApplication.CreateBuilder();
            b.Logging.ClearProviders();
            b.WebHost.ConfigureKestrel(o =>
            {
                o.Limits.MaxRequestBodySize = null;
                o.ListenAnyIP(Port, l => { l.Protocols = HttpProtocols.Http1; l.UseHttps(_cert); }); // HTTP/1.1 only: simplest for the phone client
            });
            _app = b.Build();
            _app.Use(async (ctx, next) =>
            {
                await next();
                Log($"{ctx.Request.Method} {ctx.Request.Path} -> {ctx.Response.StatusCode} from {ctx.Connection.RemoteIpAddress}");
            });
            Map(_app);
            await _app.StartAsync();
            Running = true;
            Log($"server started on port {Port}, fingerprint {Fingerprint[..16]}, addresses: {string.Join(", ", LocalAddresses())}");
            StartDiscovery();
            DataChanged?.Invoke();
        }
        catch (Exception ex)
        {
            Log("START FAILED: " + ex);
            throw;
        }
    }

    // ---- pairing code (shown in the UI) ----
    public (string Code, DateTime Expires) NewPairCode()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 8; i++) sb.Append(Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]);
        var s = new PairSession { Code = sb.ToString(), Expires = DateTime.UtcNow.AddMinutes(2) };
        lock (_lk) _pair = s;
        return (s.Code, s.Expires);
    }

    /// <summary>LAN IPv4 addresses, skipping virtual adapters (VMware, Hyper-V, WSL, VPN...) that the phone cannot reach.</summary>
    public static List<string> LocalAddresses()
    {
        static IEnumerable<string> Addrs(IEnumerable<NetworkInterface> nics) => nics
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !a.Address.ToString().StartsWith("169.254."))
            .Select(a => a.Address.ToString());
        var up = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback).ToList();
        string[] virt = { "virtual", "vmware", "vbox", "hyper-v", "vethernet", "wsl", "bluetooth", "tap", "tun", "vpn", "tailscale", "zerotier", "docker" };
        var real = up.Where(n => !virt.Any(v => (n.Name + " " + n.Description).Contains(v, StringComparison.OrdinalIgnoreCase))).ToList();
        var list = Addrs(real.Count > 0 ? real : up).Distinct().ToList();
        return list.OrderBy(a => a.StartsWith("192.168.") ? 0 : a.StartsWith("10.") ? 1 : 2).ToList();
    }

    // ---- helpers ----
    static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
    static string Str(JsonElement r, string name) => r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    static long Num(JsonElement r, string name) => r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : 0;
    static IResult Err(int code, string error, object? extra = null) => Results.Json(new { error, extra }, statusCode: code);
    string PartPath(string id) => Path.Combine(_incoming, id + ".part");
    void Notify() => DataChanged?.Invoke();

    DeviceInfo? Auth(HttpContext c)
    {
        var id = c.Request.Headers["X-Device-Id"].ToString();
        var auth = c.Request.Headers.Authorization.ToString();
        if (!auth.StartsWith("Bearer ") || id.Length == 0) return null;
        var dev = _db.GetDevice(id);
        if (dev == null) return null;
        var a = Encoding.ASCII.GetBytes(Hash(auth[7..]));
        var b = Encoding.ASCII.GetBytes(dev.TokenHash);
        if (a.Length != b.Length || !CryptographicOperations.FixedTimeEquals(a, b)) return null;
        _db.TouchDevice(id);
        LastSeenUtc = DateTime.UtcNow; LastDeviceName = dev.Name;
        return dev;
    }

    void Map(WebApplication app)
    {
        app.MapGet("/info", () => Results.Json(new { app = "TwinMemory", name = Name, id = Fingerprint[..16], version = "1.0.0" }));

        app.MapGet("/ping", (HttpContext c) => Auth(c) == null ? Err(401, "unauthorized") : Results.Json(new { ok = true }));

        app.MapPost("/pair", async (HttpContext c) =>
        {
            using var d = await JsonDocument.ParseAsync(c.Request.Body);
            var r = d.RootElement;
            string devId = Str(r, "deviceId"), devName = Str(r, "deviceName"), proof = Str(r, "proof");
            if (!Guid.TryParse(devId, out _) || devName.Length > 80) return Err(400, "bad_request");
            PairSession? s; string code;
            lock (_lk)
            {
                s = _pair;
                if (s == null || DateTime.UtcNow > s.Expires || s.Attempts >= 5) return Err(403, "no_session");
                s.Attempts++; code = s.Code;
            }
            var expect = Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes(code), Encoding.UTF8.GetBytes($"TM-PAIR|{devId}|{Fingerprint}")));
            var pa = Encoding.ASCII.GetBytes(proof); var pb = Encoding.ASCII.GetBytes(expect);
            if (pa.Length != pb.Length || !CryptographicOperations.FixedTimeEquals(pa, pb)) return Err(403, "bad_code");
            var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            _db.UpsertDevice(devId, devName.Length == 0 ? "Phone" : devName, Hash(token));
            lock (_lk) _pair = null;
            Notify();
            return Results.Json(new { token, name = Name, id = Fingerprint[..16] });
        });

        app.MapDelete("/pair", (HttpContext c) =>
        {
            var dev = Auth(c);
            if (dev == null) return Err(401, "unauthorized");
            _db.DeleteDevice(dev.Id); Notify();
            return Results.Json(new { ok = true });
        });

        app.MapPost("/transfers", async (HttpContext c) =>
        {
            var dev = Auth(c);
            if (dev == null) return Err(401, "unauthorized");
            using var d = await JsonDocument.ParseAsync(c.Request.Body);
            var r = d.RootElement;
            var id = Str(r, "id"); var kind = Str(r, "kind");
            if (!Guid.TryParse(id, out _) || !Kinds.Contains(kind)) return Err(400, "bad_request");
            var ex = _db.GetTransfer(id);
            if (ex != null)
            {
                if (ex.DeviceId != dev.Id) return Err(403, "forbidden");
                if (ex.State == "done") return Results.Json(new { state = "done", offset = ex.Size });
                if (ex.State == "failed") return Err(409, "failed_before");
                var pp = PartPath(id);
                return Results.Json(new { state = "receiving", offset = File.Exists(pp) ? new FileInfo(pp).Length : 0L });
            }
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (kind is "TEXT" or "LINK")
            {
                var text = Str(r, "text");
                if (string.IsNullOrWhiteSpace(text) || text.Length > 1_000_000) return Err(400, "bad_text");
                var link = Classify.IsSingleUrl(text);
                var itemId = Guid.NewGuid().ToString();
                _db.Insert(new Item
                {
                    Id = itemId, Kind = link ? "LINK" : "TEXT", Title = link ? Classify.HostOf(text) : Classify.TitleFor(text), Text = text.Trim(),
                    Mime = "text/plain", Size = Encoding.UTF8.GetByteCount(text), Source = "phone", Created = now, Updated = now,
                });
                _db.InsertTransfer(new Transfer { Id = id, DeviceId = dev.Id, Kind = kind, Title = Classify.TitleFor(text), Size = Encoding.UTF8.GetByteCount(text), State = "done", ItemId = itemId, Started = now, Finished = now });
                Notify();
                return Results.Json(new { state = "done", offset = 0 });
            }
            var size = Num(r, "size"); var sha = Str(r, "sha256").ToLowerInvariant();
            var fileName = Classify.SanitizeFileName(Str(r, "fileName"));
            if (size <= 0 || size > MaxFile) return Err(413, "too_large");
            if (!Regex.IsMatch(sha, "^[0-9a-f]{64}$")) return Err(400, "bad_hash");
            try
            {
                if (new DriveInfo(Path.GetPathRoot(_incoming)!).AvailableFreeSpace < size + 200L * 1024 * 1024) return Err(507, "no_space");
            }
            catch { }
            _db.InsertTransfer(new Transfer
            {
                Id = id, DeviceId = dev.Id, Kind = kind, Title = fileName, FileName = fileName, Mime = Str(r, "mime"), Size = size, Sha256 = sha, State = "receiving", Started = now,
            });
            Notify();
            return Results.Json(new { state = "receiving", offset = 0 });
        });

        app.MapPut("/transfers/{id}/data", async (HttpContext c, string id) =>
        {
            var dev = Auth(c);
            if (dev == null) return Err(401, "unauthorized");
            var t = _db.GetTransfer(id);
            if (t == null) return Err(404, "not_found");
            if (t.DeviceId != dev.Id) return Err(403, "forbidden");
            if (t.State != "receiving") return Err(409, "not_receiving");
            if (!long.TryParse(c.Request.Query["offset"].ToString(), out var offset)) return Err(400, "bad_offset");
            var part = PartPath(id);
            long cur = File.Exists(part) ? new FileInfo(part).Length : 0;
            if (offset != cur) return Err(409, "offset", cur);
            long total = cur;
            await using (var fs = new FileStream(part, FileMode.Append, FileAccess.Write))
            {
                var buf = new byte[81920]; int n;
                while ((n = await c.Request.Body.ReadAsync(buf)) > 0)
                {
                    if (total + n > t.Size) return Err(413, "too_much");
                    await fs.WriteAsync(buf.AsMemory(0, n));
                    total += n;
                }
            }
            return Results.Json(new { received = total });
        });

        app.MapPost("/transfers/{id}/complete", async (HttpContext c, string id) =>
        {
            var dev = Auth(c);
            if (dev == null) return Err(401, "unauthorized");
            var t = _db.GetTransfer(id);
            if (t == null) return Err(404, "not_found");
            if (t.DeviceId != dev.Id) return Err(403, "forbidden");
            if (t.State == "done") return Results.Json(new { ok = true, duplicate = false });
            var part = PartPath(id);
            long len = File.Exists(part) ? new FileInfo(part).Length : 0;
            if (len != t.Size) return Err(409, "size", len);
            var actual = await Task.Run(() =>
            {
                using var fs = File.OpenRead(part);
                return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
            });
            if (actual != t.Sha256)
            {
                File.Delete(part);
                _db.SetTransferState(id, "failed", "hash");
                Notify();
                return Err(422, "hash");
            }
            var itemId = Guid.NewGuid().ToString();
            var st = _store.Adopt(itemId, t.FileName, part, t.Size, actual);
            var dup = _db.CountSha(actual) > 0;
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _db.Insert(new Item
            {
                Id = itemId, Kind = t.Kind, Title = t.FileName, FileName = t.FileName, Mime = t.Mime, Size = t.Size, Sha256 = actual, RelPath = st.RelPath,
                Source = "phone", Created = now, Updated = now,
            });
            _db.FinishTransfer(id, itemId);
            Notify();
            return Results.Json(new { ok = true, duplicate = dup });
        });

        // ---- computer -> phone: the phone pulls (the phone cannot accept inbound connections) ----
        app.MapGet("/outbox", (HttpContext c) =>
        {
            var dev = Auth(c);
            if (dev == null) return Err(401, "unauthorized");
            var list = _db.ListOutbox(dev.Id).Select(t => new
            {
                id = t.Id, kind = t.Kind, title = t.Title, fileName = t.FileName, mime = t.Mime, size = t.Size, sha256 = t.Sha256,
                text = t.ItemId == null ? null : _db.GetItem(t.ItemId)?.Text,
            }).ToList();
            return Results.Json(new { items = list });
        });

        app.MapGet("/outbox/{id}/data", (HttpContext c, string id) =>
        {
            var dev = Auth(c);
            if (dev == null) return Err(401, "unauthorized");
            var t = _db.GetTransfer(id);
            if (t == null || t.Direction != "out" || t.DeviceId != dev.Id) return Err(404, "not_found");
            var item = t.ItemId == null ? null : _db.GetItem(t.ItemId);
            var path = item == null ? null : _store.PathFor(item.RelPath);
            if (path == null) return Err(410, "gone");
            return Results.File(path, "application/octet-stream", enableRangeProcessing: true); // supports Range for resume
        });

        app.MapPost("/outbox/{id}/ack", (HttpContext c, string id) =>
        {
            var dev = Auth(c);
            if (dev == null) return Err(401, "unauthorized");
            var t = _db.GetTransfer(id);
            if (t == null || t.Direction != "out" || t.DeviceId != dev.Id) return Err(404, "not_found");
            _db.SetTransferState(id, "done", null); // the phone confirmed size + SHA-256 before acking
            Notify();
            return Results.Json(new { ok = true });
        });

        app.MapPost("/outbox/{id}/fail", (HttpContext c, string id) =>
        {
            var dev = Auth(c);
            if (dev == null) return Err(401, "unauthorized");
            var t = _db.GetTransfer(id);
            if (t == null || t.Direction != "out" || t.DeviceId != dev.Id) return Err(404, "not_found");
            _db.SetTransferState(id, "failed", "phone");
            Notify();
            return Results.Json(new { ok = true });
        });
    }

    // UDP discovery: the phone broadcasts a short message and gets the address/port back (manual IP entry remains as fallback).
    void StartDiscovery()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var u = new UdpClient(DiscoveryPort) { EnableBroadcast = true };
                while (true)
                {
                    var r = await u.ReceiveAsync();
                    if (Encoding.UTF8.GetString(r.Buffer) != "TWINMEMORY_DISCOVER") continue;
                    var reply = JsonSerializer.SerializeToUtf8Bytes(new { app = "TwinMemory", name = Name, id = Fingerprint[..16], port = Port });
                    await u.SendAsync(reply, reply.Length, r.RemoteEndPoint);
                }
            }
            catch { }
        });
    }
}
