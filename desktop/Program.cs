using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace TricksStudio;

public sealed record Clip(string Path, double Start, double End, double Duration, int Video, bool Audio, bool Mute);
public sealed record Project(int Version, Clip[] Clips);

public sealed class Editor : Window
{
    private readonly List<Clip> clips = [];
    private readonly Stack<Clip[]> history = new();
    private readonly MediaElement preview = new() { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Close, Stretch = Stretch.Uniform };
    private readonly ListBox sequence = new() { Height = 140 };
    private readonly TextBox start = new() { Margin = new Thickness(4), Width = 80 };
    private readonly TextBox end = new() { Margin = new Thickness(4), Width = 80 };
    private readonly CheckBox mute = new() { Content = "Mute clip", Margin = new Thickness(8) };
    private readonly Slider seek = new() { Margin = new Thickness(8) };
    private readonly TextBlock status = new() { Margin = new Thickness(8), TextWrapping = TextWrapping.Wrap };
    private bool playing, syncing, exporting;
    private double? pendingPosition;
    private Clip? Selected => sequence.SelectedIndex >= 0 && sequence.SelectedIndex < clips.Count ? clips[sequence.SelectedIndex] : null;
    private static string Seconds(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--smoke-test")
        {
            var editor = new Editor();
            editor.Show();
            editor.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            editor.Close();
            return;
        }
        new Application().Run(new Editor());
    }

    public Editor()
    {
        Title = "Tricks Studio";
        Width = 1250;
        Height = 800;
        MinWidth = 880;
        MinHeight = 620;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/TricksStudio;component/Theme.xaml", UriKind.Relative) });
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 14;
        Background = Ink("#101519");
        Foreground = Brushes.White;
        var layout = new DockPanel { Margin = new Thickness(14) };
        Content = layout;
        void AddDocked(UIElement element, Dock edge)
        {
            DockPanel.SetDock(element, edge);
            layout.Children.Add(element);
        }
        var header = new DockPanel { Margin = new Thickness(4, 0, 4, 12) };
        AddDocked(header, Dock.Top);
        var brand = Caption("◈   Tricks Studio", 22);
        brand.Foreground = Ink("#AAE8C1");
        header.Children.Add(brand);
        var toolbar = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        header.Children.Add(toolbar);
        AddButton(toolbar, "Open project", Open);
        AddButton(toolbar, "Save", Save);
        AddButton(toolbar, "Undo", () => { if (history.TryPop(out var previous)) { clips.Clear(); clips.AddRange(previous); Refresh(0); } });
        AddButton(toolbar, "Export MP4", async () => await Export());
        status.Foreground = Ink("#B8C7CC");
        AddDocked(status, Dock.Bottom);
        var timeline = new StackPanel();
        AddDocked(PanelBox(timeline), Dock.Bottom);
        var tools = new WrapPanel();
        timeline.Children.Add(tools);
        tools.Children.Add(Caption("Sequence", 17));
        AddButton(tools, "Split", Split);
        AddButton(tools, "Move earlier", () => Move(-1));
        AddButton(tools, "Move later", () => Move(1));
        AddButton(tools, "Remove", Remove);
        sequence.Height = 85;
        sequence.Background = Ink("#11191F");
        sequence.BorderThickness = new Thickness(0);
        var strip = new FrameworkElementFactory(typeof(StackPanel));
        strip.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        sequence.ItemsPanel = new ItemsPanelTemplate(strip);
        ScrollViewer.SetHorizontalScrollBarVisibility(sequence, ScrollBarVisibility.Auto);
        timeline.Children.Add(sequence);
        var inspector = new StackPanel { Width = 185 };
        AddDocked(PanelBox(inspector), Dock.Right);
        inspector.Children.Add(Caption("Clip inspector", 17));
        inspector.Children.Add(Caption("Source in · seconds"));
        inspector.Children.Add(start);
        inspector.Children.Add(Caption("Source out · seconds"));
        inspector.Children.Add(end);
        start.Width = end.Width = double.NaN;
        mute.Foreground = Brushes.White;
        inspector.Children.Add(mute);
        AddButton(inspector, "Apply trim", Apply);
        var sources = new StackPanel { Width = 180 };
        AddDocked(PanelBox(sources), Dock.Left);
        sources.Children.Add(Caption("Project media", 17));
        AddButton(sources, "+  Import video", async () => await Import());
        sources.Children.Add(new ScrollViewer { Content = sourceList, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 365 });
        var viewer = new DockPanel();
        layout.Children.Add(PanelBox(viewer));
        var title = Caption("Program monitor", 17);
        DockPanel.SetDock(title, Dock.Top);
        viewer.Children.Add(title);
        var playback = new WrapPanel();
        DockPanel.SetDock(playback, Dock.Bottom);
        viewer.Children.Add(playback);
        AddButton(playback, "▶  Play", Play);
        AddButton(playback, "Pause", Pause);
        playback.Children.Add(clock);
        DockPanel.SetDock(seek, Dock.Bottom);
        viewer.Children.Add(seek);
        var screen = new Grid { Background = Ink("#0A1014") };
        screen.Children.Add(preview);
        empty.HorizontalAlignment = HorizontalAlignment.Center;
        empty.VerticalAlignment = VerticalAlignment.Center;
        empty.Children.Add(Caption("Import footage to begin", 18));
        AddButton(empty, "Choose video…", async () => await Import());
        screen.Children.Add(empty);
        viewer.Children.Add(screen);
        status.Text = "Ready";
        seek.ValueChanged += (_, _) => clock.Text = TimeSpan.FromSeconds(seek.Value).ToString(@"hh\:mm\:ss\.ff");
        sequence.SelectionChanged += (_, _) => SelectClip();
        seek.ValueChanged += (_, _) => { if (!syncing && Selected is not null && pendingPosition is null) preview.Position = TimeSpan.FromSeconds(seek.Value); };
        preview.MediaOpened += (_, _) =>
        {
            if (pendingPosition is double value) preview.Position = TimeSpan.FromSeconds(value);
            pendingPosition = null;
            if (!playing) preview.Pause();
        };
        preview.MediaEnded += (_, _) => Advance();
        preview.MediaFailed += (_, _) =>
        {
            pendingPosition = null;
            playing = false;
            status.Text = "Windows could not preview this video. Check the file and its codec.";
        };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        timer.Tick += (_, _) =>
        {
            if (!playing || pendingPosition is not null || Selected is not { } clip) return;
            if (preview.Position.TotalSeconds >= clip.End) Advance();
            else
            {
                syncing = true;
                seek.Value = preview.Position.TotalSeconds;
                syncing = false;
            }
        };
        timer.Start();
        Closing += (_, e) => { if (exporting) e.Cancel = true; };
        Closed += (_, _) => { timer.Stop(); preview.Close(); };
    }

