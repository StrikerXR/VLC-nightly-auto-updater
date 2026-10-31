using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;

namespace VlcNightlyUpdater.Services;

/// <param name="Received">Bytes on disk so far (includes any resumed part).</param>
/// <param name="Total">Full file size, or -1 if the server didn't say.</param>
/// <param name="BytesPerSecond">Smoothed speed; 0 while reconnecting.</param>
/// <param name="Note">Set while waiting to retry ("Connection lost. Retrying in 4s…"), otherwise null.</param>
public readonly record struct DownloadProgress(long Received, long Total, double BytesPerSecond, string? Note = null)
{
    public double Percent => Total > 0 ? Received * 100.0 / Total : 0;

    public TimeSpan? Eta => Total > 0 && BytesPerSecond > 1024
        ? TimeSpan.FromSeconds(Math.Max(0, (Total - Received) / BytesPerSecond))
        : null;
}

/// <summary>
/// Downloads to "file.part" and resumes with HTTP Range after a dropped connection, a stall, or a restart
/// of the app. Retries with backoff; the retry budget refills whenever an attempt makes progress.
/// </summary>
public static class Downloader
{
    public const int MaxAttempts = 6;
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(30);
    private const int DiskFull = unchecked((int)0x80070070);

    public static async Task DownloadAsync(string url, string destPath, IProgress<DownloadProgress> progress, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
        var part = destPath + ".part";
        long total = -1;
        var failures = 0;
        var speed = new SpeedMeter();

        long OnDisk() => File.Exists(part) ? new FileInfo(part).Length : 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var startedAt = OnDisk();

            try
            {
                var have = startedAt;
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                if (have > 0) req.Headers.Range = new RangeHeaderValue(have, null);

                // One token for "connect + every read": if nothing arrives for 30s the attempt is abandoned and retried.
                using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
                stall.CancelAfter(StallTimeout);

                using var resp = await Net.Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, stall.Token)
                    .ConfigureAwait(false);

                if (resp.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
                {
                    // Our .part is already complete or doesn't match the file any more: start clean.
                    File.Delete(part);
                    throw new IOException("Server rejected the resume point; restarting the download.");
                }
                resp.EnsureSuccessStatusCode();

                FileMode mode;
                if (resp.StatusCode == HttpStatusCode.PartialContent && have > 0)
                {
                    var range = resp.Content.Headers.ContentRange;
                    if (range?.From != have)
                    {
                        File.Delete(part);
                        throw new IOException("Server resumed from the wrong offset; restarting the download.");
                    }
                    total = range.Length ?? (resp.Content.Headers.ContentLength is long cl ? have + cl : -1);
                    mode = FileMode.Append;
                }
                else
                {
                    // 200: fresh download, or the server ignored Range. Either way start from byte 0.
                    have = 0;
                    total = resp.Content.Headers.ContentLength ?? -1;
                    mode = FileMode.Create;
                }

                var received = have;
                speed.Reset(received);
                progress.Report(new DownloadProgress(received, total, 0));

                await using (var src = await resp.Content.ReadAsStreamAsync(stall.Token).ConfigureAwait(false))
                await using (var dst = new FileStream(part, mode, FileAccess.Write, FileShare.Read, 81920, useAsync: true))
                {
                    var buf = new byte[81920];
                    var lastReport = Stopwatch.StartNew();
                    int n;
                    while (true)
                    {
                        stall.CancelAfter(StallTimeout); // got data -> push the deadline out again
                        n = await src.ReadAsync(buf, stall.Token).ConfigureAwait(false);
                        if (n == 0) break;

                        await dst.WriteAsync(buf.AsMemory(0, n), ct).ConfigureAwait(false);
                        received += n;

                        var bps = speed.Update(received);
                        if (lastReport.ElapsedMilliseconds >= 100)
                        {
                            progress.Report(new DownloadProgress(received, total, bps));
                            lastReport.Restart();
                        }
                    }
                }

                if (total > 0 && OnDisk() != total)
                    throw new IOException($"Download ended early ({OnDisk()} of {total} bytes).");

                progress.Report(new DownloadProgress(received, total, speed.Current));
                File.Move(part, destPath, overwrite: true);
                return;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && IsRetryable(ex))
            {
                // Made progress this round? Then this wasn't "the same failure again": refill the budget.
                if (OnDisk() > startedAt) failures = 0;
                if (++failures >= MaxAttempts)
                    throw new IOException($"The download failed {MaxAttempts} times in a row: {ex.Message}", ex);

                var wait = Math.Min(20, 2 << (failures - 1)); // 2, 4, 8, 16, 20 seconds
                for (var left = wait; left > 0; left--)
                {
                    progress.Report(new DownloadProgress(OnDisk(), total, 0,
                        $"Connection lost. Retrying in {left}s (attempt {failures + 1} of {MaxAttempts})…"));
                    await Task.Delay(1000, ct).ConfigureAwait(false);
                }
            }
        }
    }

    private static bool IsRetryable(Exception ex) => ex switch
    {
        // 4xx (other than "slow down") won't fix itself by asking again.
        HttpRequestException { StatusCode: { } code } when (int)code is >= 400 and < 500
            && code is not (HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests) => false,
        IOException io when io.HResult == DiskFull => false,
        HttpRequestException or IOException or TimeoutException or OperationCanceledException => true,
        _ => false
    };

    /// <summary>Exponentially smoothed bytes/second, sampled about twice a second.</summary>
    private sealed class SpeedMeter
    {
        private readonly Stopwatch _sw = new();
        private long _last;
        private double _speed;

        public double Current => _speed;

        public void Reset(long bytes)
        {
            _last = bytes;
            _speed = 0;
            _sw.Restart();
        }

        public double Update(long bytes)
        {
            var dt = _sw.Elapsed.TotalSeconds;
            if (dt >= 0.5)
            {
                var instant = (bytes - _last) / dt;
                _speed = _speed == 0 ? instant : _speed * 0.7 + instant * 0.3;
                _last = bytes;
                _sw.Restart();
            }
            return _speed;
        }
    }
}
