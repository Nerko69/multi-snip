namespace MultiSnip;

public sealed partial class MainForm
{
    /// <summary>
    /// Used by <c>--self-test</c>: drives the real window through views, layouts, groups, crop, watermark and text
    /// sizes. Throws on the first problem; returns what was exercised.
    /// </summary>
    internal IEnumerable<string> SelfTestExercise(Func<int, int, Color, Bitmap> solid)
    {
        var done = new List<string>();
        Project p = NewTab();
        AddSnips(p, new[]
        {
            new SnipItem(solid(300, 200, Color.LightGray), "test"),
            new SnipItem(solid(200, 120, Color.LightBlue), "test"),
            new SnipItem(solid(150, 150, Color.LightGreen), "test"),
        });
        RenderPreview();
        if (_list.Items.Count != 4) throw new Exception($"list should show All + 3 snips, has {_list.Items.Count}");
        done.Add("window: snips added, list shows \"All snips\" + 3");

        ShowView(1);
        if (_base == null || _base.Size != new Size(200, 120)) throw new Exception("single-snip view shows the wrong size");
        ShowView(-1);
        done.Add("window: single-snip view and back to all");

        foreach (CombineLayout layout in Enum.GetValues<CombineLayout>())
        {
            _project.Options.Layout = layout;
            LoadOptionsIntoUi();
            RenderPreview();
            _canvas.Refresh();
        }
        _project.Options.Layout = CombineLayout.Vertical;
        _project.Options.Divider = true;
        LoadOptionsIntoUi();
        RenderPreview();
        done.Add("window: every layout (incl. overlay) and dividers render");

        _list.SelectedIndices.Clear();
        GroupSnips();
        if (p.Snips.Count != 1 || !p.Snips[0].IsGroup) throw new Exception("Keep as one did not group");
        _list.SelectedIndices.Add(1);
        UngroupSnips();
        if (p.Snips.Count != 3) throw new Exception("Ungroup did not restore the snips");
        done.Add("window: keep as one + ungroup");

        p.Options.Watermark = new WatermarkOptions { Enabled = true, Text = "Self test" };
        RenderPreview();
        _canvas.Refresh();
        done.Add("window: watermark preview");

        for (int size = 0; size < 3; size++)
        {
            ApplyTextSize(size);
            _canvas.Refresh();
            Refresh();
        }
        ApplyTextSize(1);
        done.Add("window: all text sizes lay out");

        p.Dirty = false; // closing a modified tab would ask to save
        CloseTab(p);
        return done;
    }
}
