namespace Sinh.App;

internal static class AssetUris
{
    internal static Uri Base { get; } = new("pack://application:,,,/SINH;component/");
    internal static Uri For(string path) => new(Base, path);
}
