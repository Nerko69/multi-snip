using System.Diagnostics;
using System.Drawing.Imaging;
using System.Text.Json;

namespace MultiSnip;

public sealed partial class MainForm
{
    // ------------------------------------------------------------------ tabs

    private Project NewTab(Project? project = null)
    {
        project ??= new Project(_settings.ToCombineOptions());
        _projects.Add(project);
        _tabs.TabPages.Add(new TabPage(project.DisplayName));
        FitTabStrip();
        ActivateProject(project);
        return project;
    }

    /// <summary>Only the tab headers are shown (the content area is shared), so size the strip to them.</summary>
    private void FitTabStrip()
    {
        if (_tabs.TabCount > 0 && _tabs.IsHandleCreated) _tabs.Height = _tabs.GetTabRect(0).Bottom + 2;
        else _tabs.Height = _tabs.Font.Height + 14;
    }

    private void ActivateProject(Project p)
    {
        _canvas.CommitPendingEdits();
        _project = p;
        int index = _projects.IndexOf(p);
        if (_tabs.SelectedIndex != index) _tabs.SelectedIndex = index;
        LoadOptionsIntoUi();
        RefreshAll();
        RenderPreview();
        UpdateTitle();
    }

    private void SwitchTab(int delta)
    {
        if (_projects.Count < 2) return;
        int i = (_projects.IndexOf(_project) + delta + _projects.Count) % _projects.Count;
        ActivateProject(_projects[i]);
    }

    /// <summary>Closes a tab, asking to save unsaved work. Returns false if the user cancelled.</summary>
    private bool CloseTab(Project p)
    {
        if (p.Dirty && p.Snips.Count > 0)
        {
            ActivateProject(p);
            DialogResult r = MessageBox.Show(this, $"Save changes to \"{p.DisplayName}\" before closing the tab?", "Multi Snip",
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Cancel) return false;
            if (r == DialogResult.Yes && !Save()) return false;
        }
        int index = _projects.IndexOf(p);
        _projects.RemoveAt(index);
        _tabs.TabPages.RemoveAt(index);
        if (_projects.Count == 0) NewTab();
        else if (ReferenceEquals(p, _project)) ActivateProject(_projects[Math.Min(index, _projects.Count - 1)]);
        return true;
    }

    /// <summary>Updates the tab captions and the window title (full path of the saved file).</summary>
    private void UpdateTitle()
    {
        for (int i = 0; i < _projects.Count && i < _tabs.TabCount; i++)
        {
            Project p = _projects[i];
            string text = p.DisplayName + (p.Dirty && p.Snips.Count > 0 ? " *" : "");
            if (_tabs.TabPages[i].Text != text) _tabs.TabPages[i].Text = text;
            _tabs.TabPages[i].ToolTipText = p.FilePath ?? "Not saved yet";
        }
        string where = _project.FilePath ?? $"{_project.UntitledName} (not saved yet)";
        Text = $"{where}{(_project.Dirty && _project.Snips.Count > 0 ? " *" : "")} — Multi Snip v{AppInfo.Version}";
        UpdateUndoState();
    }

    private void UpdateUndoState()
    {
        _undoButton.Enabled = _undoMenu.Enabled = _project.CanUndo;
        _redoButton.Enabled = _redoMenu.Enabled = _project.CanRedo;
    }

    private void Undo()
    {
        _canvas.CommitPendingEdits();
        if (!_project.Undo()) return;
        LoadOptionsIntoUi();
        RefreshAll();
        UpdateTitle();
    }

    private void Redo()
    {
        if (!_project.Redo()) return;
        LoadOptionsIntoUi();
        RefreshAll();
        UpdateTitle();
    }

    // ------------------------------------------------------------------ combine options (per tab)

    private void LoadOptionsIntoUi()
    {
        CombineOptions o = _project.Options;
        _loadingUi = true;
        _vertical.Checked = o.Layout == CombineLayout.Vertical;
        _horizontal.Checked = o.Layout == CombineLayout.Horizontal;
        _grid.Checked = o.Layout == CombineLayout.Grid;
        int col = Array.IndexOf(GridColumnChoices, o.GridColumns);
        if (o.GridColumns > 0 && col < 0)
        {
            _columns.Items.Add($"{o.GridColumns} columns"); // value from an older/hand-edited file
            col = _columns.Items.Count - 2;
        }
        _columns.SelectedIndex = o.GridColumns <= 0 ? 0 : col + 1;
        UpdateAlignLabels(o.Layout);
        _align.SelectedIndex = (int)o.Alignment;
        _spacing.Value = Math.Clamp(o.Spacing, 0, 500);
        _margin.Value = Math.Clamp(o.Margin, 0, 500);
        _matchSize.Checked = o.MatchSize;
        _transparent.Checked = o.TransparentBackground;
        _border.Checked = o.Border;
        _borderWidth.Value = Math.Clamp(o.BorderWidth, 1, 50);
        _bgColor.BackColor = Color.FromArgb(255, o.Background);
        _borderColor.BackColor = Color.FromArgb(255, o.BorderColor);
        int scale = Array.IndexOf(OutputScales, o.OutputScale);
        _outputScale.SelectedIndex = scale < 0 ? 0 : scale;
        _nextBadge.Value = Math.Clamp(_project.NextBadgeNumber, 0, 9999);
        _loadingUi = false;
        UpdateOptionEnabledState();
    }

