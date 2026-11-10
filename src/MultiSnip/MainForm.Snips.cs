using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace MultiSnip;

public sealed partial class MainForm
{
    // ------------------------------------------------------------------ snipping

    private async Task SnipAsync(SnipMode mode)
    {
        if (_snipping) return;
        _snipping = true;
        Project target = _project; // snips go to the tab that was active when snipping started
        bool windowWasVisible = Visible && WindowState != FormWindowState.Minimized;
        int added = 0;
        string? note = null;
        try
        {
            if (windowWasVisible) Opacity = 0; // get out of the way of the screenshot
            int delayMs = _settings.DelaySeconds * 1000;
            while (true)
            {
                // Give the window/menus time to disappear (plus the optional user delay).
                await Task.Delay(Math.Max(220, delayMs));
                delayMs = 0;

                Bitmap? result;
                Rectangle area = Rectangle.Empty;
                using (Bitmap screen = SnipOverlayForm.CaptureVirtualScreen(out Rectangle virtualBounds))
                {
                    if (mode == SnipMode.FullScreen)
                    {
                        Rectangle b = Screen.FromPoint(Cursor.Position).Bounds;
                        b.Offset(-virtualBounds.X, -virtualBounds.Y);
                        b.Intersect(new Rectangle(Point.Empty, screen.Size));
                        result = screen.Clone(b, PixelFormat.Format32bppArgb);
                    }
                    else
                    {
                        string hint = mode == SnipMode.Scrolling
                            ? "Scrolling capture: select the area that scrolls (e.g. the web page content) · Esc to cancel"
                            : _settings.KeepSnipping
                                ? $"Snip #{target.Snips.Count + 1}: drag to select · Esc to finish"
                                : "Drag to select · Enter = whole screen · Esc to cancel";
                        using var overlay = new SnipOverlayForm(screen, virtualBounds, hint);
                        overlay.ShowDialog();
                        result = overlay.Result;
                        area = overlay.SelectedScreenArea;
                    }
                }

                if (result == null) break;

                if (mode == SnipMode.Scrolling)
                {
                    result.Dispose(); // only the area was needed; it is captured live while scrolling
                    await Task.Delay(250); // let the overlay disappear
                    var outcome = await ScrollingCapture.RunAsync(area, _settings.ScrollToTopFirst);
                    result = outcome.Image;
                    note = $"Scrolling capture: {outcome.Frames} screen(s) stitched, {result.Width} × {result.Height} px" +
                           (outcome.StoppedByUser ? " (stopped with Esc)" : "") +
                           (outcome.HitLimit ? " (stopped at the maximum length)" : "") +
                           (outcome.UnmatchedSteps > 0 ? $". {outcome.UnmatchedSteps} step(s) could not be aligned exactly; check the result." : ".");
                }

                _pendingNote = note;
                AddSnips(target, new[] { new SnipItem(result, mode.ToString()) });
                added++;
                if (mode != SnipMode.Region || !_settings.KeepSnipping) break;
            }
        }
        catch (Exception ex)
        {
            ShowError("Snipping failed", ex);
        }
        finally
        {
            _snipping = false;
            if (windowWasVisible)
            {
                Opacity = 1;
                Activate();
            }
            else if (added > 0)
            {
                _tray.ShowBalloonTip(2000, "Multi Snip",
                    note ?? $"{added} snip(s) added to \"{target.DisplayName}\" — {target.Snips.Count} in total. Click to open.", ToolTipIcon.Info);
            }
        }
    }

    // ------------------------------------------------------------------ snip list management

    /// <summary>Appends snips to the end of a tab (the "next free slot").</summary>
    private void AddSnips(Project target, IEnumerable<SnipItem> items)
    {
        var list = items.ToList();
        if (list.Count == 0) return;
        target.Checkpoint();
        target.Snips.AddRange(list);
        if (ReferenceEquals(target, _project)) RefreshAll(Enumerable.Range(_project.Snips.Count - list.Count, list.Count));
        UpdateTitle();
    }

    private void AddImages(IEnumerable<Bitmap> images, string source) =>
        AddSnips(_project, images.Select(b => new SnipItem(b, source)));

    private IEnumerable<SnipItem> Selected() =>
        _list.SelectedIndices.Cast<int>().OrderBy(i => i).Where(i => i < _project.Snips.Count).Select(i => _project.Snips[i]);

    private void SelectSnip(int index)
    {
        if (index < 0 || index >= _list.Items.Count) return;
        _list.SelectedIndices.Clear();
        _list.Items[index].Selected = true;
        _list.Items[index].EnsureVisible();
    }

