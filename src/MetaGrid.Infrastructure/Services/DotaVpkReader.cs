using System.Text;

namespace MetaGrid.Infrastructure.Services;

/// <summary>Read-only VPK v1/v2 directory reader. Never opens game data for writing.</summary>
public sealed class DotaVpkReader
{
    private sealed record Entry(ushort Archive, uint Offset, uint Length, byte[] Preload);
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _path;
    private readonly long _dataOffset;
    public IEnumerable<string> Paths => _entries.Keys;

    public DotaVpkReader(string path, CancellationToken token = default)
    {
        _path = Path.GetFullPath(path);
        using var stream = File.OpenRead(_path);
        using var reader = new BinaryReader(stream);
        if (reader.ReadUInt32() != 0x55AA1234) throw new InvalidDataException("Invalid VPK signature.");
        var version = reader.ReadUInt32();
        var size = reader.ReadUInt32();
        if (version is not (1 or 2) || size > 64 * 1024 * 1024) throw new InvalidDataException("Unsupported VPK header.");
        if (version == 2) stream.Position += 16;
        _dataOffset = stream.Position + size;
        string ReadString()
        {
            var bytes = new List<byte>();
            byte b;
            while ((b = reader.ReadByte()) != 0)
            {
                if (stream.Position > _dataOffset || bytes.Count > 4096) throw new InvalidDataException("Invalid VPK tree.");
                bytes.Add(b);
            }
            return Encoding.UTF8.GetString(bytes.ToArray());
        }
        for (var extension = ReadString(); extension.Length != 0; extension = ReadString())
            for (var directory = ReadString(); directory.Length != 0; directory = ReadString())
                for (var name = ReadString(); name.Length != 0; name = ReadString())
                {
                    token.ThrowIfCancellationRequested();
                    _ = reader.ReadUInt32();
                    var preload = reader.ReadUInt16();
                    var archive = reader.ReadUInt16();
                    var offset = reader.ReadUInt32();
                    var length = reader.ReadUInt32();
                    if (reader.ReadUInt16() != ushort.MaxValue) throw new InvalidDataException("Invalid VPK entry terminator.");
                    var bytes = reader.ReadBytes(preload);
                    if (bytes.Length != preload) throw new EndOfStreamException();
                    var key = (directory == " " ? "" : directory + "/") + name + (extension == " " ? "" : "." + extension);
                    _entries.Add(key, new Entry(archive, offset, length, bytes));
                }
        if (stream.Position > _dataOffset) throw new InvalidDataException("VPK tree exceeds header bounds.");
    }

    public byte[] Read(string path)
    {
        var entry = _entries[path];
        if (entry.Length > 16 * 1024 * 1024) throw new InvalidDataException("Mapping resource exceeds size limit.");
        var result = new byte[checked(entry.Preload.Length + (int)entry.Length)];
        entry.Preload.CopyTo(result, 0);
        if (entry.Length == 0) return result;
        var archive = entry.Archive == 0x7FFF ? _path
            : _path[..^"_dir.vpk".Length] + $"_{entry.Archive:D3}.vpk";
        using var file = File.OpenRead(archive);
        file.Position = entry.Offset + (entry.Archive == 0x7FFF ? _dataOffset : 0);
        file.ReadExactly(result.AsSpan(entry.Preload.Length));
        return result;
    }

    public string ReadText(string path) => Encoding.UTF8.GetString(Read(path));
}