    private void OnOptionChanged(object? sender, EventArgs e)
    {
        if (_loadingUi) return;
        if (sender is RadioButton { Checked: false }) return; // handle only the newly checked one
        _project.Checkpoint();
        CombineOptions o = _project.Options;
        o.Layout = _grid.Checked ? CombineLayout.Grid : _horizontal.Checked ? CombineLayout.Horizontal : CombineLayout.Vertical;
        o.GridColumns = _columns.SelectedIndex <= 0 ? 0 : int.Parse(((string)_columns.SelectedItem!).Split(' ')[0]);
        o.Alignment = (CombineAlignment)Math.Max(0, _align.SelectedIndex);
        o.Spacing = (int)_spacing.Value;
        o.Margin = (int)_margin.Value;
        o.MatchSize = _matchSize.Checked;
        o.TransparentBackground = _transparent.Checked;
        o.Border = _border.Checked;
        o.BorderWidth = (int)_borderWidth.Value;
        o.OutputScale = OutputScales[Math.Max(0, _outputScale.SelectedIndex)];
        _settings.RememberOptions(o);
        if (sender is RadioButton) UpdateAlignLabels(o.Layout);
        UpdateOptionEnabledState();
        UpdateTitle();
        InvalidateCombined();
    }

    private void UpdateAlignLabels(CombineLayout layout)
    {
        string[] labels = layout switch
        {
            CombineLayout.Vertical => new[] { "Left", "Center", "Right" },
            CombineLayout.Horizontal => new[] { "Top", "Middle", "Bottom" },
            _ => new[] { "Top-left", "Center", "Bottom-right" },
        };
        bool wasLoading = _loadingUi;
        _loadingUi = true;
        int idx = _align.SelectedIndex;
        _align.Items.Clear();
        _align.Items.AddRange(labels);
        _align.SelectedIndex = idx < 0 ? 0 : idx;
        _loadingUi = wasLoading;
    }

    private void UpdateOptionEnabledState()
    {
        _columns.Enabled = _grid.Checked;
        _gridInfo.Visible = _grid.Checked;
        int n = _project.Snips.Count;
        int cols = _project.Options.EffectiveColumns(n);
        _gridInfo.Text = n == 0 ? "" : $"= {cols} × {(n + cols - 1) / cols}";
        _bgColor.Enabled = !_transparent.Checked;
        _borderColor.Enabled = _borderWidth.Enabled = _border.Checked;
    }

    private void PickOptionColor(Button swatch, Action<CombineOptions, Color> apply)
    {
        using var dlg = new ColorDialog { Color = swatch.BackColor, FullOpen = true, CustomColors = _settings.CustomColors ?? Array.Empty<int>() };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _settings.CustomColors = dlg.CustomColors;
        _project.Checkpoint();
        swatch.BackColor = dlg.Color;
        apply(_project.Options, dlg.Color);
        _settings.RememberOptions(_project.Options);
        UpdateTitle();
        InvalidateCombined();
    }

    // ------------------------------------------------------------------ save / export

    private string SuggestFileName(string? currentPath, string fallback)
    {
        string baseName = currentPath != null ? Path.GetFileNameWithoutExtension(currentPath) : fallback;
        return _settings.AddTimestamp ? FileNames.WithTimestamp(baseName, DateTime.Now) : FileNames.StripTimestamp(baseName);
    }

    /// <summary>This tab's folder, else the most recent folder that still exists.</summary>
    private string? SuggestFolder(string? currentPath)
    {
        if (currentPath != null && Directory.Exists(Path.GetDirectoryName(currentPath))) return Path.GetDirectoryName(currentPath);
        return _settings.RecentFolders.FirstOrDefault(Directory.Exists)
               ?? (_settings.LastSaveFolder is { } f && Directory.Exists(f) ? f : null);
    }

    private void PrepareDialog(FileDialog dlg, string? folder)
    {
        if (folder != null) dlg.InitialDirectory = folder;
        // Recent folders show up in the dialog's left pane for one-click access.
        foreach (string f in _settings.RecentFolders.Where(Directory.Exists).Take(8))
        {
            try { dlg.CustomPlaces.Add(f); } catch { /* older dialogs may refuse; not important */ }
        }
    }