    private void MoveSelected(int delta)
    {
        var selected = _list.SelectedIndices.Cast<int>().OrderBy(i => i).ToList();
        if (selected.Count == 0) return;
        if (delta < 0 && selected[0] == 0) return;
        if (delta > 0 && selected[^1] == _project.Snips.Count - 1) return;
        _project.Checkpoint();
        List<SnipItem> snips = _project.Snips;
        if (delta > 0) selected.Reverse();
        foreach (int i in selected)
            (snips[i], snips[i + delta]) = (snips[i + delta], snips[i]);
        RefreshAll(selected.Select(i => i + delta));
        UpdateTitle();
    }

    private void MoveSelectedTo(int target)
    {
        var selected = _list.SelectedIndices.Cast<int>().OrderBy(i => i).ToList();
        if (selected.Count == 0) return;
        _project.Checkpoint();
        List<SnipItem> snips = _project.Snips;
        var items = selected.Select(i => snips[i]).ToList();
        int insertAt = target - selected.Count(i => i < target);
        foreach (int i in Enumerable.Reverse(selected)) snips.RemoveAt(i);
        insertAt = Math.Clamp(insertAt, 0, snips.Count);
        snips.InsertRange(insertAt, items);
        RefreshAll(Enumerable.Range(insertAt, items.Count));
        UpdateTitle();
    }

    private void MoveSelectedToTab(Project? target)
    {
        var selected = _list.SelectedIndices.Cast<int>().OrderBy(i => i).ToList();
        if (selected.Count == 0) return;
        Project source = _project;
        var items = selected.Select(i => source.Snips[i].Snapshot()).ToList();
        source.Checkpoint();
        foreach (int i in Enumerable.Reverse(selected)) source.Snips.RemoveAt(i);
        if (target == null)
        {
            target = NewTab();
        }
        else
        {
            RefreshAll();
        }
        AddSnips(target, items);
        UpdateTitle();
        SetStatus($"Moved {items.Count} snip(s) to \"{target.DisplayName}\".");
    }

    private void RemoveSelected()
    {
        var selected = _list.SelectedIndices.Cast<int>().OrderByDescending(i => i).ToList();
        if (selected.Count == 0) return;
        _project.Checkpoint();
        foreach (int i in selected) _project.Snips.RemoveAt(i);
        RefreshAll(selectIndex: Math.Min(selected[^1], _project.Snips.Count - 1));
        UpdateTitle();
    }

    /// <summary>Delete key: the selected drawing if there is one, otherwise the selected snips.</summary>
    private void DeleteSelected()
    {
        if (_canvas.HasSelection) _canvas.DeleteSelection();
        else RemoveSelected();
    }

