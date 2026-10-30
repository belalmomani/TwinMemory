using System.IO;
using System.Security.Cryptography;

namespace TwinMemory.Core;

/// <summary>%LOCALAPPDATA%\TwinMemory\items\&lt;id&gt;\&lt;safe name&gt;; paths are checked against the root.</summary>
public class FileStore
{
    readonly string _root;
    public FileStore(string root) { _root = Path.GetFullPath(root); Directory.CreateDirectory(_root); }

    public record Stored(string RelPath, long Size, string Sha256);

    public Stored Store(string id, string name, Stream input)
    {
        var safe = Classify.SanitizeFileName(name);
        var dir = Path.Combine(_root, id);
        Directory.CreateDirectory(dir);
        var path = Path.GetFullPath(Path.Combine(dir, safe));
        if (!path.StartsWith(_root + Path.DirectorySeparatorChar)) throw new IOException("bad path");
        try
        {
            using var sha = SHA256.Create();
            long total = 0;
            using (var os = File.Create(path))
            {
                var buf = new byte[64 * 1024];
                int n;
                while ((n = input.Read(buf, 0, buf.Length)) > 0) { os.Write(buf, 0, n); sha.TransformBlock(buf, 0, n, null, 0); total += n; }
            }
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            return new Stored(id + "/" + safe, total, Convert.ToHexString(sha.Hash!).ToLowerInvariant());
        }
        catch { Delete(id); throw; }
    }

    /// <summary>Moves an already verified file (same volume) into item storage without copying it again.</summary>
    public Stored Adopt(string id, string name, string sourcePath, long size, string sha256)
    {
        var safe = Classify.SanitizeFileName(name);
        var dir = Path.Combine(_root, id);
        Directory.CreateDirectory(dir);
        var dest = Path.GetFullPath(Path.Combine(dir, safe));
        if (!dest.StartsWith(_root + Path.DirectorySeparatorChar)) throw new IOException("bad path");
        File.Move(sourcePath, dest);
        return new Stored(id + "/" + safe, size, sha256);
    }

    public string? PathFor(string? rel)
    {
        if (rel == null) return null;
        var p = Path.GetFullPath(Path.Combine(_root, rel));
        return p.StartsWith(_root + Path.DirectorySeparatorChar) && File.Exists(p) ? p : null;
    }

    public void Delete(string id)
    {
        var d = Path.GetFullPath(Path.Combine(_root, id));
        if (d.StartsWith(_root + Path.DirectorySeparatorChar) && Directory.Exists(d)) Directory.Delete(d, true);
    }
}
