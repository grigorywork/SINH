using System.Buffers;
using System.Diagnostics;
using System.Security.Cryptography;

namespace Sinh.Core;

internal static class VerifiedCopy
{
    public static async Task CopyAsync(string source, string target, Entry expected, int speedLimitMiB, IProgress<SyncProgress>? progress, string stage, CancellationToken token,Func<Task>? created=null)
    {
        PathRules.RejectLinks(source); PathRules.RejectLinks(target);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, FileOptions.Asynchronous | FileOptions.WriteThrough);
        if(created!=null)await created().ConfigureAwait(false);
        var buffer = ArrayPool<byte>.Shared.Rent(131072); var watch = Stopwatch.StartNew(); long copied = 0; long lastReport = 0;
        try
        {
            int read;
            while ((read = await input.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) > 0)
            {
                token.ThrowIfCancellationRequested();
                await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                hash.AppendData(buffer, 0, read); copied += read;
                if (watch.ElapsedMilliseconds - lastReport > 100) { progress?.Report(new(stage, expected.RelativePath, copied, expected.Length)); lastReport = watch.ElapsedMilliseconds; }
                if (speedLimitMiB > 0)
                {
                    var delay = (long)(copied * 1000d / (speedLimitMiB * 1048576d)) - watch.ElapsedMilliseconds;
                    if (delay > 0) await Task.Delay(TimeSpan.FromMilliseconds(delay), token).ConfigureAwait(false);
                }
            }
            if (copied != expected.Length || Convert.ToHexString(hash.GetHashAndReset()) != expected.Hash) throw new IOException("Содержимое источника изменилось: " + source);
            await output.FlushAsync(token).ConfigureAwait(false); output.Flush(true);
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }
}
