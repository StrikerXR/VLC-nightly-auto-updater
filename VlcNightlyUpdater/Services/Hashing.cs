using System.Security.Cryptography;

namespace VlcNightlyUpdater.Services;

/// <summary>SHA-512 of a stream as upper-case hex (works on every .NET version this project is built for).</summary>
public static class Hashing
{
    public static Task<string> Sha512HexAsync(Stream s, CancellationToken ct) => Task.Run(() =>
    {
        using var sha = SHA512.Create();
        var buf = new byte[81920];
        int n;
        while ((n = s.Read(buf, 0, buf.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            sha.TransformBlock(buf, 0, n, null, 0);
        }
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha.Hash!);
    }, ct);
}
