using System.Buffers.Binary;
using System.Text;

namespace MultiSnip;

/// <summary>
/// Lossless metadata removal: drops the metadata blocks from JPEG and PNG files without re-compressing the picture.
/// </summary>
public static class MetadataTools
{
    public sealed record Block(string Name, int Bytes, string Preview);

    public static bool IsJpeg(byte[] f) => f.Length > 3 && f[0] == 0xFF && f[1] == 0xD8;
    public static bool IsPng(byte[] f) => f.Length > 8 && f[0] == 137 && f[1] == 80 && f[2] == 78 && f[3] == 71;

    // PNG chunks that only carry metadata (text, EXIF, time, Multi Snip project data).
    private static readonly string[] PngMetaChunks = { "tEXt", "zTXt", "iTXt", "eXIf", "tIME", "msNp" };

    /// <summary>Metadata blocks found in a JPEG or PNG (empty for other files).</summary>
    public static List<Block> ListBlocks(byte[] f)
    {
        var list = new List<Block>();
        if (IsJpeg(f))
        {
            foreach (var (marker, start, len) in JpegSegments(f))
                if (IsJpegMeta(f, marker, start, len))
                    list.Add(new Block(JpegName(f, marker, start, len), len, Printable(f, start + 4, Math.Min(len - 2, 60))));
        }
        else if (IsPng(f))
        {
            foreach (var (type, start, len) in PngChunks(f))
                if (PngMetaChunks.Contains(type))
                    list.Add(new Block(type == "msNp" ? "Multi Snip project data (editable snips)" : $"PNG {type}", len,
                        type == "msNp" ? "" : Printable(f, start + 8, Math.Min(len, 60))));
        }
        return list;
    }

    /// <summary>The file without any metadata blocks, or null if the format isn't JPEG/PNG.</summary>
    public static byte[]? StripAll(byte[] f, out int removed)
    {
        removed = 0;
        if (IsJpeg(f))
        {
            using var ms = new MemoryStream();
            ms.Write(f, 0, 2); // SOI
            int pos = 2;
            foreach (var (marker, start, len) in JpegSegments(f))
            {
                if (IsJpegMeta(f, marker, start, len)) removed++;
                else ms.Write(f, start, len + 2);
                pos = start + len + 2;
                if (marker == 0xDA) break; // start of scan: the rest is image data
            }
            ms.Write(f, pos, f.Length - pos);
            return ms.ToArray();
        }
        if (IsPng(f))
        {
            using var ms = new MemoryStream();
            ms.Write(f, 0, 8);
            foreach (var (type, start, len) in PngChunks(f))
            {
                if (PngMetaChunks.Contains(type)) removed++;
                else ms.Write(f, start, len + 12);
            }
            return ms.ToArray();
        }
        return null;
    }

    // JPEG: APP1 (EXIF / XMP), APP13 (Photoshop/IPTC), other APPn except APP0 (JFIF), APP2 (ICC colour profile) and
    // APP14 (Adobe colour transform), which are needed to show the colours correctly; COM (comments).
    private static bool IsJpegMeta(byte[] f, int marker, int start, int len) =>
        marker == 0xFE || (marker is >= 0xE1 and <= 0xEF && marker != 0xEE && !(marker == 0xE2 && StartsWith(f, start + 4, "ICC_PROFILE")));

    private static string JpegName(byte[] f, int marker, int start, int len) => marker switch
    {
        0xFE => "JPEG comment",
        0xE1 when StartsWith(f, start + 4, "Exif") => "EXIF (camera, date, GPS location, software…)",
        0xE1 when StartsWith(f, start + 4, "http://ns.adobe.com/xap") => "XMP (editing history, author, rights…)",
        0xED => "IPTC / Photoshop (captions, keywords, author…)",
        _ => $"JPEG APP{marker - 0xE0} block",
    };

    private static IEnumerable<(int Marker, int Start, int Len)> JpegSegments(byte[] f)
    {
        int pos = 2;
        while (pos + 4 <= f.Length && f[pos] == 0xFF)
        {
            int marker = f[pos + 1];
            if (marker == 0xD8 || marker is >= 0xD0 and <= 0xD7 || marker == 0x01) { pos += 2; continue; }
            int len = BinaryPrimitives.ReadUInt16BigEndian(f.AsSpan(pos + 2));
            if (len < 2 || pos + 2 + len > f.Length) yield break;
            yield return (marker, pos, len);
            if (marker == 0xDA) yield break;
            pos += 2 + len;
        }
    }

    private static IEnumerable<(string Type, int Start, int Len)> PngChunks(byte[] f)
    {
        int pos = 8;
        while (pos + 12 <= f.Length)
        {
            int len = BinaryPrimitives.ReadInt32BigEndian(f.AsSpan(pos));
            if (len < 0 || pos + 12L + len > f.Length) yield break;
            yield return (Encoding.ASCII.GetString(f, pos + 4, 4), pos, len);
            pos += 12 + len;
        }
    }

    private static bool StartsWith(byte[] f, int at, string s) =>
        at + s.Length <= f.Length && Encoding.ASCII.GetString(f, at, s.Length) == s;

    private static string Printable(byte[] f, int at, int count)
    {
        if (count <= 0 || at >= f.Length) return "";
        count = Math.Min(count, f.Length - at);
        var sb = new StringBuilder();
        for (int i = 0; i < count; i++)
        {
            char c = (char)f[at + i];
            sb.Append(c >= 32 && c < 127 ? c : c == 0 ? ' ' : '·');
        }
        return sb.ToString().Trim();
    }
}
