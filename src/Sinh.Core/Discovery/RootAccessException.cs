namespace Sinh.Core;

public enum RootFailure { Missing, AccessDenied, NotDirectory, Unavailable }

/// <summary>A root lookup failure is never an empty directory snapshot.</summary>
public sealed class RootAccessException : IOException
{
    public string FolderPath { get; }
    public RootFailure Reason { get; }
    public int? NativeCode { get; }
    public RootAccessException(string path, RootFailure reason, Exception? inner = null)
        : base(Caption(reason) + ": " + path + (inner == null ? "" : " · " + (inner.HResult & 0xffff)), inner)
    {
        FolderPath = path; Reason = reason; NativeCode = inner == null ? null : inner.HResult & 0xffff;
    }
    private static string Caption(RootFailure reason) => reason switch
    {
        RootFailure.Missing => "Папка не найдена",
        RootFailure.AccessDenied => "Нет доступа к папке",
        RootFailure.NotDirectory => "Путь указывает на файл, а не на папку",
        _ => "Не удалось проверить папку"
    };
}
