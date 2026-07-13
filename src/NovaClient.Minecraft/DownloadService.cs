using System.Net;
using NovaClient.Core.Http;
using NovaClient.Core.Logging;
using NovaClient.Core.Util;

namespace NovaClient.Minecraft;

public sealed record DownloadItem(string Url, string Destination, string? Sha1, long Size);

public sealed class DownloadProgress
{
    public string CurrentFile { get; init; } = "";
    public int FilesDone { get; init; }
    public int FilesTotal { get; init; }
    public long BytesDone { get; init; }
    public long BytesTotal { get; init; }
    public double BytesPerSecond { get; init; }
}

/// <summary>
/// Parallel file downloader: skips files whose SHA-1 already matches, resumes interrupted
/// downloads from .part files (HTTP Range), verifies hashes, and re-downloads corrupt files.
/// </summary>
public sealed class DownloadService
{
    private const int MaxParallel = 8;
    private const int MaxAttempts = 3;

    public async Task DownloadAllAsync(IReadOnlyList<DownloadItem> items, IProgress<DownloadProgress>? progress, CancellationToken ct = default)
    {
        long bytesTotal = items.Sum(i => i.Size);
        long bytesDone = 0;
        var filesDone = 0;
        var started = DateTime.UtcNow;
        var lastReport = DateTime.MinValue;
        var reportLock = new object();

        void Report(string file, bool force = false)
        {
            lock (reportLock)
            {
                var now = DateTime.UtcNow;
                if (!force && (now - lastReport).TotalMilliseconds < 100) return;
                lastReport = now;
                var elapsed = Math.Max(0.25, (now - started).TotalSeconds);
                progress?.Report(new DownloadProgress
                {
                    CurrentFile = file,
                    FilesDone = filesDone,
                    FilesTotal = items.Count,
                    BytesDone = Interlocked.Read(ref bytesDone),
                    BytesTotal = bytesTotal,
                    BytesPerSecond = Interlocked.Read(ref bytesDone) / elapsed
                });
            }
        }

        using var semaphore = new SemaphoreSlim(MaxParallel);
        var tasks = items.Select(async item =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                var skippedBytes = await DownloadOneAsync(item,
                    reported => { Interlocked.Add(ref bytesDone, reported); Report(Path.GetFileName(item.Destination)); }, ct);
                if (skippedBytes > 0) Interlocked.Add(ref bytesDone, skippedBytes);
                Interlocked.Increment(ref filesDone);
                Report(Path.GetFileName(item.Destination), force: true);
            }
            finally
            {
                semaphore.Release();
            }
        }).ToList();

        await Task.WhenAll(tasks);
        Report("", force: true);
    }

    /// <summary>Returns item.Size when the file was already valid and skipped, otherwise 0.</summary>
    private static async Task<long> DownloadOneAsync(DownloadItem item, Action<long> onBytes, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(item.Destination)!);

        if (File.Exists(item.Destination) && await IsValidAsync(item, item.Destination, ct))
            return item.Size;

        var partPath = item.Destination + ".part";
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                await FetchAsync(item, partPath, onBytes, resume: attempt == 1, ct);
                if (await IsValidAsync(item, partPath, ct))
                {
                    File.Move(partPath, item.Destination, overwrite: true);
                    return 0;
                }
                NovaLog.Warn("Download", $"Hash mismatch for {Path.GetFileName(item.Destination)} (attempt {attempt}); redownloading.");
                File.Delete(partPath);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException && attempt < MaxAttempts)
            {
                NovaLog.Warn("Download", $"Attempt {attempt} failed for {item.Url}: {ex.Message}");
                await Task.Delay(500 * attempt, ct);
            }
        }
        throw new IOException($"Failed to download {item.Url} after {MaxAttempts} attempts.");
    }

    private static async Task FetchAsync(DownloadItem item, string partPath, Action<long> onBytes, bool resume, CancellationToken ct)
    {
        long existing = 0;
        if (resume && File.Exists(partPath))
            existing = new FileInfo(partPath).Length;
        else if (File.Exists(partPath))
            File.Delete(partPath);

        using var request = new HttpRequestMessage(HttpMethod.Get, item.Url);
        if (existing > 0) request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existing, null);

        using var response = await HttpProvider.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (existing > 0 && response.StatusCode != HttpStatusCode.PartialContent)
        {
            // Server ignored the range request — start over.
            existing = 0;
            File.Delete(partPath);
        }
        response.EnsureSuccessStatusCode();

        await using var target = new FileStream(partPath, existing > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write);
        await using var source = await response.Content.ReadAsStreamAsync(ct);
        var buffer = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), ct);
            onBytes(read);
        }
    }

    private static async Task<bool> IsValidAsync(DownloadItem item, string path, CancellationToken ct)
    {
        if (item.Sha1 is null)
            return item.Size <= 0 || new FileInfo(path).Length == item.Size;
        return string.Equals(await HashUtil.Sha1FileAsync(path, ct), item.Sha1, StringComparison.OrdinalIgnoreCase);
    }
}
