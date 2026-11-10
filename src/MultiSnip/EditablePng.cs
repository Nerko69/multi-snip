using System.Buffers.Binary;
using System.Text;

namespace MultiSnip;

/// <summary>
/// Stores the Multi Snip project (original snips + annotations) inside a normal PNG file, in a private
/// ancillary chunk. Every image viewer shows the combined picture; Multi Snip can reopen it with all
/// snips and drawings still separate and editable (similar to GIMP's .xcf layers, but one portable file).
/// </summary>
public static class EditablePng
{
    // Chunk type rules: lowercase 1st = ancillary (viewers may skip it), lowercase 2nd = private,
    // uppercase 3rd = reserved bit, lowercase 4th = safe to copy.
    private static readonly byte[] ChunkType = Encoding.ASCII.GetBytes("msNp");
    private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

    /// <summary>Returns <paramref name="png"/> with <paramref name="payload"/> inserted before the IEND chunk.</summary>
    public static byte[] Embed(byte[] png, byte[] payload)
    {
        int iend = FindChunk(png, "IEND") ?? throw new InvalidDataException("Not a valid PNG (no IEND chunk).");
        var chunk = new byte[12 + payload.Length];
        BinaryPrimitives.WriteInt32BigEndian(chunk, payload.Length);
        ChunkType.CopyTo(chunk, 4);
        payload.CopyTo(chunk, 8);
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(8 + payload.Length), Crc32(chunk.AsSpan(4, 4 + payload.Length)));

        var result = new byte[png.Length + chunk.Length];
        png.AsSpan(0, iend).CopyTo(result);
        chunk.CopyTo(result, iend);
        png.AsSpan(iend).CopyTo(result.AsSpan(iend + chunk.Length));
        return result;
    }

    /// <summary>The embedded project data, or null for an ordinary PNG (or any other file).</summary>
    public static byte[]? TryExtract(byte[] file)
    {
        int? pos = FindChunk(file, "msNp");
        if (pos == null) return null;
        int len = BinaryPrimitives.ReadInt32BigEndian(file.AsSpan(pos.Value));
        var data = file.AsSpan(pos.Value + 8, len);
        uint crc = BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(pos.Value + 8 + len));
        if (crc != Crc32(file.AsSpan(pos.Value + 4, 4 + len))) throw new InvalidDataException("The Multi Snip data in this PNG is damaged.");
        return data.ToArray();
    }

    /// <summary>Offset of the first chunk of the given type, or null.</summary>
    private static int? FindChunk(byte[] png, string type)
    {
        if (png.Length < 8 || !png.AsSpan(0, 8).SequenceEqual(Signature)) return null;
        byte[] t = Encoding.ASCII.GetBytes(type);
        int pos = 8;
        while (pos + 12 <= png.Length)
        {
            int len = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(pos));
            if (len < 0 || pos + 12L + len > png.Length) return null; // truncated / corrupt
            if (png.AsSpan(pos + 4, 4).SequenceEqual(t)) return pos;
            pos += 12 + len;
        }
        return null;
    }

    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
    {
        uint c = (uint)n;
        for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        uint c = 0xFFFFFFFFu;
        foreach (byte b in data) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }
}
