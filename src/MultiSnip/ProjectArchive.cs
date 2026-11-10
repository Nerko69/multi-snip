using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MultiSnip;

/// <summary>One snip as stored in a project file.</summary>
public sealed class SnipRecord
{
    public string File { get; set; } = "";
    public string Source { get; set; } = "";
    public List<Annotation> Annotations { get; set; } = new();

    /// <summary>The original snip as PNG bytes (stored as a separate entry in the archive).</summary>
    [JsonIgnore]
    public byte[] Png { get; set; } = Array.Empty<byte>();
}

/// <summary>Everything needed to reopen a tab exactly as it was: original snips, drawings and options.</summary>
public sealed class ProjectDocument
{
    public int FormatVersion { get; set; } = 1;
    public string AppVersion { get; set; } = "";
    public CombineOptions Options { get; set; } = new();
    public List<SnipRecord> Snips { get; set; } = new();
    public int NextBadgeNumber { get; set; } = 1;

    // Only used when tabs are restored after a restart.
    public string? Title { get; set; }
    public string? FilePath { get; set; }
    public bool Dirty { get; set; }
}

/// <summary>Reads/writes a <see cref="ProjectDocument"/> as a zip: project.json + one PNG per snip.</summary>
public static class ProjectArchive
{
    private const string ManifestName = "project.json";

    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new ColorJsonConverter(), new JsonStringEnumConverter() },
    };

    public static byte[] Write(ProjectDocument doc)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            for (int i = 0; i < doc.Snips.Count; i++)
            {
                SnipRecord s = doc.Snips[i];
                s.File = $"snips/{i + 1:0000}.png";
                // PNG data is already compressed; storing avoids wasting time on it.
                using Stream e = zip.CreateEntry(s.File, CompressionLevel.NoCompression).Open();
                e.Write(s.Png);
            }
            using Stream m = zip.CreateEntry(ManifestName, CompressionLevel.Optimal).Open();
            JsonSerializer.Serialize(m, doc, Json);
        }
        return ms.ToArray();
    }

    public static ProjectDocument Read(byte[] data)
    {
        using var zip = new ZipArchive(new MemoryStream(data), ZipArchiveMode.Read);
        ZipArchiveEntry manifest = zip.GetEntry(ManifestName) ?? throw new InvalidDataException("Project data is missing project.json.");
        ProjectDocument doc;
        using (Stream m = manifest.Open())
            doc = JsonSerializer.Deserialize<ProjectDocument>(m, Json) ?? throw new InvalidDataException("Project data is empty.");
        if (doc.FormatVersion > 1)
            throw new InvalidDataException("This file was saved by a newer version of Multi Snip. Please update to open it.");
        foreach (SnipRecord s in doc.Snips)
        {
            ZipArchiveEntry entry = zip.GetEntry(s.File) ?? throw new InvalidDataException($"Project data is missing {s.File}.");
            using Stream e = entry.Open();
            using var buf = new MemoryStream();
            e.CopyTo(buf);
            s.Png = buf.ToArray();
        }
        return doc;
    }

    /// <summary>Stores colors as "#AARRGGBB" so project files stay readable.</summary>
    private sealed class ColorJsonConverter : JsonConverter<Color>
    {
        public override Color Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string s = reader.GetString() ?? "#FF000000";
            return Color.FromArgb(unchecked((int)Convert.ToUInt32(s.TrimStart('#'), 16)));
        }

        public override void Write(Utf8JsonWriter writer, Color value, JsonSerializerOptions options) =>
            writer.WriteStringValue($"#{value.ToArgb():X8}");
    }
}