    private void RememberSave(string path, bool isProject)
    {
        if (isProject) FileNames.PushRecent(_settings.RecentFiles, path);
        string? folder = Path.GetDirectoryName(path);
        if (folder != null)
        {
            FileNames.PushRecent(_settings.RecentFolders, folder);
            _settings.LastSaveFolder = folder;
        }
        _settings.Save();
    }

    /// <summary>Saves the tab to its file (same location as before), or asks where if it was never saved.</summary>
    private bool Save()
    {
        _canvas.CommitPendingEdits();
        if (_project.Snips.Count == 0) { SetStatus("Nothing to save yet — take a snip first."); return false; }
        if (_project.FilePath == null) return SaveAs();
        return SaveProjectTo(_project.FilePath);
    }

    private bool SaveAs()
    {
        _canvas.CommitPendingEdits();
        if (_project.Snips.Count == 0) { SetStatus("Nothing to save yet — take a snip first."); return false; }
        using var dlg = new SaveFileDialog
        {
            Title = "Save Multi Snip image",
            FileName = SuggestFileName(_project.FilePath, FileNames.DefaultBaseName),
            Filter = "Multi Snip PNG — image with editable snips and drawings (*.png)|*.png",
            DefaultExt = "png",
            AddExtension = true,
        };
        PrepareDialog(dlg, SuggestFolder(_project.FilePath));
        if (dlg.ShowDialog(this) != DialogResult.OK) return false;
        return SaveProjectTo(dlg.FileName);
    }

