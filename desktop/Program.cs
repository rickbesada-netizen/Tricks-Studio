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
        Width = 1050;
        Height = 750;
        MinWidth = 830;
        MinHeight = 600;
        Background = new SolidColorBrush(Color.FromRgb(234, 240, 236));
        var layout = new DockPanel { Margin = new Thickness(16) };
        Content = layout;
        var toolbar = new WrapPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        layout.Children.Add(toolbar);
        AddButton(toolbar, "Add video", async () => await Import());
        AddButton(toolbar, "Open project", Open);
        AddButton(toolbar, "Save project", Save);
        AddButton(toolbar, "Undo", () =>
        {
            if (!history.TryPop(out var previous)) return;
            clips.Clear();
            clips.AddRange(previous);
            Refresh(0);
        });
        AddButton(toolbar, "Export MP4", async () => await Export());
        var editing = new StackPanel();
        DockPanel.SetDock(editing, Dock.Bottom);
        layout.Children.Add(editing);
        editing.Children.Add(seek);
        var controls = new WrapPanel();
        editing.Children.Add(controls);
        AddButton(controls, "Play", Play);
        AddButton(controls, "Pause", Pause);
        controls.Children.Add(new Label { Content = "Source in (s)" });
        controls.Children.Add(start);
        controls.Children.Add(new Label { Content = "Source out (s)" });
        controls.Children.Add(end);
        controls.Children.Add(mute);
        AddButton(controls, "Apply trim", Apply);
        var timeline = new WrapPanel();
        editing.Children.Add(timeline);
        AddButton(timeline, "Split", Split);
        AddButton(timeline, "Move earlier", () => Move(-1));
        AddButton(timeline, "Move later", () => Move(1));
        AddButton(timeline, "Remove clip", Remove);
        editing.Children.Add(sequence);
        editing.Children.Add(status);
        status.Text = "Add a video to begin.";
        layout.Children.Add(new Border { Background = Brushes.Black, Child = preview });
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

    private void AddButton(Panel panel, string name, Action action)
    {
        var button = new Button { Content = name, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(2, 4, 4, 4) };
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
        var offset = 0.0;
        foreach (var clip in clips)
        {
            sequence.Items.Add($"{offset:0.00}s    {Path.GetFileName(clip.Path)}    [{clip.End - clip.Start:0.00}s]");
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