    private void ClearAll()
    {
        if (_project.Snips.Count == 0) return;
        if (MessageBox.Show(this, $"Remove all {_project.Snips.Count} snips from this tab? (You can undo this.)", "Multi Snip",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return;
        _project.Checkpoint();
        _project.Snips.Clear();
        RefreshAll();
        UpdateTitle();
    }

    private void ClearDrawingsOnSelected()
    {
        var targets = Selected().Where(s => s.Annotations.Count > 0).ToList();
        if (targets.Count == 0) { SetStatus("Select snips with drawings in the list first."); return; }
        _project.Checkpoint();
        foreach (SnipItem s in targets)
        {
            s.Annotations.Clear();
            s.InvalidateThumbnail();
        }
        RefreshAll(_list.SelectedIndices.Cast<int>().ToList());
        UpdateTitle();
    }

    private void RenumberBadges()
    {
        var badges = _project.Snips.SelectMany(s => s.Annotations).Where(a => a.IsBadge && a.BadgeNumber != null).ToList();
        if (badges.Count == 0) { SetStatus("There are no numbered labels to renumber."); return; }
        _project.Checkpoint();
        for (int i = 0; i < badges.Count; i++) badges[i].Text = (i + 1).ToString();
        _project.NextBadgeNumber = badges.Count + 1;
        foreach (SnipItem s in _project.Snips) s.InvalidateThumbnail();
        LoadOptionsIntoUi();
        RefreshAll();
        UpdateTitle();
        SetStatus($"Renumbered {badges.Count} label(s) 1–{badges.Count}.");
    }

    /// <summary>Called by the canvas after a drawing was added, changed or removed.</summary>
    private void OnDrawingsChanged()
    {
        _loadingUi = true;
        _nextBadge.Value = Math.Clamp(_project.NextBadgeNumber, 0, 9999);
        _loadingUi = false;
        RefreshThumbnails();
        UpdateTitle();
    }

    private void RefreshAll(int selectIndex) => RefreshAll(selectIndex >= 0 ? new[] { selectIndex } : Array.Empty<int>());

    private void RefreshAll(IEnumerable<int>? select = null)
    {
        List<SnipItem> snips = _project.Snips;
        _list.BeginUpdate();
        _list.Items.Clear();
        _thumbs.Images.Clear();
        for (int i = 0; i < snips.Count; i++)
        {
            snips[i].Thumbnail ??= MakeThumbnail(snips[i], _thumbs.ImageSize);
            _thumbs.Images.Add(snips[i].Thumbnail!);
            _list.Items.Add(new ListViewItem($"{i + 1}.  {snips[i].Image.Width} × {snips[i].Image.Height}", i));
        }
        if (select != null)
        {
            foreach (int i in select)
                if (i >= 0 && i < _list.Items.Count)
                {
                    _list.Items[i].Selected = true;
                    _list.Items[i].EnsureVisible();
                }
        }
        _list.EndUpdate();
        UpdateOptionEnabledState();
        InvalidateCombined();
    }

    /// <summary>Updates thumbnails after drawing, without rebuilding the combined image.</summary>
    private void RefreshThumbnails()
    {
        List<SnipItem> snips = _project.Snips;
        if (_thumbs.Images.Count != snips.Count) { RefreshAll(); return; }
        for (int i = 0; i < snips.Count; i++)
        {
            if (snips[i].Thumbnail != null) continue;
            snips[i].Thumbnail = MakeThumbnail(snips[i], _thumbs.ImageSize);
            _thumbs.Images[i] = snips[i].Thumbnail!;
        }
        _list.Invalidate();
    }

    private static Bitmap MakeThumbnail(SnipItem s, Size box)
    {
        var thumb = new Bitmap(box.Width, box.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(thumb);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        float k = Math.Min(1f, Math.Min((box.Width - 2) / (float)s.Image.Width, (box.Height - 2) / (float)s.Image.Height));
        int w = Math.Max(1, (int)(s.Image.Width * k)), h = Math.Max(1, (int)(s.Image.Height * k));
        var dest = new Rectangle((box.Width - w) / 2, (box.Height - h) / 2, w, h);
        g.DrawImage(s.Image, dest);
        if (s.Annotations.Count > 0)
        {
            GraphicsState state = g.Save();
            g.SetClip(dest);
            g.TranslateTransform(dest.X, dest.Y);
            g.ScaleTransform(k, k);
            AnnotationRenderer.DrawAll(g, s.Annotations);
            g.Restore(state);
        }
        g.DrawRectangle(Pens.Silver, dest.X - 1, dest.Y - 1, dest.Width + 1, dest.Height + 1);
        return thumb;
    }

    /// <summary>One snip with its drawings (for "Copy/Save this snip").</summary>
    private static Bitmap RenderSnip(SnipItem s)
    {
        var bmp = new Bitmap(s.Image.Width, s.Image.Height, PixelFormat.Format32bppArgb);
        using Graphics g = Graphics.FromImage(bmp);
        g.DrawImageUnscaled(s.Image, 0, 0);
        AnnotationRenderer.DrawAll(g, s.Annotations);
        return bmp;
    }

    // ------------------------------------------------------------------ drag & drop

    private void OnListDragOver(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(InternalDragFormat) == true)
        {
            e.Effect = DragDropEffects.Move;
            Point pt = _list.PointToClient(new Point(e.X, e.Y));
            int idx = _list.InsertionMark.NearestIndex(pt);
            if (idx >= 0)
            {
                Rectangle r = _list.GetItemRect(idx);
                _list.InsertionMark.AppearsAfterItem = pt.Y > r.Bottom || (pt.Y >= r.Top && pt.X > r.Left + r.Width / 2);
            }
            _list.InsertionMark.Index = idx;
        }
        else
        {
            OnExternalDragEnter(sender, e);
        }
    }

    private void OnListDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(InternalDragFormat) is int from)
        {
            int mark = _list.InsertionMark.Index;
            bool after = _list.InsertionMark.AppearsAfterItem;
            _list.InsertionMark.Index = -1;
            if (mark < 0) return;
            int target = mark + (after ? 1 : 0);
            if (!_list.SelectedIndices.Contains(from))
            {
                _list.SelectedIndices.Clear();
                _list.SelectedIndices.Add(from);
            }
            MoveSelectedTo(target);
        }
        else
        {
            OnExternalDragDrop(sender, e);
        }
    }