    private bool SaveProjectTo(string path)
    {
        try
        {
            Cursor = Cursors.WaitCursor;
            ProjectFile.Save(_project, path);
            _project.FilePath = path;
            _project.Dirty = false;
            RememberSave(path, isProject: true);
            UpdateTitle();
            SetStatus($"Saved {path}");
            return true;
        }
        catch (Exception ex)
        {
            ShowError("Save failed", ex);
            return false;
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    /// <summary>Exports a plain image (no project data), e.g. a JPG for email.</summary>
    private void ExportImage()
    {
        _canvas.CommitPendingEdits();
        if (_project.Snips.Count == 0) { SetStatus("Nothing to export yet — take a snip first."); return; }
        using Bitmap final = _project.RenderFinal();
        SaveImageFile(final, _project.FilePath != null ? Path.GetFileNameWithoutExtension(_project.FilePath) : FileNames.DefaultBaseName, out _);
    }

    private bool SaveImageFile(Bitmap image, string baseName, out string? savedPath)
    {
        savedPath = null;
        using var dlg = new SaveFileDialog
        {
            Title = "Export image",
            FileName = SuggestFileName(null, baseName),
            Filter = "PNG image|*.png|JPEG image|*.jpg|Bitmap image|*.bmp|GIF image|*.gif",
            DefaultExt = "png",
            AddExtension = true,
        };
        PrepareDialog(dlg, SuggestFolder(_project.FilePath));
        if (dlg.ShowDialog(this) != DialogResult.OK) return false;

        try
        {
            string ext = Path.GetExtension(dlg.FileName).ToLowerInvariant();
            Color flattenColor = _project.Options.TransparentBackground ? Color.White : Color.FromArgb(255, _project.Options.Background);
            switch (ext)
            {
                case ".jpg" or ".jpeg":
                {
                    using Bitmap flat = ImageUtil.Flatten(image, flattenColor);
                    ImageCodecInfo codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                    using var p = new EncoderParameters(1);
                    p.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 92L);
                    flat.Save(dlg.FileName, codec, p);
                    break;
                }
                case ".bmp":
                {
                    using Bitmap flat = ImageUtil.Flatten(image, flattenColor);
                    flat.Save(dlg.FileName, ImageFormat.Bmp);
                    break;
                }
                case ".gif":
                    image.Save(dlg.FileName, ImageFormat.Gif);
                    break;
                default:
                    image.Save(dlg.FileName, ImageFormat.Png);
                    break;
            }
            savedPath = dlg.FileName;
            RememberSave(dlg.FileName, isProject: false);
            SetStatus($"Exported {dlg.FileName}");
            return true;
        }
        catch (Exception ex)
        {
            ShowError("Export failed", ex);
            return false;
        }
    }

    // ------------------------------------------------------------------ open

    private void OpenFiles()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Open a Multi Snip image, or add images as snips",
            Multiselect = true,
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|All files|*.*",
        };
        PrepareDialog(dlg, SuggestFolder(_project.FilePath));
        if (dlg.ShowDialog(this) == DialogResult.OK)
            OpenPaths(dlg.FileNames);
    }

    /// <summary>Multi Snip images open as their own tab (all snips restored); other images are added to the current tab.</summary>
    private void OpenPaths(IEnumerable<string> paths)
    {
        var plain = new List<string>();
        foreach (string path in paths)
        {
            Project? existing = _projects.FirstOrDefault(p => string.Equals(p.FilePath, path, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                ActivateProject(existing);
                continue;
            }
            try
            {
                Project? project = Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase) ? ProjectFile.TryOpen(path) : null;
                if (project == null)
                {
                    plain.Add(path);
                    continue;
                }
                project.NextBadgeNumber = Math.Max(project.NextBadgeNumber, project.SuggestNextBadge());
                // Reuse an empty untitled tab instead of leaving it behind.
                if (_project.Snips.Count == 0 && _project.FilePath == null && !_project.CanUndo)
                {
                    int i = _projects.IndexOf(_project);
                    _projects[i] = project;
                    _tabs.TabPages[i].Text = project.DisplayName;
                    ActivateProject(project);
                }
                else
                {
                    NewTab(project);
                }
                FileNames.PushRecent(_settings.RecentFiles, path);
                SetStatus($"Opened {path} — {project.Snips.Count} snip(s), ready to edit.");
            }
            catch (Exception ex)
            {
                ShowError($"Could not open {Path.GetFileName(path)}", ex);
            }
        }
        if (plain.Count > 0) AddImages(LoadImageFiles(plain), "File");
    }

    private void RebuildRecentMenus()
    {
        _recentFilesMenu.DropDownItems.Clear();
        foreach (string f in _settings.RecentFiles.Take(12))
        {
            string path = f;
            var item = new ToolStripMenuItem(path) { Enabled = File.Exists(path) };
            item.Click += (_, _) => OpenPaths(new[] { path });
            _recentFilesMenu.DropDownItems.Add(item);
        }
        if (_recentFilesMenu.DropDownItems.Count == 0) _recentFilesMenu.DropDownItems.Add(new ToolStripMenuItem("(none yet)") { Enabled = false });
        else
        {
            _recentFilesMenu.DropDownItems.Add(new ToolStripSeparator());
            _recentFilesMenu.DropDownItems.Add("Clear list", null, (_, _) => _settings.RecentFiles.Clear());
        }

        _recentFoldersMenu.DropDownItems.Clear();
        foreach (string f in _settings.RecentFolders.Take(12))
        {
            string folder = f;
            var item = new ToolStripMenuItem(folder) { Enabled = Directory.Exists(folder), ToolTipText = "Open this folder in Explorer" };
            item.Click += (_, _) =>
            {
                try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true }); }
                catch (Exception ex) { ShowError("Could not open the folder", ex); }
            };
            _recentFoldersMenu.DropDownItems.Add(item);
        }
        if (_recentFoldersMenu.DropDownItems.Count == 0) _recentFoldersMenu.DropDownItems.Add(new ToolStripMenuItem("(none yet)") { Enabled = false });
    }

    // ------------------------------------------------------------------ session (open tabs survive a restart)

    private sealed record SessionInfo(List<string> Files, int Active);

    private void SaveSession()
    {
        try
        {
            _canvas.CommitPendingEdits();
            string dir = Settings.SessionFolder;
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            Directory.CreateDirectory(dir);
            var files = new List<string>();
            foreach (Project p in _projects.Where(p => p.Snips.Count > 0 || p.FilePath != null))
            {
                string name = $"tab{files.Count + 1:00}.msnip";
                File.WriteAllBytes(Path.Combine(dir, name), ProjectArchive.Write(ProjectFile.ToDocument(p)));
                files.Add(name);
            }
            int active = Math.Max(0, _projects.Where(p => p.Snips.Count > 0 || p.FilePath != null).ToList().IndexOf(_project));
            File.WriteAllText(Path.Combine(dir, "session.json"), JsonSerializer.Serialize(new SessionInfo(files, active)));
        }
        catch
        {
            // Restoring tabs is a convenience; never block closing the app.
        }
    }

    private void RestoreSession()
    {
        try
        {
            string file = Path.Combine(Settings.SessionFolder, "session.json");
            if (!File.Exists(file)) return;
            var info = JsonSerializer.Deserialize<SessionInfo>(File.ReadAllText(file));
            if (info == null) return;
            foreach (string name in info.Files)
            {
                try
                {
                    ProjectDocument doc = ProjectArchive.Read(File.ReadAllBytes(Path.Combine(Settings.SessionFolder, name)));
                    Project p = ProjectFile.FromDocument(doc);
                    p.FilePath = doc.FilePath;
                    p.Dirty = doc.Dirty;
                    NewTab(p);
                }
                catch
                {
                    // skip a damaged tab, keep the others
                }
            }
            if (info.Active >= 0 && info.Active < _projects.Count) ActivateProject(_projects[info.Active]);
            if (_projects.Count > 0) _pendingNote = $"Restored {_projects.Count} tab(s) from last time";
        }
        catch
        {
            // ignore: start with a fresh tab
        }
    }
}
