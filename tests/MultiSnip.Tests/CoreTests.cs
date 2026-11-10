using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace MultiSnip.Tests;

internal static class CoreTests
{
    public static void Run()
    {
        FileNameTests();
        LayoutTests();
        EditablePngTests();
        ArchiveTests();
    }

    private static void FileNameTests()
    {
        var t = new DateTime(2026, 1, 11, 18, 20, 0);
        T.Check("timestamp suffix format", FileNames.TimestampSuffix(t) == "_2026-01_11-18-20-00", FileNames.TimestampSuffix(t));
        T.Check("timestamp added to base name", FileNames.WithTimestamp("Report", t) == "Report_2026-01_11-18-20-00");
        T.Check("old timestamp replaced, not stacked",
            FileNames.WithTimestamp("Report_2025-12_01-09-05-07", t) == "Report_2026-01_11-18-20-00");
        T.Check("empty name falls back to MultiSnip", FileNames.WithTimestamp("  ", t) == "MultiSnip_2026-01_11-18-20-00");

        var recent = new List<string> { "a", "b", "c" };
        FileNames.PushRecent(recent, "B", max: 3);
        T.Check("recent list moves item to front (case-insensitive)", recent.SequenceEqual(new[] { "B", "a", "c" }));
        FileNames.PushRecent(recent, "d", max: 3);
        T.Check("recent list is capped", recent.SequenceEqual(new[] { "d", "B", "a" }));
    }

    private static void LayoutTests()
    {
        var sizes = Enumerable.Range(0, 10).Select(_ => new Size(100, 50)).ToArray();
        var o = new CombineOptions { Layout = CombineLayout.Grid, GridColumns = 0, Spacing = 10, Margin = 0 };
        T.Check("auto grid: 10 snips -> 4 columns", o.EffectiveColumns(10) == 4);
        T.Check("auto grid: 9 snips -> 3 columns", o.EffectiveColumns(9) == 3);
        T.Check("auto grid: 1 snip -> 1 column", o.EffectiveColumns(1) == 1);
        var layout = CombinedLayout.Compute(sizes, o);
        T.Check("auto grid size (4 x 3)", layout.Total == new Size(4 * 100 + 3 * 10, 3 * 50 + 2 * 10), layout.Total.ToString());
        T.Check("auto grid: 5th snip starts row 2", layout.ImageRects[4].Location == new Point(0, 60));

        o.GridColumns = 6;
        T.Check("fixed grid with more columns than before", CombinedLayout.Compute(sizes, o).Total == new Size(6 * 100 + 5 * 10, 2 * 50 + 10));

        var v = new CombineOptions { Layout = CombineLayout.Vertical, Spacing = 5, Margin = 3, Border = true, BorderWidth = 2 };
        var lv = CombinedLayout.Compute(new[] { new Size(10, 10), new Size(20, 5) }, v);
        T.Check("vertical layout with border/margin", lv.Total == new Size(3 + 24 + 3, 3 + 14 + 5 + 9 + 3) && lv.ImageRects[1] == new Rectangle(5, 24, 20, 5),
            $"{lv.Total} {lv.ImageRects[1]}");
    }

    private static void EditablePngTests()
    {
        byte[] png = TinyPng();
        byte[] payload = Encoding.UTF8.GetBytes("hello project");
        byte[] withData = EditablePng.Embed(png, payload);
        T.Check("embedded data reads back", EditablePng.TryExtract(withData) is { } back && back.SequenceEqual(payload));
        T.Check("plain PNG has no project data", EditablePng.TryExtract(png) == null);
        T.Check("non-PNG has no project data", EditablePng.TryExtract(Encoding.ASCII.GetBytes("not a png")) == null);
        T.Check("file still ends with IEND", Encoding.ASCII.GetString(withData, withData.Length - 8, 4) == "IEND");

        withData[^20] ^= 0xFF; // corrupt the payload's CRC area
        bool threw = false;
        try { EditablePng.TryExtract(withData); } catch (InvalidDataException) { threw = true; }
        T.Check("damaged project data is detected", threw);
    }

    private static void ArchiveTests()
    {
        var doc = new ProjectDocument
        {
            AppVersion = "1.3.0",
            Options = new CombineOptions { Layout = CombineLayout.Grid, GridColumns = 5, Background = Color.FromArgb(255, 10, 20, 30), OutputScale = 50 },
            NextBadgeNumber = 4,
            Snips =
            {
                new SnipRecord
                {
                    Source = "Snip", Png = new byte[] { 1, 2, 3 },
                    Annotations =
                    {
                        new Annotation { Kind = AnnotationKind.Pen, Points = { new PointF(1.5f, 2), new PointF(3, 4) }, Argb = unchecked((int)0xFF00FF00), Width = 4 },
                        new Annotation { Kind = AnnotationKind.BadgeCircle, Points = { new PointF(10, 10) }, Text = "3", FontSize = 24 },
                    },
                },
                new SnipRecord { Source = "Pasted", Png = new byte[] { 9, 8 } },
            },
        };
        ProjectDocument back = ProjectArchive.Read(ProjectArchive.Write(doc));
        T.Check("archive: snips and image bytes", back.Snips.Count == 2 && back.Snips[0].Png.SequenceEqual(new byte[] { 1, 2, 3 }) && back.Snips[1].Png.SequenceEqual(new byte[] { 9, 8 }));
        T.Check("archive: options", back.Options.Layout == CombineLayout.Grid && back.Options.GridColumns == 5 &&
                                    back.Options.Background.ToArgb() == doc.Options.Background.ToArgb() && back.Options.OutputScale == 50);
        Annotation pen = back.Snips[0].Annotations[0], badge = back.Snips[0].Annotations[1];
        T.Check("archive: pen stroke", pen.Kind == AnnotationKind.Pen && pen.Points.SequenceEqual(new[] { new PointF(1.5f, 2), new PointF(3, 4) }) && pen.Argb == unchecked((int)0xFF00FF00) && pen.Width == 4);
        T.Check("archive: numbered badge", badge.Kind == AnnotationKind.BadgeCircle && badge.BadgeNumber == 3 && badge.FontSize == 24);
        T.Check("archive: next badge number", back.NextBadgeNumber == 4);

        // Whole round trip through a PNG file.
        byte[] file = EditablePng.Embed(TinyPng(), ProjectArchive.Write(doc));
        T.Check("project survives embedding in a PNG", ProjectArchive.Read(EditablePng.TryExtract(file)!).Snips.Count == 2);
    }

    /// <summary>A valid 1x1 RGBA PNG, built by hand.</summary>
    private static byte[] TinyPng()
    {
        var ms = new MemoryStream();
        ms.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        void Chunk(string type, byte[] data)
        {
            var len = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
            ms.Write(len);
            byte[] td = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
            ms.Write(td);
            var crc = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(crc, Crc(td));
            ms.Write(crc);
        }
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, 1);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), 1);
        ihdr[8] = 8; ihdr[9] = 6; // 8-bit RGBA
        Chunk("IHDR", ihdr);
        var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true)) z.Write(new byte[] { 0, 255, 0, 0, 255 });
        Chunk("IDAT", raw.ToArray());
        Chunk("IEND", Array.Empty<byte>());
        return ms.ToArray();
    }

    private static uint Crc(byte[] data)
    {
        uint c = 0xFFFFFFFFu;
        foreach (byte b in data)
        {
            c ^= b;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        }
        return c ^ 0xFFFFFFFFu;
    }
}
