using System.Collections.ObjectModel;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Microsoft.Win32;
using Microsoft.VisualBasic.FileIO;
using MeshCaddy.Models;
using MeshCaddy.Services;
using Point = System.Windows.Point;

namespace MeshCaddy;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<FileItem> _visibleFiles = [];
    private List<FileItem> _allFiles = [];
    private string? _folder;
    private CancellationTokenSource? _loadCancellation;
    private Point _dragStart;
    private bool _orbiting;
    private double _yaw = -40;
    private double _pitch = 25;
    private double _distance = 10;
    private Point3D _target;
    private bool _sortDescending = true;
    private readonly ConcurrentDictionary<string, Task<ModelMesh>> _modelCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _modelCacheSizes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _modelCacheOrder = new();
    private readonly object _cacheLock = new();
    private long _modelCacheBytes;
    private long _modelCacheBudgetBytes;
    private CancellationTokenSource _folderCacheCancellation = new();

    public MainWindow()
    {
        InitializeComponent();
        ThemeManager.Apply("System");
        FileList.ItemsSource = _visibleFiles;
        UpdateCamera();
        StatusText.Text = "Ready";
        var startupPath = Environment.GetCommandLineArgs().Skip(1).FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(startupPath)) Loaded += (_, _) => OpenDroppedPath(startupPath);
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose a folder containing 3D models", Multiselect = false };
        if (_folder is not null) dialog.InitialDirectory = _folder;
        if (dialog.ShowDialog(this) == true) LoadFolder(dialog.FolderName);
    }

    private void LoadFolder(string folder, string? selectPath = null)
    {
        try
        {
            _folder = folder;
            _folderCacheCancellation.Cancel();
            _folderCacheCancellation.Dispose();
            _folderCacheCancellation = new CancellationTokenSource();
            _modelCache.Clear();
            lock (_cacheLock)
            {
                _modelCacheSizes.Clear();
                _modelCacheOrder.Clear();
                _modelCacheBytes = 0;
                _modelCacheBudgetBytes = CalculateCacheBudget();
            }
            PathBox.Text = folder;
            _allFiles = Directory.EnumerateFiles(folder)
                .Where(IsSupported)
                .OrderBy(path => Path.GetFileName(path), StringComparer.CurrentCultureIgnoreCase)
                .Select(path => new FileItem { FullPath = path }).ToList();
            ApplyFilter(selectPath);
            StatusText.Text = _allFiles.Count == 0 ? "No supported 3D model files in this folder" : "Folder loaded";
            _ = PreloadFolderAsync(_allFiles, _folderCacheCancellation.Token);
        }
        catch (Exception ex) { ShowError("Could not open the folder", ex); }
    }

    private void ApplyFilter(string? selectPath = null)
    {
        var query = SearchBox.Text.Trim();
        var current = selectPath ?? (FileList.SelectedItem as FileItem)?.FullPath;
        _visibleFiles.Clear();
        IEnumerable<FileItem> files = _allFiles.Where(f => query.Length == 0 || f.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase));
        var sort = (SortBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "Name";
        files = sort switch
        {
            "Modified" => _sortDescending ? files.OrderByDescending(f => f.Modified) : files.OrderBy(f => f.Modified),
            "Created" => _sortDescending ? files.OrderByDescending(f => f.Created) : files.OrderBy(f => f.Created),
            "Size" => _sortDescending ? files.OrderByDescending(f => f.Size) : files.OrderBy(f => f.Size),
            "Type" => _sortDescending ? files.OrderByDescending(f => f.Extension).ThenByDescending(f => f.Name) : files.OrderBy(f => f.Extension).ThenBy(f => f.Name),
            _ => _sortDescending ? files.OrderByDescending(f => f.Name, StringComparer.CurrentCultureIgnoreCase) : files.OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase)
        };
        foreach (var file in files)
            _visibleFiles.Add(file);
        FileCountText.Text = $"{_visibleFiles.Count} model{(_visibleFiles.Count == 1 ? "" : "s")}";
        var selected = _visibleFiles.FirstOrDefault(f => string.Equals(f.FullPath, current, StringComparison.OrdinalIgnoreCase));
        FileList.SelectedItem = selected ?? _visibleFiles.FirstOrDefault();
    }

    private async void FileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FileList.SelectedItem is not FileItem file)
        {
            ModelVisual.Content = null;
            EmptyState.Visibility = Visibility.Visible;
            ModelInfoText.Text = "";
            return;
        }

        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = new CancellationTokenSource();
        var load = _loadCancellation;
        LoadingOverlay.Visibility = Visibility.Visible;
        StatusText.Text = $"Loading {file.Name}…";

        try
        {
            var modelTask = _modelCache.GetOrAdd(file.FullPath, path => ModelLoader.LoadAsync(path, _folderCacheCancellation.Token));
            var mesh = await modelTask.WaitAsync(load.Token);
            KeepCached(file.FullPath, mesh, allowEviction: true);
            if (!ReferenceEquals(_loadCancellation, load)) return;
            ShowMesh(mesh);
            StatusText.Text = file.Name;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            ModelVisual.Content = null;
            EmptyState.Visibility = Visibility.Visible;
            StatusText.Text = $"Could not preview {file.Name}";
            MessageBox.Show(this, ex.Message, "Could not load model", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            if (ReferenceEquals(_loadCancellation, load)) LoadingOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowMesh(ModelMesh source)
    {
        var defaultColor = 0xFF24C7A4u;
        var coloredTriangles = Enumerable.Range(0, source.TriangleCount)
            .GroupBy(index => index < source.TriangleColors.Count ? source.TriangleColors[index] ?? defaultColor : defaultColor);
        var models = new Model3DGroup();
        foreach (var colorGroup in coloredTriangles)
        {
            var points = new Point3DCollection();
            var normals = new Vector3DCollection();
            var indices = new Int32Collection();
            foreach (var triangleIndex in colorGroup)
            {
                for (var corner = 0; corner < 3; corner++)
                {
                    var sourceIndex = source.Indices[triangleIndex * 3 + corner];
                    var point = source.Positions[sourceIndex];
                    var normal = source.Normals[sourceIndex];
                    points.Add(new Point3D(point.X, point.Y, point.Z));
                    normals.Add(new Vector3D(normal.X, normal.Y, normal.Z));
                    indices.Add(indices.Count);
                }
            }
            var argb = colorGroup.Key;
            var color = Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
            var material = new MaterialGroup();
            material.Children.Add(new DiffuseMaterial(new SolidColorBrush(color)));
            material.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromArgb(color.A, 220, 235, 240)), 28));
            var mesh = new MeshGeometry3D { Positions = points, TriangleIndices = indices, Normals = normals };
            models.Children.Add(new GeometryModel3D(mesh, material) { BackMaterial = material });
        }
        ModelVisual.Content = models;
        EmptyState.Visibility = Visibility.Collapsed;

        var min = source.Min;
        var max = source.Max;
        var size = max - min;
        _target = new Point3D((min.X + max.X) / 2, (min.Y + max.Y) / 2, (min.Z + max.Z) / 2);
        _distance = Math.Max(0.01, Math.Max(size.X, Math.Max(size.Y, size.Z)) * 2.1);
        _yaw = -40;
        _pitch = 25;
        UpdateCamera();
        var savedColorCount = source.TriangleColors.Where(color => color.HasValue).Select(color => color!.Value).Distinct().Count();
        var colorInfo = savedColorCount > 0 ? $"  ·  {savedColorCount} saved color{(savedColorCount == 1 ? "" : "s")}" : "";
        ModelInfoText.Text = $"{source.TriangleCount:N0} triangles  ·  {size.X:0.##} × {size.Y:0.##} × {size.Z:0.##} {source.UnitLabel}{colorInfo}";
    }

    private void UpdateCamera()
    {
        var yaw = _yaw * Math.PI / 180;
        var pitch = _pitch * Math.PI / 180;
        var offset = new Vector3D(Math.Cos(pitch) * Math.Cos(yaw), Math.Cos(pitch) * Math.Sin(yaw), Math.Sin(pitch)) * _distance;
        Camera.Position = _target + offset;
        Camera.LookDirection = _target - Camera.Position;
        Camera.UpDirection = new Vector3D(0, 0, 1);
        Camera.NearPlaneDistance = Math.Max(0.001, _distance / 1000);
        Camera.FarPlaneDistance = Math.Max(1000, _distance * 100);
    }

    private void Viewport_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (e.ClickCount == 2 && FileList.SelectedItem is FileItem) { UpdateCamera(); return; }
        _orbiting = true;
        _dragStart = e.GetPosition(Viewport);
        Viewport.CaptureMouse();
    }

    private void Viewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_orbiting) return;
        var point = e.GetPosition(Viewport);
        _yaw += (point.X - _dragStart.X) * 0.45;
        _pitch = Math.Clamp(_pitch + (point.Y - _dragStart.Y) * 0.45, -89, 89);
        _dragStart = point;
        UpdateCamera();
    }

    private void Viewport_MouseUp(object sender, MouseButtonEventArgs e) { _orbiting = false; Viewport.ReleaseMouseCapture(); }
    private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e) { _distance *= e.Delta > 0 ? 0.85 : 1.18; _distance = Math.Max(0.001, _distance); UpdateCamera(); }

    private void Previous_Click(object sender, RoutedEventArgs e) => SelectRelative(-1);
    private void Next_Click(object sender, RoutedEventArgs e) => SelectRelative(1);
    private void SelectRelative(int delta)
    {
        if (_visibleFiles.Count == 0) return;
        var index = Math.Max(0, FileList.SelectedIndex);
        FileList.SelectedIndex = (index + delta + _visibleFiles.Count) % _visibleFiles.Count;
        FileList.ScrollIntoView(FileList.SelectedItem);
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) { if (_folder is not null) LoadFolder(_folder, (FileList.SelectedItem as FileItem)?.FullPath); }
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) { if (IsLoaded) ApplyFilter(); }
    private void SortBox_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (IsLoaded) ApplyFilter(); }
    private void SortDirection_Click(object sender, RoutedEventArgs e)
    {
        _sortDescending = !_sortDescending;
        SortDirectionButton.Content = _sortDescending ? "↓" : "↑";
        ApplyFilter();
    }

    private void ThemeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeBox.SelectedItem is ComboBoxItem item && item.Tag is string mode)
            ThemeManager.Apply(mode);
    }

    private async Task PreloadFolderAsync(IReadOnlyList<FileItem> files, CancellationToken token)
    {
        if (files.Count == 0) { PreloadStatusText.Text = ""; return; }
        var completed = 0;
        var nextIndex = -1;
        var budgetReached = false;
        var workers = Enumerable.Range(0, Math.Min(2, files.Count)).Select(async _ =>
        {
            while (!token.IsCancellationRequested && !Volatile.Read(ref budgetReached))
            {
                var index = Interlocked.Increment(ref nextIndex);
                if (index >= files.Count) break;
                var file = files[index];
                try
                {
                    var task = _modelCache.GetOrAdd(file.FullPath, path => ModelLoader.LoadAsync(path, token));
                    var mesh = await task;
                    if (!KeepCached(file.FullPath, mesh, allowEviction: false))
                    {
                        Volatile.Write(ref budgetReached, true);
                        break;
                    }
                }
                catch (OperationCanceledException) { break; }
                catch { }

                var count = Interlocked.Increment(ref completed);
                if (!token.IsCancellationRequested)
                    await Dispatcher.InvokeAsync(() => PreloadStatusText.Text = $"Caching previews {count}/{files.Count} · {FormatCacheBudget()} RAM budget");
            }
        }).ToArray();
        try { await Task.WhenAll(workers); }
        catch (OperationCanceledException) { }
        if (!token.IsCancellationRequested)
            await Dispatcher.InvokeAsync(() => PreloadStatusText.Text = budgetReached
                ? $"{completed} previews cached · RAM limit reached"
                : $"{completed} previews cached");
    }

    private bool KeepCached(string path, ModelMesh mesh, bool allowEviction)
    {
        var size = EstimateMeshBytes(mesh);
        lock (_cacheLock)
        {
            if (_modelCacheSizes.ContainsKey(path)) return true;
            if (!allowEviction && _modelCacheBytes + size > _modelCacheBudgetBytes)
            {
                _modelCache.TryRemove(path, out _);
                return false;
            }

            while (allowEviction && _modelCacheBytes + size > _modelCacheBudgetBytes && _modelCacheOrder.Count > 0)
            {
                var oldest = _modelCacheOrder.Dequeue();
                if (!_modelCacheSizes.Remove(oldest, out var oldSize)) continue;
                _modelCacheBytes -= oldSize;
                _modelCache.TryRemove(oldest, out _);
            }

            _modelCacheSizes[path] = size;
            _modelCacheOrder.Enqueue(path);
            _modelCacheBytes += size;
            return true;
        }
    }

    private static long EstimateMeshBytes(ModelMesh mesh) =>
        mesh.Positions.Count * 12L + mesh.Normals.Count * 12L + mesh.Indices.Count * 4L + mesh.TriangleColors.Count * 8L + 4096;

    private static long CalculateCacheBudget()
    {
        try
        {
            var status = new MemoryStatusEx();
            if (!GlobalMemoryStatusEx(status)) return 512L * 1024 * 1024;
            var available = checked((long)status.AvailablePhysicalMemory);
            return Math.Clamp(available / 4, 256L * 1024 * 1024, 4L * 1024 * 1024 * 1024);
        }
        catch { return 512L * 1024 * 1024; }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MemoryStatusEx
    {
        public uint Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
        public uint MemoryLoad;
        public ulong TotalPhysicalMemory;
        public ulong AvailablePhysicalMemory;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtualMemory;
        public ulong AvailableVirtualMemory;
        public ulong AvailableExtendedVirtualMemory;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx buffer);

    private string FormatCacheBudget() => _modelCacheBudgetBytes >= 1024L * 1024 * 1024
        ? $"{_modelCacheBudgetBytes / (1024d * 1024 * 1024):0.#} GB"
        : $"{_modelCacheBudgetBytes / (1024d * 1024):0} MB";

    private void NewFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_folder is null) return;
        var dialog = new InputDialog("New folder", "Folder name:") { Owner = this };
        if (dialog.ShowDialog() != true) return;
        try { Directory.CreateDirectory(Path.Combine(_folder, SafeName(dialog.Value))); StatusText.Text = $"Created folder “{dialog.Value}”"; }
        catch (Exception ex) { ShowError("Could not create the folder", ex); }
    }

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (FileList.SelectedItem is not FileItem file) return;
        var dialog = new InputDialog("Rename model", "New file name:", file.Name) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var destination = Path.Combine(Path.GetDirectoryName(file.FullPath)!, SafeName(dialog.Value));
            if (!IsSupported(destination)) throw new InvalidOperationException("The file name must end in .stl or .3mf.");
            File.Move(file.FullPath, destination);
            LoadFolder(_folder!, destination);
            StatusText.Text = "File renamed";
        }
        catch (Exception ex) { ShowError("Could not rename the file", ex); }
    }

    private void Move_Click(object sender, RoutedEventArgs e)
    {
        var files = GetOperationTargets();
        if (files.Count == 0) return;
        var dialog = new OpenFolderDialog { Title = "Move model to folder", InitialDirectory = _folder };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            foreach (var file in files)
                File.Move(file.FullPath, Path.Combine(dialog.FolderName, file.Name));
            LoadFolder(_folder!);
            StatusText.Text = files.Count == 1 ? $"Moved {files[0].Name}" : $"Moved {files.Count} files";
        }
        catch (Exception ex) { ShowError("Could not move the file", ex); }
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (FileList.SelectedItem is not FileItem file) return;
        var dialog = new OpenFolderDialog { Title = "Copy model to folder", InitialDirectory = _folder };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            File.Copy(file.FullPath, Path.Combine(dialog.FolderName, file.Name));
            StatusText.Text = $"Copied {file.Name}";
        }
        catch (Exception ex) { ShowError("Could not copy the file", ex); }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        var files = GetOperationTargets();
        if (files.Count == 0) return;
        var prompt = files.Count == 1 ? $"Move “{files[0].Name}” to the Recycle Bin?" : $"Move {files.Count} selected models to the Recycle Bin?";
        if (MessageBox.Show(this, prompt, "Recycle models", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            foreach (var file in files)
                FileSystem.DeleteFile(file.FullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            LoadFolder(_folder!);
            StatusText.Text = files.Count == 1 ? $"Moved {files[0].Name} to the Recycle Bin" : $"Moved {files.Count} files to the Recycle Bin";
        }
        catch (Exception ex) { ShowError("Could not recycle the file", ex); }
    }

    private List<FileItem> GetOperationTargets()
    {
        var checkedFiles = _allFiles.Where(file => file.IsChecked).ToList();
        if (checkedFiles.Count > 0) return checkedFiles;
        return FileList.SelectedItem is FileItem selected ? [selected] : [];
    }

    private void FileCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        var selectedCount = _allFiles.Count(file => file.IsChecked);
        FileCountText.Text = selectedCount == 0
            ? $"{_visibleFiles.Count} model{(_visibleFiles.Count == 1 ? "" : "s")}" 
            : $"{selectedCount} selected · {_visibleFiles.Count} shown";
    }

    private void SelectAllCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        var isChecked = SelectAllCheckBox.IsChecked == true;
        foreach (var file in _visibleFiles) file.IsChecked = isChecked;
        FileList.Items.Refresh();
        FileCheckBox_Changed(sender, e);
    }

    private void Reveal_Click(object sender, RoutedEventArgs e)
    {
        var file = (FileList.SelectedItem as FileItem)?.FullPath;
        if (file is null) return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{file}\"") { UseShellExecute = true });
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.O) { OpenFolder_Click(sender, e); e.Handled = true; }
        else if (e.Key == Key.F5) { Refresh_Click(sender, e); e.Handled = true; }
        else if (e.Key == Key.Left) { SelectRelative(-1); e.Handled = true; }
        else if (e.Key == Key.Right) { SelectRelative(1); e.Handled = true; }
        else if (e.Key == Key.Delete) { Delete_Click(sender, e); e.Handled = true; }
    }

    private void Window_DragOver(object sender, DragEventArgs e) { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; }
    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths) OpenDroppedPath(paths[0]);
    }

    private void OpenDroppedPath(string path)
    {
        if (Directory.Exists(path)) LoadFolder(path);
        else if (File.Exists(path) && IsSupported(path)) LoadFolder(Path.GetDirectoryName(path)!, path);
    }

    private static bool IsSupported(string path) => ModelLoader.SupportedExtensions.Contains(Path.GetExtension(path));
    private static string SafeName(string name) => name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name is "." or ".." ? throw new InvalidOperationException("That name contains invalid characters.") : name;
    private void ShowError(string title, Exception ex) { StatusText.Text = title; MessageBox.Show(this, ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Warning); }
}
