using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Catalyst
{
    public partial class MainWindow : Window
    {
        // Per-category target formats. Lists include every readable ext for the
        // category, so (targets - source) always fits the 4 ring nodes.
        private static readonly string[] ImageTargets = { ".jpg", ".png", ".bmp", ".gif" };
        private static readonly string[] VideoTargets = { ".mp4", ".mkv", ".mov", ".avi", ".webm" };
        private static readonly string[] AudioTargets = { ".mp3", ".wav", ".m4a", ".flac", ".ogg" };

        private static readonly HashSet<string> ImageReadable =
            new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff" };
        private static readonly HashSet<string> VideoReadable =
            new(VideoTargets, StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> AudioReadable =
            new(AudioTargets, StringComparer.OrdinalIgnoreCase);

        // Back-compat union: everything the app accepts as a drag source at all.
        private static readonly HashSet<string> ReadableExts =
            new(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, ImageFormat> FormatByExt =
            new(StringComparer.OrdinalIgnoreCase)
            {
                { ".jpg", ImageFormat.Jpeg },
                { ".jpeg", ImageFormat.Jpeg },
                { ".png", ImageFormat.Png },
                { ".bmp", ImageFormat.Bmp },
                { ".gif", ImageFormat.Gif },
                { ".tiff", ImageFormat.Tiff },
            };

        private static readonly Regex DurationRegex =
            new(@"Duration:\s*(\d+):(\d+):([\d\.]+)", RegexOptions.Compiled);
        private static readonly Regex FfmpegTimeRegex =
            new(@"time=(\d+):(\d+):([\d\.]+)", RegexOptions.Compiled);

        static MainWindow()
        {
            foreach (var e in ImageReadable) ReadableExts.Add(e);
            foreach (var e in VideoReadable) ReadableExts.Add(e);
            foreach (var e in AudioReadable) ReadableExts.Add(e);
        }

        // Fixed screen positions (relative to the window) of the four ring nodes
        private static readonly System.Windows.Point[] NodeCenters =
        {
            new(130, 40),   // top
            new(220, 130),  // right
            new(130, 220),  // bottom
            new(40, 130),   // left
        };

        private Grid[] _nodeGrids = Array.Empty<Grid>();
        private System.Windows.Shapes.Ellipse[] _nodeEllipses = Array.Empty<System.Windows.Shapes.Ellipse>();
        private TextBlock[] _nodeTexts = Array.Empty<TextBlock>();
        private string?[] _nodeFormats = new string?[4];
        private int _highlightedIndex = -1;

        // Multi-file drag state: supported files grouped by category (in drop
        // order), plus the category currently shown on the ring.
        private List<(string Category, List<string> Files)> _dragGroups = new();
        private string? _currentCategory;
        private bool _isConverting;

        // Pending other-category groups after a mixed drop. The ring stays up
        // for the next group; clicking a node converts that group (Task 3).
        private readonly Queue<(string Category, List<string> Files)> _pendingGroups = new();

        private readonly DispatcherTimer _resetTimer;
        private System.Windows.Forms.NotifyIcon? _trayIcon;
        private System.Drawing.Icon? _appIcon;
        private HistoryWindow? _historyWindow;

        private static string SettingsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Catalyst", "settings.json");

        private sealed class WidgetSettings
        {
            public double Left { get; set; }
            public double Top { get; set; }
        }

        private static readonly SolidColorBrush IdleNucleusBrush = new(System.Windows.Media.Color.FromRgb(0x18, 0x5F, 0xA5));
        private static readonly SolidColorBrush ActiveNucleusBrush = new(System.Windows.Media.Color.FromRgb(0x37, 0x8A, 0xDD));
        private static readonly SolidColorBrush NodeIdleFill = new(System.Windows.Media.Color.FromRgb(0xB5, 0xD4, 0xF4));
        private static readonly SolidColorBrush NodeHighlightFill = new(System.Windows.Media.Color.FromRgb(0x37, 0x8A, 0xDD));

        public MainWindow()
        {
            InitializeComponent();

            _nodeGrids = new[] { NodeTop, NodeRight, NodeBottom, NodeLeft };
            _nodeEllipses = new[] { NodeTopEllipse, NodeRightEllipse, NodeBottomEllipse, NodeLeftEllipse };
            _nodeTexts = new[] { NodeTopText, NodeRightText, NodeBottomText, NodeLeftText };

            // Click-to-pick for pending groups after a mixed drop. Preview (tunnel)
            // + Handled so the window's DragMove handler doesn't steal the click.
            for (int i = 0; i < _nodeGrids.Length; i++)
            {
                int index = i;
                _nodeGrids[i].PreviewMouseLeftButtonDown += (_, e) =>
                {
                    if (_pendingGroups.Count > 0 && _nodeFormats[index] is not null)
                    {
                        e.Handled = true;
                        OnNodePick(index);
                    }
                };
            }

            _resetTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _resetTimer.Tick += (_, _) =>
            {
                StatusText.Text = "drop file";
                Nucleus.Fill = IdleNucleusBrush;
                _resetTimer.Stop();
            };
        }

        // Restore the last known position, or park in the bottom-right corner on first run
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var saved = LoadSettings();
            if (saved is not null)
            {
                Left = saved.Left;
                Top = saved.Top;
            }
            else
            {
                var workArea = SystemParameters.WorkArea;
                Left = workArea.Right - Width - 24;
                Top = workArea.Bottom - Height - 24;
            }

            SetupTrayIcon();
        }

        private static System.Drawing.Icon? LoadAppIcon()
        {
            try
            {
                var uri = new Uri("pack://application:,,,/Assets/Catalyst.ico");
                var info = System.Windows.Application.GetResourceStream(uri);
                if (info?.Stream is null) return null;
                using var s = info.Stream;
                return new System.Drawing.Icon(s);
            }
            catch
            {
                return null;
            }
        }

        private void SetupTrayIcon()
        {
            var menu = new System.Windows.Forms.ContextMenuStrip();

            var historyItem = new System.Windows.Forms.ToolStripMenuItem("History");
            historyItem.Click += (_, _) => Dispatcher.Invoke(ShowHistory);
            menu.Items.Add(historyItem);

            var settingsItem = new System.Windows.Forms.ToolStripMenuItem("Settings");
            settingsItem.Click += (_, _) =>
                System.Windows.Forms.MessageBox.Show("Settings coming soon.", "Catalyst");
            menu.Items.Add(settingsItem);

            var quitItem = new System.Windows.Forms.ToolStripMenuItem("Quit");
            quitItem.Click += (_, _) => Close();
            menu.Items.Add(quitItem);

            _appIcon = LoadAppIcon();
            _trayIcon = new System.Windows.Forms.NotifyIcon
            {
                Icon = _appIcon ?? System.Drawing.SystemIcons.Application,
                Text = "Catalyst",
                Visible = true,
                ContextMenuStrip = menu,
            };
        }

        private void ShowHistory()
        {
            if (_historyWindow is null)
            {
                _historyWindow = new HistoryWindow();
                _historyWindow.Closed += (_, _) => _historyWindow = null;
                _historyWindow.Show();
            }
            else
            {
                _historyWindow.Refresh();
                if (!_historyWindow.IsVisible) _historyWindow.Show();
                _historyWindow.Activate();
            }
        }

        private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            SaveSettings(Left, Top);

            try { _historyWindow?.Close(); } catch { /* best effort */ }

            if (_trayIcon is not null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }
            _appIcon?.Dispose();
            _appIcon = null;
        }

        private static WidgetSettings? LoadSettings()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return null;
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<WidgetSettings>(json);
            }
            catch
            {
                return null; // Corrupt or unreadable settings file: fall back to defaults.
            }
        }

        private static void SaveSettings(double left, double top)
        {
            try
            {
                var dir = Path.GetDirectoryName(SettingsPath)!;
                Directory.CreateDirectory(dir);
                var json = JsonSerializer.Serialize(new WidgetSettings { Left = left, Top = top });
                File.WriteAllText(SettingsPath, json);
            }
            catch
            {
                // Prototype: if we can't save position, just skip it silently.
            }
        }

        // Left-drag repositions the widget anywhere on screen
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        private static string NormalizeExt(string ext) =>
            ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ? ".jpg" : ext.ToLowerInvariant();

        private static string? GetCategory(string ext)
        {
            if (ImageReadable.Contains(ext)) return "image";
            if (VideoReadable.Contains(ext)) return "video";
            if (AudioReadable.Contains(ext)) return "audio";
            return null;
        }

        private static string[] TargetsForCategory(string category, string excludeNormalizedExt)
        {
            string[] all = category switch
            {
                "video" => VideoTargets,
                "audio" => AudioTargets,
                _ => ImageTargets,
            };
            return all.Where(f => f != excludeNormalizedExt).ToArray();
        }

        private void ShowFormatsForCategory(string category, string excludeNormalizedExt)
        {
            var candidates = TargetsForCategory(category, excludeNormalizedExt);
            for (int i = 0; i < _nodeGrids.Length; i++)
            {
                if (i < candidates.Length)
                {
                    _nodeFormats[i] = candidates[i];
                    _nodeTexts[i].Text = candidates[i].TrimStart('.').ToUpperInvariant();
                    _nodeGrids[i].Visibility = Visibility.Visible;
                    _nodeEllipses[i].Fill = NodeIdleFill;
                }
                else
                {
                    _nodeFormats[i] = null;
                    _nodeGrids[i].Visibility = Visibility.Collapsed;
                }
            }

            _highlightedIndex = -1;
            Nucleus.Fill = ActiveNucleusBrush;
        }

        private void Window_DragEnter(object sender, System.Windows.DragEventArgs e)
        {
            if (_isConverting) return;
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

            var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
            if (files.Length == 0) return;

            // Group supported files by category, preserving drop order.
            var order = new List<string>();
            var byCategory = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in files)
            {
                var cat = GetCategory(Path.GetExtension(f));
                if (cat is null) continue;
                if (!byCategory.TryGetValue(cat, out var list))
                {
                    list = new List<string>();
                    byCategory[cat] = list;
                    order.Add(cat);
                }
                list.Add(f);
            }

            if (order.Count == 0)
            {
                StatusText.Text = "unsupported";
                return;
            }

            _dragGroups = order.Select(c => (c, byCategory[c])).ToList();
            _pendingGroups.Clear();
            _currentCategory = _dragGroups[0].Category;

            var firstExt = NormalizeExt(Path.GetExtension(_dragGroups[0].Files[0]));
            ShowFormatsForCategory(_currentCategory, firstExt);
            StatusText.Text = _dragGroups.Count > 1
                ? $"mixed — pick for {_currentCategory}"
                : "pick a format";
        }

        private void Window_DragOver(object sender, System.Windows.DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

            var cursor = e.GetPosition(this);
            HighlightNearestNode(cursor);
        }

        private void HighlightNearestNode(System.Windows.Point cursor)
        {
            int nearest = -1;
            double bestDistSq = double.MaxValue;

            for (int i = 0; i < NodeCenters.Length; i++)
            {
                if (_nodeFormats[i] is null) continue;

                double dx = cursor.X - NodeCenters[i].X;
                double dy = cursor.Y - NodeCenters[i].Y;
                double distSq = dx * dx + dy * dy;

                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    nearest = i;
                }
            }

            if (nearest == _highlightedIndex) return;

            if (_highlightedIndex >= 0)
                _nodeEllipses[_highlightedIndex].Fill = NodeIdleFill;

            if (nearest >= 0)
                _nodeEllipses[nearest].Fill = NodeHighlightFill;

            _highlightedIndex = nearest;
        }

        private void Window_DragLeave(object sender, System.Windows.DragEventArgs e)
        {
            if (_pendingGroups.Count > 0) return; // keep the click-to-pick ring up
            HideNodes();
            _dragGroups = new();
            _currentCategory = null;
            Nucleus.Fill = IdleNucleusBrush;
            StatusText.Text = "drop file";
        }

        private void HideNodes()
        {
            for (int i = 0; i < _nodeGrids.Length; i++)
            {
                _nodeGrids[i].Visibility = Visibility.Collapsed;
                _nodeFormats[i] = null;
            }
            _highlightedIndex = -1;
        }

        private async void Window_Drop(object sender, System.Windows.DragEventArgs e)
        {
            if (_isConverting)
            {
                StatusText.Text = "busy...";
                return;
            }
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

            var cursor = e.GetPosition(this);
            HighlightNearestNode(cursor);

            string? targetExt = _highlightedIndex >= 0 ? _nodeFormats[_highlightedIndex] : null;
            HideNodes();

            if (targetExt is null)
            {
                StatusText.Text = "no format";
                Nucleus.Fill = IdleNucleusBrush;
                _dragGroups = new();
                _currentCategory = null;
                _resetTimer.Stop();
                _resetTimer.Start();
                return;
            }

            var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;

            // Re-group from the actual drop (drag state may be stale). The picked
            // format applies to the first file's category; other categories queue
            // up as click-to-pick rounds instead of being silently skipped.
            var order = new List<string>();
            var byCategory = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var unsupported = new List<string>();
            foreach (var f in files)
            {
                var cat = GetCategory(Path.GetExtension(f));
                if (cat is null) { unsupported.Add(f); continue; }
                if (!byCategory.TryGetValue(cat, out var list))
                {
                    list = new List<string>();
                    byCategory[cat] = list;
                    order.Add(cat);
                }
                list.Add(f);
            }

            if (order.Count == 0)
            {
                StatusText.Text = "unsupported";
                Nucleus.Fill = IdleNucleusBrush;
                _resetTimer.Stop();
                _resetTimer.Start();
                return;
            }

            _pendingGroups.Clear();
            string firstCategory = order[0];
            for (int i = 1; i < order.Count; i++)
                _pendingGroups.Enqueue((order[i], byCategory[order[i]]));

            var skipped = unsupported.Select(f => $"{Path.GetFileName(f)} (unsupported)").ToList();
            int converted = await ConvertGroupAsync(firstCategory, byCategory[firstCategory], targetExt, skipped);

            FinishDropRound(converted, skipped);
        }

        /// <summary>
        /// Click-to-pick handler for queued groups after a mixed drop.
        /// </summary>
        private async void OnNodePick(int index)
        {
            if (_isConverting || _pendingGroups.Count == 0) return;
            string? targetExt = _nodeFormats[index];
            if (targetExt is null) return;

            var (category, fileList) = _pendingGroups.Dequeue();
            HideNodes();

            var skipped = new List<string>();
            int converted = await ConvertGroupAsync(category, fileList, targetExt, skipped);

            FinishDropRound(converted, skipped);
        }

        /// <summary>
        /// Shared end-of-round UI: either arm the next queued group or show the summary.
        /// </summary>
        private void FinishDropRound(int converted, List<string> skipped)
        {
            if (_pendingGroups.Count > 0)
            {
                var (nextCategory, nextFiles) = _pendingGroups.Peek();
                var exclude = NormalizeExt(Path.GetExtension(nextFiles[0]));
                ShowFormatsForCategory(nextCategory, exclude);
                StatusText.Text = converted > 0
                    ? $"done ({converted}) — click for {nextCategory} ({nextFiles.Count})"
                    : $"click a format for {nextCategory} ({nextFiles.Count})";
                if (skipped.Count > 0) ShowSkippedTip(skipped);
                return;
            }

            _dragGroups = new();
            _currentCategory = null;

            if (converted > 0 && skipped.Count > 0)
            {
                StatusText.Text = $"done ({converted}), skipped {skipped.Count}";
                ShowSkippedTip(skipped);
            }
            else if (converted > 0)
            {
                StatusText.Text = $"done ({converted})";
            }
            else if (skipped.Count > 0)
            {
                StatusText.Text = "skipped";
                ShowSkippedTip(skipped);
            }
            else
            {
                StatusText.Text = "no match";
            }

            Nucleus.Fill = IdleNucleusBrush;
            _resetTimer.Stop();
            _resetTimer.Start();
        }

        private void ShowSkippedTip(List<string> skipped)
        {
            if (_trayIcon is null || skipped.Count == 0) return;
            try
            {
                var detail = string.Join("; ", skipped.Take(5));
                if (skipped.Count > 5) detail += $"; +{skipped.Count - 5} more";
                if (detail.Length > 250) detail = detail[..247] + "...";
                _trayIcon.ShowBalloonTip(4000, "Catalyst — skipped files", detail,
                    System.Windows.Forms.ToolTipIcon.Info);
            }
            catch
            {
                // Balloon tips are best-effort.
            }
        }

        /// <summary>
        /// Converts every file in one category group to targetExt. Images go
        /// through System.Drawing; video/audio shell out to ffmpeg. Reports
        /// text progress (Task 2: text-only) and records history per file.
        /// </summary>
        private async Task<int> ConvertGroupAsync(
            string category, List<string> files, string targetExt, List<string> skipped)
        {
            _isConverting = true;
            Nucleus.Fill = ActiveNucleusBrush;
            int converted = 0;

            try
            {
                for (int i = 0; i < files.Count; i++)
                {
                    var file = files[i];
                    var ext = Path.GetExtension(file);

                    if (GetCategory(ext) != category)
                    {
                        skipped.Add($"{Path.GetFileName(file)} (needs {GetCategory(ext)} pick)");
                        continue;
                    }
                    if (NormalizeExt(ext) == targetExt)
                    {
                        skipped.Add($"{Path.GetFileName(file)} (already {targetExt})");
                        continue;
                    }

                    try
                    {
                        if (category == "image")
                        {
                            StatusText.Text = files.Count > 1
                                ? $"file {i + 1}/{files.Count}..."
                                : "converting...";
                            var output = await Task.Run(() => ConvertImage(file, targetExt));
                            converted++;
                            HistoryStore.Add(new HistoryEntry
                            {
                                SourcePath = file,
                                OutputPath = output,
                                Category = category,
                                TargetExt = targetExt,
                                Timestamp = DateTimeOffset.Now,
                            });
                        }
                        else
                        {
                            string label = files.Count > 1 ? $"file {i + 1}/{files.Count}... " : "converting... ";
                            var progress = new Progress<double>(p =>
                                StatusText.Text = $"{label}{(int)Math.Round(p * 100)}%");
                            StatusText.Text = $"{label}0%";
                            var output = await ConvertMediaAsync(file, targetExt, progress);
                            converted++;
                            HistoryStore.Add(new HistoryEntry
                            {
                                SourcePath = file,
                                OutputPath = output,
                                Category = category,
                                TargetExt = targetExt,
                                Timestamp = DateTimeOffset.Now,
                            });
                        }
                    }
                    catch (Exception ex) when (ex is FileNotFoundException || ex is InvalidOperationException)
                    {
                        // Known operational failures (missing ffmpeg, bad exit code):
                        // surface the message, abort the rest of this group.
                        StatusText.Text = TrimStatus(ex.Message);
                        skipped.Add($"{Path.GetFileName(file)} ({TrimStatus(ex.Message, 60)})");
                        break;
                    }
                    catch
                    {
                        skipped.Add($"{Path.GetFileName(file)} (failed)");
                    }
                }
            }
            finally
            {
                _isConverting = false;
            }

            return converted;
        }

        private static string TrimStatus(string message, int max = 24)
        {
            message = message.Trim().ReplaceLineEndings(" ");
            return message.Length <= max ? message : message[..max];
        }

        private static string OutputPathFor(string sourcePath, string targetExt)
        {
            var directory = Path.GetDirectoryName(sourcePath)!;
            var baseName = Path.GetFileNameWithoutExtension(sourcePath);
            return Path.Combine(directory, $"{baseName}_catalyst{targetExt}");
        }

        private static string ConvertImage(string sourcePath, string targetExt)
        {
            var targetFormat = FormatByExt[targetExt];
            var outputPath = OutputPathFor(sourcePath, targetExt);

            using var original = new Bitmap(sourcePath);
            using var copy = new Bitmap(original); // decouple from the source file handle
            copy.Save(outputPath, targetFormat);
            return outputPath;
        }

        /// <summary>
        /// ffmpeg lookup: bundled <c>ffmpeg/ffmpeg.exe</c> next to the app first,
        /// then %LocalAppData%\Catalyst\ffmpeg (on-demand cache location),
        /// then PATH fallback ("ffmpeg"). Throws FileNotFoundException with a
        /// user-friendly message when nothing resolves.
        /// </summary>
        private static string ResolveFfmpegPath()
        {
            var bundled = Path.Combine(AppContext.BaseDirectory, "ffmpeg", "ffmpeg.exe");
            if (File.Exists(bundled)) return bundled;

            var cached = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Catalyst", "ffmpeg", "ffmpeg.exe");
            if (File.Exists(cached)) return cached;

            return "ffmpeg"; // resolved via PATH at Process.Start time
        }

        private static TimeSpan? ParseDuration(string text)
        {
            var m = DurationRegex.Match(text);
            if (!m.Success) return null;
            if (!int.TryParse(m.Groups[1].Value, out int h)) return null;
            if (!int.TryParse(m.Groups[2].Value, out int min)) return null;
            if (!double.TryParse(m.Groups[3].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double sec)) return null;
            try
            {
                return new TimeSpan(0, h, min, 0) + TimeSpan.FromSeconds(sec);
            }
            catch
            {
                return null;
            }
        }

        private static double? ParseFfmpegTimeSeconds(string line)
        {
            var m = FfmpegTimeRegex.Match(line);
            if (!m.Success) return null;
            if (!int.TryParse(m.Groups[1].Value, out int h)) return null;
            if (!int.TryParse(m.Groups[2].Value, out int min)) return null;
            if (!double.TryParse(m.Groups[3].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double sec)) return null;
            return h * 3600 + min * 60 + sec;
        }

        private static async Task<TimeSpan?> ProbeDurationAsync(string ffmpeg, string source, CancellationToken ct)
        {
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = ffmpeg,
                        Arguments = $"-hide_banner -i \"{source}\"",
                        RedirectStandardError = true,
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    }
                };
                process.Start();
                string stderr = await process.StandardError.ReadToEndAsync(ct);
                await process.WaitForExitAsync(ct);
                return ParseDuration(stderr);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Runs ffmpeg conversion asynchronously (UI stays responsive) and
        /// reports 0..1 progress parsed from -progress output (Task 2).
        /// Without a probeable duration it reports indeterminate 0 until done.
        /// </summary>
        private static async Task<string> ConvertMediaAsync(
            string sourcePath, string targetExt,
            IProgress<double>? progress, CancellationToken ct = default)
        {
            string ffmpeg = ResolveFfmpegPath();
            string outputPath = OutputPathFor(sourcePath, targetExt);

            TimeSpan? total = await ProbeDurationAsync(ffmpeg, sourcePath, ct);
            double totalSeconds = total?.TotalSeconds ?? 0;

            Process process;
            try
            {
                process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = ffmpeg,
                        Arguments = $"-y -hide_banner -nostats -progress pipe:1 -i \"{sourcePath}\" \"{outputPath}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    }
                };
                if (!process.Start())
                    throw new InvalidOperationException("ffmpeg failed to start.");
            }
            catch (System.ComponentModel.Win32Exception)
            {
                throw new FileNotFoundException(
                    "ffmpeg missing — build once with internet (auto-download), or place ffmpeg.exe on PATH. See README.");
            }

            using (process)
            {
                var stderrTail = new Queue<string>();
                var stdoutTask = Task.Run(async () =>
                {
                    string? line;
                    while ((line = await process.StandardOutput.ReadLineAsync()) != null)
                    {
                        // -progress pipe:1 emits out_time_ms= / out_time_us= lines.
                        double? seconds = null;
                        if (line.StartsWith("out_time_ms=", StringComparison.Ordinal)
                            && long.TryParse(line["out_time_ms=".Length..], out long ms))
                            seconds = ms / 1_000_000.0;
                        else if (line.StartsWith("out_time_us=", StringComparison.Ordinal)
                            && long.TryParse(line["out_time_us=".Length..], out long us))
                            seconds = us / 1_000_000.0;

                        if (seconds.HasValue && totalSeconds > 0 && progress is not null)
                            progress.Report(Math.Clamp(seconds.Value / totalSeconds, 0, 1));
                    }
                }, ct);

                var stderrTask = Task.Run(async () =>
                {
                    string? line;
                    while ((line = await process.StandardError.ReadLineAsync()) != null)
                    {
                        lock (stderrTail)
                        {
                            stderrTail.Enqueue(line);
                            while (stderrTail.Count > 20) stderrTail.Dequeue();
                        }
                        // Fallback if -progress lines are missing.
                        if (totalSeconds > 0 && progress is not null)
                        {
                            var s = ParseFfmpegTimeSeconds(line);
                            if (s.HasValue) progress.Report(Math.Clamp(s.Value / totalSeconds, 0, 1));
                        }
                    }
                }, ct);

                await process.WaitForExitAsync(ct);
                await Task.WhenAll(stdoutTask, stderrTask);

                if (process.ExitCode != 0)
                {
                    string tail;
                    lock (stderrTail) tail = string.Join(" ", stderrTail.TakeLast(3));
                    try { if (File.Exists(outputPath)) File.Delete(outputPath); } catch { /* best effort */ }
                    throw new InvalidOperationException(
                        string.IsNullOrWhiteSpace(tail) ? "ffmpeg failed." : $"ffmpeg failed: {TrimStatus(tail, 80)}");
                }
            }

            progress?.Report(1.0);
            return outputPath;
        }
    }
}
