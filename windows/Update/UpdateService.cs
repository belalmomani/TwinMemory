using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;

namespace TwinMemory.Update;

public record UpdateInfo(string Version, string Notes, string Url, string Sha256, long Size);

/// <summary>
/// Update source: the repository's latest GitHub Release (update.json produced by the release workflow). The download must match the
/// published SHA-256 and size and look like TwinMemory before it is used. The running exe is renamed to .old (Windows allows that),
/// the new exe is put in its place and started; on failure the old exe is restored. User data lives elsewhere and is never touched.
/// Note: the exe is not Authenticode-signed (no certificate yet), so Windows SmartScreen may warn on a manual download; in-app the
/// hash from the trusted release is the integrity check.
/// </summary>
public static class UpdateService
{
    public const string ManifestUrl = "https://github.com/belalmomani/TwinMemory/releases/latest/download/update.json";
    const string TrustedPrefix = "https://github.com/belalmomani/TwinMemory/releases/";
    static readonly HttpClient Http = Create();

    static HttpClient Create()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(20) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("TwinMemory/" + Current);
        return c;
    }

    public static Version Current => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);
    static string UpdateDir => Path.Combine(App.DataDir, "update");

    public static async Task<(UpdateInfo? Info, string? Error)> CheckAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var text = await Http.GetStringAsync(ManifestUrl, cts.Token);
            using var d = JsonDocument.Parse(text);
            var r = d.RootElement;
            var w = r.GetProperty("windows");
            var info = new UpdateInfo(
                r.GetProperty("version").GetString() ?? "", r.TryGetProperty("notes", out var n) ? n.GetString() ?? "" : "",
                w.GetProperty("url").GetString() ?? "", (w.GetProperty("sha256").GetString() ?? "").ToLowerInvariant(), w.GetProperty("size").GetInt64());
            if (!info.Url.StartsWith(TrustedPrefix)) return (null, S.T("مصدر التحديث غير موثوق؛ رُفض"));
            if (!Version.TryParse(info.Version.TrimStart('v'), out var v)) return (null, S.T("تعذّر التحقق من التحديثات (تأكد من الشبكة، أو أنه لم يُنشر إصدار بعد)"));
            return (v > Current ? info : null, null);
        }
        catch (Exception)
        {
            return (null, S.T("تعذّر التحقق من التحديثات (تأكد من الشبكة، أو أنه لم يُنشر إصدار بعد)"));
        }
    }

    public static async Task<(string? Path, string? Error)> DownloadAsync(UpdateInfo info, IProgress<int> progress)
    {
        try
        {
            Directory.CreateDirectory(UpdateDir);
            var dest = Path.Combine(UpdateDir, "TwinMemory-" + info.Version + ".exe");
            var part = dest + ".part";
            using var resp = await Http.GetAsync(info.Url, HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();
            using var sha = SHA256.Create();
            long done = 0; int last = -1;
            await using (var input = await resp.Content.ReadAsStreamAsync())
            await using (var output = File.Create(part))
            {
                var buf = new byte[81920]; int n;
                while ((n = await input.ReadAsync(buf)) > 0)
                {
                    await output.WriteAsync(buf.AsMemory(0, n)); sha.TransformBlock(buf, 0, n, null, 0); done += n;
                    var p = info.Size > 0 ? (int)Math.Min(100, done * 100 / info.Size) : 0;
                    if (p != last) { last = p; progress.Report(p); }
                }
            }
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            var hex = Convert.ToHexString(sha.Hash!).ToLowerInvariant();
            if (done != info.Size || hex != info.Sha256) { File.Delete(part); return (null, S.T("الملف المُنزَّل لا يطابق البصمة المنشورة؛ لم يُثبَّت")); }
            var fv = FileVersionInfo.GetVersionInfo(part);
            if (!string.IsNullOrEmpty(fv.ProductName) && !fv.ProductName.Equals("TwinMemory", StringComparison.OrdinalIgnoreCase))
            { File.Delete(part); return (null, S.T("الملف ليس برنامج TwinMemory؛ رُفض")); }
            if (File.Exists(dest)) File.Delete(dest);
            File.Move(part, dest);
            return (dest, null);
        }
        catch (Exception ex) { return (null, S.T("فشل التنزيل") + ": " + ex.Message); }
    }

    /// <summary>Returns an error message, or null after the new version was started (this process then exits).</summary>
    public static string? ApplyAndRestart(string newExe)
    {
        var exe = Environment.ProcessPath!;
        var old = exe + ".old";
        try { if (File.Exists(old)) File.Delete(old); File.Move(exe, old); }
        catch (Exception ex) { return ex.Message; }
        try { File.Copy(newExe, exe, false); }
        catch (Exception ex) { try { File.Move(old, exe); } catch { } return ex.Message; }
        try { Process.Start(new ProcessStartInfo(exe, $"--restart {Environment.ProcessId}") { UseShellExecute = true }); }
        catch (Exception ex) { try { File.Delete(exe); File.Move(old, exe); } catch { } return ex.Message; }
        Application.Current.Shutdown();
        return null;
    }

    /// <summary>Called at startup (after the previous process exited): removes leftovers of a finished update.</summary>
    public static void Cleanup()
    {
        try { var old = Environment.ProcessPath + ".old"; if (File.Exists(old)) File.Delete(old); } catch { }
        try { if (Directory.Exists(UpdateDir)) Directory.Delete(UpdateDir, true); } catch { }
    }
}
