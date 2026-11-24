using System.Text.Json;

namespace Sinh.Core;

public sealed class StateStore(string directory)
{
    public string DirectoryPath { get; } = Path.GetFullPath(directory);
    internal static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public async Task<ConfirmedState?> LoadAsync(Guid pairId, CancellationToken token = default)
    {
        var path = StatePath(pairId);
        if (!File.Exists(path)) return null;
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<ConfirmedState>(stream, Json, token).ConfigureAwait(false);
        }
        catch (JsonException ex) { throw new IOException("База сравнения повреждена. Автоматическая синхронизация остановлена: " + path, ex); }
    }
    public Task SaveAsync(Guid pairId, ConfirmedState state, CancellationToken token = default) => AtomicJsonAsync(StatePath(pairId), state, token);
    private string StatePath(Guid id) => Path.Combine(DirectoryPath, "state", id.ToString("N") + ".json");
    internal static async Task AtomicJsonAsync<T>(string path, T value, CancellationToken token = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.WriteThrough))
            { await JsonSerializer.SerializeAsync(stream, value, Json, token).ConfigureAwait(false); await stream.FlushAsync(token).ConfigureAwait(false); stream.Flush(true); }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
