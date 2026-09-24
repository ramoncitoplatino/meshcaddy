namespace MeshCaddy;

public sealed class FileItem
{
    public required string FullPath { get; init; }
    public bool IsChecked { get; set; }
    public string Name => Path.GetFileName(FullPath);
    public string Extension => Path.GetExtension(FullPath).TrimStart('.').ToUpperInvariant();
    public long Size => new FileInfo(FullPath).Length;
    public DateTime Modified => File.GetLastWriteTime(FullPath);
    public DateTime Created => File.GetCreationTime(FullPath);
    public string Detail
    {
        get
        {
            var info = new FileInfo(FullPath);
            return $"{FormatBytes(info.Length)}  ·  Modified {info.LastWriteTime:g}";
        }
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824d:0.##} GB",
        >= 1_048_576 => $"{bytes / 1_048_576d:0.##} MB",
        >= 1024 => $"{bytes / 1024d:0.##} KB",
        _ => $"{bytes} B"
    };
}