    private readonly StackPanel empty = new();
    private readonly StackPanel sourceList = new();
    private readonly TextBlock clock = Caption("00:00:00.00");
    private static Brush Ink(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;
    private static TextBlock Caption(string value, double size = 13) => new() { Text = value, FontSize = size, Foreground = Ink("#DFE9EC"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(6, 8, 8, 8), VerticalAlignment = VerticalAlignment.Center };
    private static Border PanelBox(UIElement content) => new() { Background = Ink("#1A2229"), BorderBrush = Ink("#303D46"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(10), Margin = new Thickness(3), Child = content };
    private void AddButton(Panel panel, string name, Action action)
    {
        var button = new Button { Content = name, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(2, 4, 4, 4) };
        if (name == "Export MP4" || name == "Choose video…") { button.Background = Ink("#AAE8C1"); button.Foreground = Ink("#10271C"); }
        button.Click += (_, _) => { try { action(); } catch (Exception e) { status.Text = e.Message; } };
        panel.Children.Add(button);
    }

    private void SelectClip()
    {
        if (Selected is not { } clip) return;
        syncing = true;
        seek.Minimum = clip.Start;
        seek.Maximum = clip.End;
        seek.Value = clip.Start;
        syncing = false;
        start.Text = Seconds(clip.Start);
        end.Text = Seconds(clip.End);
        mute.IsChecked = clip.Mute;
        preview.IsMuted = clip.Mute;
        var uri = new Uri(clip.Path);
        if (preview.Source != uri || pendingPosition is not null)
        {
            pendingPosition = clip.Start;
            preview.Source = uri;
            preview.Play();
        }
        else
        {
            preview.Position = TimeSpan.FromSeconds(clip.Start);
            if (playing) preview.Play();
            else preview.Pause();
        }
    }

    private void Refresh(int index)
    {
        Pause();
        sequence.Items.Clear();
        empty.Visibility = clips.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        sourceList.Children.Clear();
        foreach (var path in clips.Select(c => c.Path).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var item = new Button { Content = new TextBlock { Text = Path.GetFileName(path), TextTrimming = TextTrimming.CharacterEllipsis }, ToolTip = path, Padding = new Thickness(8), Margin = new Thickness(2,4,2,0) };
            item.Click += (_, _) => sequence.SelectedIndex = clips.FindIndex(c => c.Path == path);
            sourceList.Children.Add(item);
        }
        var offset = 0.0;
        foreach (var clip in clips)
        {
            var duration = clip.End - clip.Start;
            var tile = new StackPanel { Width = Math.Clamp(duration * 14, 75, 550) };
            tile.Children.Add(new TextBlock { Text = Path.GetFileName(clip.Path), FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            tile.Children.Add(new TextBlock { Text = $"{offset:0.##}s  ·  {duration:0.##}s", FontSize = 12 });
            sequence.Items.Add(tile);
            offset += clip.End - clip.Start;
        }
        if (clips.Count > 0) sequence.SelectedIndex = Math.Clamp(index, 0, clips.Count - 1);
        else
        {
            preview.Close();
            preview.Source = null;
            pendingPosition = null;
            start.Clear();
            end.Clear();
            mute.IsChecked = false;
        }
    }

    private void Play()
    {
        if (Selected is not { } clip) return;
        playing = true;
        if (pendingPosition is null && preview.Position.TotalSeconds >= clip.End)
            preview.Position = TimeSpan.FromSeconds(clip.Start);
        preview.Play();
    }

    private void Pause() { playing = false; preview.Pause(); }
    private void Advance()
    {
        if (!playing) return;
        if (sequence.SelectedIndex + 1 < clips.Count) sequence.SelectedIndex++;
        else Pause();
    }

    private void Snapshot() => history.Push(clips.ToArray());
    private void Apply()
    {
        if (Selected is not { } clip) return;
        if (!double.TryParse(start.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var first) ||
            !double.TryParse(end.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var last) ||
            !double.IsFinite(first) || !double.IsFinite(last) || first < 0 || last > clip.Duration + 0.000001 || last - first < .05)
            throw new InvalidOperationException("Enter valid source times in seconds.");
        Snapshot();
        var i = sequence.SelectedIndex;
        clips[i] = clip with { Start = first, End = Math.Min(last, clip.Duration), Mute = mute.IsChecked == true };
        Refresh(i);
    }
    private void Split()
    {
        if (Selected is not { } clip) return;
        var point = seek.Value;
        if (point - clip.Start < .05 || clip.End - point < .05) throw new InvalidOperationException("Move the playhead inside the clip.");
        Snapshot();
        var i = sequence.SelectedIndex;
        clips[i] = clip with { End = point };
        clips.Insert(i + 1, clip with { Start = point });
        Refresh(i + 1);
    }
    private void Move(int direction)
    {
        if (Selected is not { } clip) return;
        var from = sequence.SelectedIndex;
        var to = from + direction;
        if (to < 0 || to >= clips.Count) return;
        Snapshot();
        clips.RemoveAt(from);
        clips.Insert(to, clip);
        Refresh(to);
    }
    private void Remove()
    {
        if (Selected is null) return;
        Snapshot();
        var i = sequence.SelectedIndex;
        clips.RemoveAt(i);
        Refresh(i);
    }

    private static string Tool(string name)
    {
        var directories = new List<string> { AppContext.BaseDirectory, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Links") };
        directories.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));
        foreach (var directory in directories)
        {
            var candidate = Path.Combine(directory.Trim('"'), name + ".exe");
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }
        throw new InvalidOperationException($"Install FFmpeg with WinGet (package Gyan.FFmpeg), then reopen Tricks Studio. Missing: {name}.");
    }

    private static async Task<string> Run(string tool, IEnumerable<string> arguments)
    {
        var info = new ProcessStartInfo(Tool(tool)) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new InvalidOperationException($"Could not start {tool}.");
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new InvalidOperationException(await errors);
        return await output;
    }

    private async Task Import()
    {
        var dialog = new OpenFileDialog { Filter = "Video files|*.mp4;*.mov;*.mkv;*.webm;*.avi;*.wmv|All files|*.*" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var path = dialog.FileName;
            using var data = JsonDocument.Parse(await Run("ffprobe", ["-v", "error", "-show_format", "-show_streams", "-of", "json", path]));
            var streams = data.RootElement.GetProperty("streams").EnumerateArray().ToArray();
            var video = streams.FirstOrDefault(x => x.GetProperty("codec_type").GetString() == "video" &&
                !(x.TryGetProperty("disposition", out var disposition) && disposition.TryGetProperty("attached_pic", out var attached) && attached.GetInt32() == 1));
            if (video.ValueKind == JsonValueKind.Undefined) throw new InvalidOperationException("The file has no video stream.");
            if (!double.TryParse(data.RootElement.GetProperty("format").GetProperty("duration").GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var duration) || !double.IsFinite(duration) || duration <= 0)
                throw new InvalidOperationException("The file has no usable duration.");
            Snapshot();
            clips.Add(new Clip(path, 0, duration, duration, video.GetProperty("index").GetInt32(), streams.Any(x => x.GetProperty("codec_type").GetString() == "audio"), false));
            Refresh(clips.Count - 1);
            status.Text = "Video added.";
        }
        catch (Exception e) { status.Text = e.Message; }
    }

    private void Save()
    {
        var dialog = new SaveFileDialog { Filter = "Tricks Studio project|*.fcstudio", FileName = "Untitled.fcstudio" };
        if (dialog.ShowDialog(this) != true) return;
        File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(new Project(1, clips.ToArray()), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        status.Text = "Project saved. Keep source videos in their current locations.";
    }

    private void Open()
    {
        var dialog = new OpenFileDialog { Filter = "Tricks Studio project|*.fcstudio" };
        if (dialog.ShowDialog(this) != true) return;
        var project = JsonSerializer.Deserialize<Project>(File.ReadAllText(dialog.FileName), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (project is not { Version: 1, Clips: not null } || project.Clips.Any(c => c is null || !Path.IsPathFullyQualified(c.Path) || !File.Exists(c.Path) ||
            !double.IsFinite(c.Start) || !double.IsFinite(c.End) || !double.IsFinite(c.Duration) || c.Start < 0 || c.End > c.Duration + .001 || c.End <= c.Start || c.Video < 0))
            throw new InvalidOperationException("The project is invalid or its original media is missing.");
        Snapshot();
        clips.Clear();
        clips.AddRange(project.Clips);
        Refresh(0);
        status.Text = "Project opened.";
    }

    private async Task Export()
    {
        if (exporting || clips.Count == 0) return;
        var dialog = new SaveFileDialog { Filter = "MP4 video|*.mp4", FileName = "TricksStudio.mp4" };
        if (dialog.ShowDialog(this) != true) return;
        var snapshot = clips.ToArray();
        if (snapshot.Any(c => string.Equals(Path.GetFullPath(c.Path), dialog.FileName, StringComparison.OrdinalIgnoreCase))) { status.Text = "Choose a different path from the source video."; return; }
        var temporary = Path.Combine(Path.GetDirectoryName(dialog.FileName)!, ".tricks-" + Guid.NewGuid().ToString("N"));
        exporting = true;
        status.Text = "Rendering 1080p MP4.";
        try
        {
            Directory.CreateDirectory(temporary);
            var args = new List<string> { "-hide_banner", "-loglevel", "error", "-nostdin", "-y" };
            var filters = new List<string>();
            var inputs = new List<string>();
            for (var i = 0; i < snapshot.Length; i++)
            {
                var clip = snapshot[i];
                var length = Seconds(clip.End - clip.Start);
                args.AddRange(["-ss", Seconds(clip.Start), "-t", length, "-i", clip.Path]);
                filters.Add($"[{i}:{clip.Video}]setpts=PTS-STARTPTS,scale=1920:1080:force_original_aspect_ratio=decrease:force_divisible_by=2,pad=1920:1080:(ow-iw)/2:(oh-ih)/2,setsar=1,fps=30,format=yuv420p[v{i}]");
                filters.Add(clip.Audio && !clip.Mute ? $"[{i}:a:0]asetpts=PTS-STARTPTS,aresample=48000,aformat=channel_layouts=stereo,apad,atrim=duration={length}[a{i}]" : $"anullsrc=r=48000:cl=stereo,atrim=duration={length}[a{i}]");
                inputs.Add($"[v{i}][a{i}]");
            }
            filters.Add($"{string.Concat(inputs)}concat=n={snapshot.Length}:v=1:a=1[v][a]");
            var output = Path.Combine(temporary, "render.mp4");
            args.AddRange(["-filter_complex", string.Join(';', filters), "-map", "[v]", "-map", "[a]", "-c:v", "libx264", "-preset", "fast", "-crf", "20", "-c:a", "aac", "-movflags", "+faststart", output]);
            await Run("ffmpeg", args);
            File.Move(output, dialog.FileName, overwrite: true);
            status.Text = $"Exported {dialog.FileName}.";
        }
        catch (Exception e) { status.Text = e.Message; }
        finally
        {
            exporting = false;
            try { if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
