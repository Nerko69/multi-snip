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
        OverlayAndDividerTests();
        WatermarkTests();
        MetadataTests();
        GroupArchiveTests();
    }

    private static void OverlayAndDividerTests()
    {
        var sizes = new[] { new Size(400, 300), new Size(200, 100), new Size(100, 100) };
        var o = new CombineOptions { Layout = CombineLayout.Overlay, Margin = 10 };
        var l = CombinedLayout.Compute(sizes, o, new RectangleF?[] { null, new RectangleF(50, 60, 100, 50), null });
        T.Check("overlay: canvas = background + margin", l.Total == new Size(420, 320), l.Total.ToString());
        T.Check("overlay: background at margin", l.ImageRects[0] == new Rectangle(10, 10, 400, 300));
        T.Check("overlay: placed snip uses its box", l.ImageRects[1] == new Rectangle(60, 70, 100, 50), l.ImageRects[1].ToString());
        RectangleF d = CombinedLayout.DefaultOverlayRect(new Size(400, 300), new Size(1000, 100), 1);
        T.Check("overlay: default place shrinks big snips and keeps them inside", d.Width <= 240.5f && d.X >= 0 && d.Right <= 400.5f, d.ToString());

        var v = new CombineOptions { Layout = CombineLayout.Vertical, Spacing = 0, Divider = true, DividerWidth = 4 };
        var lv = CombinedLayout.Compute(new[] { new Size(100, 50), new Size(100, 50), new Size(100, 50) }, v);
        T.Check("divider: gap widened to the line width", lv.Total == new Size(100, 158), lv.Total.ToString());
        T.Check("divider: one line between each pair", lv.Dividers.Length == 2 && lv.Dividers[0] == new Rectangle(0, 50, 100, 4), lv.Dividers.FirstOrDefault().ToString());
        var g = new CombineOptions { Layout = CombineLayout.Grid, GridColumns = 2, Spacing = 10, Divider = true, DividerWidth = 2 };
        var lg = CombinedLayout.Compute(Enumerable.Repeat(new Size(50, 50), 4).ToArray(), g);
        T.Check("divider: grid has a column and a row line", lg.Dividers.Length == 2 && lg.Dividers[0] == new Rectangle(54, 0, 2, 110) && lg.Dividers[1] == new Rectangle(0, 54, 110, 2),
            string.Join(" ", lg.Dividers));
        T.Check("divider off: no lines", CombinedLayout.Compute(Enumerable.Repeat(new Size(50, 50), 4).ToArray(), new CombineOptions()).Dividers.Length == 0);
    }

    private static void WatermarkTests()
    {
        SizeF s = WatermarkOptions.TargetSize(new Size(1000, 500), new SizeF(200, 50), 25);
        T.Check("watermark size = % of image width", Math.Abs(s.Width - 250) < 0.01 && Math.Abs(s.Height - 62.5) < 0.01, s.ToString());
        var br = WatermarkOptions.Place(new Size(1000, 500), new SizeF(250, 60), WatermarkPosition.BottomRight)[0];
        T.Check("watermark bottom-right stays inside", br.Right <= 1000 && br.Bottom <= 500 && br.X > 700 && br.Y > 400, br.ToString());
        var c = WatermarkOptions.Place(new Size(1000, 500), new SizeF(200, 50), WatermarkPosition.Center)[0];
        T.Check("watermark centred", c == new RectangleF(400, 225, 200, 50), c.ToString());
        T.Check("watermark tiled covers the image", WatermarkOptions.Place(new Size(1000, 500), new SizeF(200, 50), WatermarkPosition.Tiled).Count > 8);
        var o = new CombineOptions { Watermark = new WatermarkOptions { Enabled = true, Text = "Hi", ImagePng = new byte[] { 1, 2 } } };
        var clone = o.Clone();
        clone.Watermark.Text = "changed";
        T.Check("watermark settings are copied, not shared", o.Watermark.Text == "Hi" && clone.Watermark.ImagePng!.SequenceEqual(new byte[] { 1, 2 }));
    }

    private static void MetadataTests()
    {
        // Minimal JPEG structure: SOI, APP0 (JFIF), APP1 (EXIF), COM, DQT, SOS + data, EOI.
        byte[] Seg(byte marker, string payload)
        {
            byte[] p = Encoding.ASCII.GetBytes(payload);
            var seg = new byte[4 + p.Length];
            seg[0] = 0xFF; seg[1] = marker;
            BinaryPrimitives.WriteUInt16BigEndian(seg.AsSpan(2), (ushort)(p.Length + 2));
            p.CopyTo(seg, 4);
            return seg;
        }
        byte[] jpeg = new byte[] { 0xFF, 0xD8 }
            .Concat(Seg(0xE0, "JFIF\0rest")).Concat(Seg(0xE1, "Exif\0\0GPS secret")).Concat(Seg(0xFE, "a comment"))
            .Concat(Seg(0xDB, "quant")).Concat(Seg(0xDA, "scan")).Concat(new byte[] { 1, 2, 3, 0xFF, 0xD9 }).ToArray();
        var blocks = MetadataTools.ListBlocks(jpeg);
        T.Check("jpeg: finds EXIF and comment", blocks.Count == 2 && blocks[0].Name.StartsWith("EXIF"), string.Join("; ", blocks.Select(b => b.Name)));
        byte[] clean = MetadataTools.StripAll(jpeg, out int removed)!;
        T.Check("jpeg: metadata removed losslessly", removed == 2 && MetadataTools.ListBlocks(clean).Count == 0 &&
            clean.Length == jpeg.Length - Seg(0xE1, "Exif\0\0GPS secret").Length - Seg(0xFE, "a comment").Length &&
            clean.AsSpan(clean.Length - 5).SequenceEqual(new byte[] { 1, 2, 3, 0xFF, 0xD9 }) &&
            Encoding.ASCII.GetString(clean).Contains("JFIF") && !Encoding.ASCII.GetString(clean).Contains("GPS"));

        byte[] png = EditablePng.Embed(TinyPng(), new byte[] { 9, 9, 9 });
        T.Check("png: project data listed as metadata", MetadataTools.ListBlocks(png).Any(b => b.Name.StartsWith("Multi Snip")));
        byte[] cleanPng = MetadataTools.StripAll(png, out int r2)!;
        T.Check("png: stripped back to the plain image", r2 == 1 && cleanPng.SequenceEqual(TinyPng()));
        T.Check("other formats are not touched", MetadataTools.StripAll(new byte[] { 1, 2, 3 }, out _) == null);
    }

    private static void GroupArchiveTests()
    {
        var doc = new ProjectDocument
        {
            Snips =
            {
                new SnipRecord
                {
                    Png = new byte[] { 1 },
                    GroupOptions = new CombineOptions { Layout = CombineLayout.Vertical },
                    OverlayBox = new[] { 1f, 2f, 30f, 40f },
                },
            },
        };
        doc.Snips[0].Children = new List<SnipRecord>
        {
            new() { Png = new byte[] { 2 }, Annotations = { new Annotation { Kind = AnnotationKind.Text, Text = "inner" } } },
            new() { Png = new byte[] { 3 } },
        };
        var back = ProjectArchive.Read(ProjectArchive.Write(doc));
        SnipRecord g = back.Snips[0];
        T.Check("group: children and their images survive", g.Children!.Count == 2 && g.Children[0].Png.SequenceEqual(new byte[] { 2 }) &&
                                                           g.Children[1].Png.SequenceEqual(new byte[] { 3 }) && g.Children[0].Annotations[0].Text == "inner");
        T.Check("group: options and overlay box survive", g.GroupOptions!.Layout == CombineLayout.Vertical && g.OverlayBox!.SequenceEqual(new[] { 1f, 2f, 30f, 40f }));
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