    private void OnExternalDragEnter(object? sender, DragEventArgs e)
    {
        bool ok = e.Data != null && !e.Data.GetDataPresent(InternalDragFormat) &&
                  (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.Bitmap));
        e.Effect = ok ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void OnExternalDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data == null || e.Data.GetDataPresent(InternalDragFormat)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
            OpenPaths(files);
        else if (e.Data.GetData(DataFormats.Bitmap) is Image img)
            AddImages(new[] { ImageUtil.ToArgb(img) }, "Dropped");
    }

    private void StartDragOut()
    {
        if (_project.Snips.Count == 0) { SetStatus("Nothing to drag yet — take a snip first."); return; }
        try
        {
            _canvas.CommitPendingEdits();
            using Bitmap final = _project.RenderFinal();
            string dir = Path.Combine(Path.GetTempPath(), "MultiSnip");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, SuggestFileName(_project.FilePath, FileNames.DefaultBaseName) + ".png");
            final.Save(file, ImageFormat.Png);
            var data = new DataObject();
            data.SetData(DataFormats.FileDrop, new[] { file });
            using Bitmap flat = ImageUtil.Flatten(final, Color.White);
            data.SetData(DataFormats.Bitmap, flat);
            _captureBar.DoDragDrop(data, DragDropEffects.Copy);
        }
        catch (Exception ex)
        {
            ShowError("Could not start drag", ex);
        }
    }

    // ------------------------------------------------------------------ clipboard

    /// <summary>Adds the clipboard image (e.g. from another snipping tool) as the next snip of this tab.</summary>
    private void PasteFromClipboard()
    {
        try
        {
            if (Clipboard.ContainsFileDropList())
            {
                OpenPaths(Clipboard.GetFileDropList().Cast<string>());
            }
            else if (Clipboard.ContainsImage() && Clipboard.GetImage() is Image img)
            {
                using (img) AddImages(new[] { ImageUtil.ToArgb(img) }, "Pasted");
                SetStatus($"Pasted as snip #{_project.Snips.Count}.");
            }
            else
            {
                SetStatus("The clipboard does not contain an image.");
            }
        }
        catch (Exception ex)
        {
            ShowError("Paste failed", ex);
        }
    }

    private List<Bitmap> LoadImageFiles(IEnumerable<string> files)
    {
        var result = new List<Bitmap>();
        var failed = new List<string>();
        foreach (string f in files)
        {
            try
            {
                using var fs = File.OpenRead(f);
                using var img = Image.FromStream(fs);
                result.Add(ImageUtil.ToArgb(img));
            }
            catch
            {
                failed.Add(Path.GetFileName(f));
            }
        }
        if (failed.Count > 0)
            SetStatus("Skipped (not an image): " + string.Join(", ", failed));
        return result;
    }

    private void CopyCombined()
    {
        _canvas.CommitPendingEdits();
        if (_project.Snips.Count == 0) { SetStatus("Nothing to copy yet — take a snip first."); return; }
        using Bitmap c = _project.RenderFinal();
        SetClipboardImage(c);
        SetStatus($"Copied the combined image ({c.Width} × {c.Height}) to the clipboard.");
    }

    private void SetClipboardImage(Bitmap image)
    {
        try
        {
            var data = new DataObject();
            // Plain bitmap for apps like Paint/Word, PNG (keeps transparency) for apps that support it.
            using Bitmap flat = ImageUtil.Flatten(image, Color.White);
            data.SetData(DataFormats.Bitmap, true, flat);
            using var png = new MemoryStream();
            image.Save(png, ImageFormat.Png);
            data.SetData("PNG", false, png);
            Clipboard.SetDataObject(data, true, 5, 100); // copy=true renders all formats now
        }
        catch (Exception ex)
        {
            ShowError("Copy failed", ex);
        }
    }

    // ------------------------------------------------------------------ rendering

    private void InvalidateCombined()
    {
        _renderTimer.Stop();
        _renderTimer.Start();
    }

    private void RenderPreview()
    {
        _renderTimer.Stop();
        Bitmap? old = _base;
        if (_project.Snips.Count > 0)
        {
            _layout = _project.ComputeLayout();
            _base = _project.RenderBase(_layout);
        }
        else
        {
            _layout = null;
            _base = null;
        }
        _canvas.SetDocument(_project, _base, _layout);
        old?.Dispose();
        SyncZoomUi();

        if (_base == null)
        {
            SetStatus(_pendingNote ?? "No snips in this tab yet.");
        }
        else
        {
            int scale = _project.Options.OutputScale;
            string size = scale == 100 ? $"{_base.Width} × {_base.Height} px"
                : $"{_base.Width * scale / 100} × {_base.Height * scale / 100} px ({scale}% of {_base.Width} × {_base.Height})";
            SetStatus($"{_project.Snips.Count} snip(s) · combined image {size}" + (_pendingNote != null ? " · " + _pendingNote : ""));
        }
        _pendingNote = null;
    }

    private void SetStatus(string text) => _status.Text = text;

    private void ShowError(string title, Exception ex) =>
        MessageBox.Show(this, ex.Message, $"Multi Snip — {title}", MessageBoxButtons.OK, MessageBoxIcon.Error);
}
