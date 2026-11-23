param([string]$Mode='Editor')
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase
function Find-Tool([string]$name){
  foreach($dir in @((Split-Path $env:TRICKS_STUDIO_EXE),(Join-Path $env:LOCALAPPDATA 'Tricks Studio'),(Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Links'))){
    $candidate=Join-Path $dir ($name+'.exe')
    if(Test-Path -LiteralPath $candidate -PathType Leaf){return $candidate}
  }
  $command=Get-Command ($name+'.exe') -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
  if($command){return $command.Source}
}
$installDir=Join-Path $env:LOCALAPPDATA 'Tricks Studio'
$installedExe=Join-Path $installDir 'TricksStudio.exe'
$runningExe=[IO.Path]::GetFullPath($env:TRICKS_STUDIO_EXE)
if($Mode -eq 'Install' -or ($Mode -eq 'Auto' -and $runningExe -ne $installedExe)){
  try {
    [void](New-Item -ItemType Directory -Force -Path $installDir)
    if($runningExe -ne $installedExe){Copy-Item -LiteralPath $runningExe -Destination $installedExe -Force}
    $shortcut=(New-Object -ComObject WScript.Shell).CreateShortcut((Join-Path ([Environment]::GetFolderPath('Programs')) 'Tricks Studio.lnk'))
    $shortcut.TargetPath=$installedExe
    $shortcut.WorkingDirectory=$installDir
    $shortcut.Save()
  } catch {
    [void][Windows.MessageBox]::Show("Setup could not install Tricks Studio. Close any running copy and try again.`n`n$($_.Exception.Message)",'Tricks Studio Setup')
    return
  }
  if(!(Find-Tool 'ffmpeg') -or !(Find-Tool 'ffprobe')){
    $winget=Get-Command winget.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if($winget){
      try {
        Start-Process -FilePath $winget.Source -ArgumentList @('install','--id','Gyan.FFmpeg','--exact','--source','winget','--scope','user') -Wait
      } catch {
        # Keep the installed editor available even if the dependency install fails.
      }
    }
    if(!(Find-Tool 'ffmpeg') -or !(Find-Tool 'ffprobe')){
      [void][Windows.MessageBox]::Show('Tricks Studio is installed. FFmpeg setup did not complete. To enable video import and export, install Gyan.FFmpeg with WinGet, or place ffmpeg.exe and ffprobe.exe in your Tricks Studio installation folder.','Tricks Studio Setup')
    }
  }
  Start-Process -FilePath $installedExe
  return
}
[xml]$xaml=@'
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Title="Tricks Studio" Width="1120" Height="750" MinWidth="850" MinHeight="640" Background="#151B20" Foreground="#EDF2F0" FontFamily="Segoe UI">
  <Window.Resources>
    <Style TargetType="Button"><Setter Property="Margin" Value="0,0,8,0"/><Setter Property="Padding" Value="11,7"/><Setter Property="Background" Value="#D9EE9B"/><Setter Property="Foreground" Value="#1A2218"/><Setter Property="BorderThickness" Value="0"/></Style>
    <Style TargetType="TextBox"><Setter Property="Margin" Value="0,4,0,12"/><Setter Property="Padding" Value="6"/><Setter Property="Background" Value="#151B20"/><Setter Property="Foreground" Value="#EDF2F0"/><Setter Property="BorderBrush" Value="#61716C"/></Style>
  </Window.Resources>
  <DockPanel Margin="18">
    <StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Margin="0,0,0,14">
      <TextBlock Text="Tricks Studio" FontSize="23" FontWeight="SemiBold" Margin="0,0,24,0" VerticalAlignment="Center"/>
      <Button Name="Import" Content="Add video"/><Button Name="Open" Content="Open project"/><Button Name="Save" Content="Save project"/><Button Name="Undo" Content="Undo"/><Button Name="Export" Content="Export MP4"/>
    </StackPanel>
    <TextBlock Name="Status" DockPanel.Dock="Bottom" Foreground="#CDDDBE" Margin="0,12,0,0" TextWrapping="Wrap" Text="Add a video to start editing."/>
    <Grid>
      <Grid.RowDefinitions><RowDefinition Height="*"/><RowDefinition Height="200"/></Grid.RowDefinitions>
      <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="235"/></Grid.ColumnDefinitions>
      <DockPanel Background="#0B1114" Margin="0,0,12,12">
        <StackPanel DockPanel.Dock="Bottom" Margin="10">
          <Slider Name="Seek" Minimum="0" Maximum="1"/>
          <StackPanel Orientation="Horizontal" Margin="0,7,0,0"><Button Name="Play" Content="Play sequence"/><Button Name="Pause" Content="Pause"/><TextBlock Name="Time" VerticalAlignment="Center" Text="0.00 s"/></StackPanel>
        </StackPanel>
        <MediaElement Name="Preview" LoadedBehavior="Manual" UnloadedBehavior="Stop" Stretch="Uniform"/>
      </DockPanel>
      <StackPanel Grid.Column="1" Background="#233038" Margin="0,0,0,12">
        <StackPanel Margin="14">
          <TextBlock Text="Clip settings" FontSize="17" FontWeight="SemiBold" Margin="0,0,0,14"/>
          <TextBlock Text="Source in (seconds)"/><TextBox Name="InTime"/>
          <TextBlock Text="Source out (seconds)"/><TextBox Name="OutTime"/>
          <CheckBox Name="Mute" Content="Mute clip audio" Foreground="#EDF2F0" Margin="0,0,0,14"/>
          <Button Name="Apply" Content="Apply clip settings" HorizontalAlignment="Left"/>
        </StackPanel>
      </StackPanel>
      <DockPanel Grid.Row="1" Grid.ColumnSpan="2" Background="#233038">
        <StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Margin="12">
          <TextBlock Text="Sequence" FontSize="17" FontWeight="SemiBold" VerticalAlignment="Center" Margin="0,0,20,0"/>
          <Button Name="Split" Content="Split at playhead"/><Button Name="Earlier" Content="Move earlier"/><Button Name="Later" Content="Move later"/><Button Name="Remove" Content="Remove clip"/>
        </StackPanel>
        <ListBox Name="Clips" Background="#151B20" Foreground="#EDF2F0" BorderBrush="#61716C" Margin="12,0,12,12"/>
      </DockPanel>
    </Grid>
  </DockPanel>
</Window>
'@
$window=[Windows.Markup.XamlReader]::Load((New-Object System.Xml.XmlNodeReader $xaml))
foreach($name in 'Import','Open','Save','Undo','Export','Status','Preview','Seek','Time','Play','Pause','InTime','OutTime','Mute','Apply','Clips','Split','Earlier','Later','Remove'){
  Set-Variable -Scope Script -Name $name -Value $window.FindName($name)
}
$script:sequence=[Collections.Generic.List[object]]::new()
$script:history=[Collections.Generic.Stack[string]]::new()
$script:playing=$false
$script:pending=$null
$script:syncing=$false
$script:exportJob=$null
$culture=[Globalization.CultureInfo]::InvariantCulture
function Tell([string]$message){$Status.Text=$message}
function Number([double]$value){$value.ToString('0.###',$culture)}
function Snapshot{$history.Push((ConvertTo-Json -InputObject @($sequence.ToArray()) -Depth 4 -Compress))}
function Load-Sequence($items){$sequence.Clear();foreach($entry in @($items)){if($null -ne $entry){$sequence.Add($entry)}}}
function View-Clip($clip,[double]$position){
  $script:syncing=$true
  $Seek.Minimum=$clip.Start
  $Seek.Maximum=$clip.End
  $Seek.Value=$position
  $script:syncing=$false
  $Time.Text="$(Number $position) s"
  $Preview.IsMuted=[bool]$clip.Mute
  $uri=[Uri]::new([string]$clip.Path)
  if($Preview.Source -ne $uri){$script:pending=$position;$Preview.Source=$uri;$Preview.Play()}
  else{$script:pending=$null;$Preview.Position=[TimeSpan]::FromSeconds($position);if($playing){$Preview.Play()}else{$Preview.Pause()}}
}
function Refresh([int]$index){
  $script:playing=$false
  $Preview.Pause()
  $Clips.Items.Clear()
  $offset=0.0
  foreach($clip in $sequence){
    $label=[IO.Path]::GetFileName($clip.Path)
    $length=$clip.End-$clip.Start
    [void]$Clips.Items.Add(('{0:0.00}s   {1}   [{2:0.00}s]' -f $offset,$label,$length))
    $offset+=$length
  }
  if($sequence.Count -gt 0){$Clips.SelectedIndex=[Math]::Max(0,[Math]::Min($index,$sequence.Count-1))}
  else{$Preview.Close();$Preview.Source=$null;$InTime.Clear();$OutTime.Clear();$Mute.IsChecked=$false;$Time.Text='0.00 s'}
  $Undo.IsEnabled=$history.Count -gt 0
}
function Selected-Clip{if($Clips.SelectedIndex -ge 0){$sequence[$Clips.SelectedIndex]}}
function Pick-File([bool]$saveDialog,[string]$filter,[string]$suggested=''){
  $dialog=if($saveDialog){New-Object Microsoft.Win32.SaveFileDialog}else{New-Object Microsoft.Win32.OpenFileDialog}
  $dialog.Filter=$filter
  $dialog.FileName=$suggested
  if($dialog.ShowDialog($window)){return $dialog.FileName}
}
$Clips.Add_SelectionChanged({
  $clip=Selected-Clip
  if($null -eq $clip){return}
  $InTime.Text=Number $clip.Start
  $OutTime.Text=Number $clip.End
  $Mute.IsChecked=[bool]$clip.Mute
  View-Clip $clip $clip.Start
})
$Preview.Add_MediaOpened({
  if($null -ne $pending){$Preview.Position=[TimeSpan]::FromSeconds($pending);$script:pending=$null}
  if(!$playing){$Preview.Pause()}
})
$Preview.Add_MediaFailed({param($sender,$event);$script:playing=$false;Tell "Windows could not preview this clip. Check the file and its codec."})
function Advance{
  if(!$playing){return}
  if($Clips.SelectedIndex+1 -lt $sequence.Count){$Clips.SelectedIndex++}
  else{$script:playing=$false;$Preview.Pause()}
}
$Preview.Add_MediaEnded({Advance})
$timer=[Windows.Threading.DispatcherTimer]::new()
$timer.Interval=[TimeSpan]::FromMilliseconds(70)
$timer.Add_Tick({
  $clip=Selected-Clip
  if($playing -and $null -ne $clip -and $null -eq $pending){
    $position=$Preview.Position.TotalSeconds
    if($position -ge $clip.End){Advance}
    else{$script:syncing=$true;$Seek.Value=$position;$script:syncing=$false;$Time.Text="$(Number $position) s"}
  }
  if($null -ne $exportJob -and $exportJob.Process.HasExited){
    if($exportJob.Process.ExitCode -eq 0 -and (Test-Path -LiteralPath $exportJob.TempOutput)){
      try{Move-Item -LiteralPath $exportJob.TempOutput -Destination $exportJob.Output -Force;Tell "Exported $($exportJob.Output)."}
      catch{Tell "Could not write the export. Rendered file: $($exportJob.TempOutput)";$exportJob.Keep=$true}
    }else{Tell "Export failed. Check media formats or the export log at $($exportJob.Log).";$exportJob.Keep=$true}
    if(!$exportJob.Keep){Remove-Item -LiteralPath $exportJob.Dir -Recurse -Force}
    $exportJob.Process.Dispose()
    $script:exportJob=$null
    $Export.IsEnabled=$true
  }
})
$timer.Start()
$Seek.Add_ValueChanged({if(!$syncing -and $null -ne $Preview.Source){$Preview.Position=[TimeSpan]::FromSeconds($Seek.Value);$Time.Text="$(Number $Seek.Value) s"}})
$Play.Add_Click({if($Clips.SelectedIndex -ge 0){$script:playing=$true;$clip=Selected-Clip;if($Preview.Position.TotalSeconds -ge $clip.End-.04){View-Clip $clip $clip.Start};$Preview.Play()}})
$Pause.Add_Click({$script:playing=$false;$Preview.Pause()})
function Inspect([string]$path){
  $probe=Find-Tool 'ffprobe'
  if(!$probe){throw 'Place ffprobe.exe and ffmpeg.exe beside TricksStudio.exe or add them to your Windows PATH.'}
  $raw=& $probe -v error -show_format -show_streams -of json $path | Out-String
  if($LASTEXITCODE -ne 0){throw 'FFprobe could not read the selected file.'}
  $meta=ConvertFrom-Json $raw
  $video=@($meta.streams | Where-Object {$_.codec_type -eq 'video' -and !$_.disposition.attached_pic})
  if(!$video.Count){throw 'The selected file has no video stream.'}
  $duration=[double]::Parse([string]$meta.format.duration,$culture)
  if($duration -le 0 -or [double]::IsInfinity($duration) -or [double]::IsNaN($duration)){throw 'The selected file has no usable duration.'}
  return [pscustomobject]@{Path=$path;Start=0.0;End=$duration;Duration=$duration;Video=[int]$video[0].index;Audio=(@($meta.streams | Where-Object codec_type -eq 'audio').Count -gt 0);Mute=$false}
}
$Import.Add_Click({
  $path=Pick-File $false 'Video files|*.mp4;*.mov;*.mkv;*.avi;*.webm;*.wmv|All files|*.*'
  if(!$path){return}
  try{$clip=Inspect $path;Snapshot;$sequence.Add($clip);Refresh ($sequence.Count-1);Tell 'Video added.'}catch{Tell $_.Exception.Message}
})
$Apply.Add_Click({
  $clip=Selected-Clip
  if(!$clip){return}
  try{
    $start=[double]::Parse($InTime.Text,$culture);$finish=[double]::Parse($OutTime.Text,$culture)
    if([double]::IsNaN($start) -or [double]::IsNaN($finish) -or $start -lt 0 -or $finish -gt $clip.Duration+.001 -or $finish-$start -lt .05){throw 'Enter a valid clip range in seconds.'}
    Snapshot
    $clip.Start=$start;$clip.End=[Math]::Min($clip.Duration,$finish);$clip.Mute=$Mute.IsChecked
    Refresh $Clips.SelectedIndex
  }catch{Tell $_.Exception.Message}
})
$Split.Add_Click({
  $clip=Selected-Clip
  if(!$clip){return}
  $point=$Seek.Value
  if($point-$clip.Start -lt .05 -or $clip.End-$point -lt .05){Tell 'Move the playhead inside the selected clip.';return}
  Snapshot
  $next=$clip.PSObject.Copy()
  $next.Start=$point
  $clip.End=$point
  $index=$Clips.SelectedIndex+1
  $sequence.Insert($index,$next)
  Refresh $index
})
function Move-Clip([int]$delta){
  $index=$Clips.SelectedIndex;$destination=$index+$delta
  if($index -lt 0 -or $destination -lt 0 -or $destination -ge $sequence.Count){return}
  Snapshot
  $clip=$sequence[$index];$sequence.RemoveAt($index);$sequence.Insert($destination,$clip);Refresh $destination
}
$Earlier.Add_Click({Move-Clip -1});$Later.Add_Click({Move-Clip 1})
$Remove.Add_Click({$index=$Clips.SelectedIndex;if($index -ge 0){Snapshot;$sequence.RemoveAt($index);Refresh $index}})
$Undo.Add_Click({if($history.Count){Load-Sequence (ConvertFrom-Json ($history.Pop()));Refresh 0}})
$Save.Add_Click({
  $path=Pick-File $true 'Tricks Studio desktop project|*.fcstudio' 'Untitled.fcstudio'
  if($path){try{@{version=1;clips=@($sequence.ToArray())} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $path -Encoding UTF8;Tell 'Project saved. Keep the original videos in their current locations.'}catch{Tell $_.Exception.Message}}
})
$Open.Add_Click({
  $path=Pick-File $false 'Tricks Studio desktop project|*.fcstudio'
  if(!$path){return}
  try{
    $project=Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    if($project.version -ne 1){throw 'Unsupported project format.'}
    $validated=@(foreach($entry in $project.clips){
      if(!(Test-Path -LiteralPath $entry.Path -PathType Leaf)){throw "Missing media: $($entry.Path)"}
      $clip=Inspect $entry.Path
      if($entry.Start -lt 0 -or $entry.End -gt $clip.Duration+.001 -or $entry.End-$entry.Start -lt .05){throw 'Invalid trim range in project.'}
      $clip.Start=[double]$entry.Start;$clip.End=[double]$entry.End;$clip.Mute=[bool]$entry.Mute
      $clip
    })
    Snapshot;Load-Sequence $validated;Refresh 0;Tell 'Project opened.'
  }catch{Tell $_.Exception.Message}
})
function Quote-Arg([string]$arg){'"'+$arg.Replace('"','\"')+'"'}
$Export.Add_Click({
  if(!$sequence.Count){Tell 'Add at least one video before exporting.';return}
  $ffmpeg=Find-Tool 'ffmpeg'
  if(!$ffmpeg){Tell 'Place ffmpeg.exe beside TricksStudio.exe or add it to your Windows PATH.';return}
  $path=Pick-File $true 'MP4 video|*.mp4' 'TricksStudio.mp4'
  if(!$path){return}
  if(@($sequence | Where-Object {$_.Path -eq $path}).Count){Tell 'Choose a different path from your source videos.';return}
  $dir=Join-Path ([IO.Path]::GetDirectoryName($path)) ('.tricks-studio-'+[Guid]::NewGuid())
  try{
    [void](New-Item -ItemType Directory -Path $dir)
    $argsList=[Collections.Generic.List[string]]::new()
    $argsList.AddRange([string[]]@('-hide_banner','-nostdin','-y'))
    $filters=[Collections.Generic.List[string]]::new()
    $concat=''
    for($i=0;$i -lt $sequence.Count;$i++){
      $clip=$sequence[$i];$length=Number ($clip.End-$clip.Start);$start=Number $clip.Start
      $argsList.AddRange([string[]]@('-ss',$start,'-t',$length,'-i',$clip.Path))
      $filters.Add(('[{0}:{1}]setpts=PTS-STARTPTS,scale=1920:1080:force_original_aspect_ratio=decrease:force_divisible_by=2,pad=1920:1080:(ow-iw)/2:(oh-ih)/2,setsar=1,fps=30,format=yuv420p[v{0}]' -f $i,$clip.Video))
      if($clip.Audio -and !$clip.Mute){$filters.Add(('[{0}:a:0]asetpts=PTS-STARTPTS,aresample=48000,aformat=channel_layouts=stereo,apad,atrim=duration={1}[a{0}]' -f $i,$length))}
      else{$filters.Add(('anullsrc=r=48000:cl=stereo,atrim=duration={1}[a{0}]' -f $i,$length))}
      $concat+="[v$i][a$i]"
    }
    $filters.Add("${concat}concat=n=$($sequence.Count):v=1:a=1[v][a]")
    $filterPath=Join-Path $dir 'filters.txt'
    [IO.File]::WriteAllText($filterPath,($filters -join ';'))
    $output=Join-Path $dir 'render.mp4'
    $log=Join-Path $dir 'export.log'
    $argsList.AddRange([string[]]@('-filter_complex_script',$filterPath,'-map','[v]','-map','[a]','-c:v','libx264','-preset','fast','-crf','20','-c:a','aac','-movflags','+faststart',$output))
    $process=Start-Process -FilePath $ffmpeg -ArgumentList (($argsList | ForEach-Object {Quote-Arg $_}) -join ' ') -WindowStyle Hidden -RedirectStandardError $log -PassThru
    $script:exportJob=@{Process=$process;Output=$path;TempOutput=$output;Dir=$dir;Log=$log;Keep=$false}
    $Export.IsEnabled=$false
    Tell 'Rendering 1080p MP4. Keep Tricks Studio open until export finishes.'
  }catch{if(Test-Path -LiteralPath $dir){Remove-Item -LiteralPath $dir -Recurse -Force};Tell $_.Exception.Message}
})
$window.Add_Closing({param($sender,$event);if($null -ne $exportJob){$event.Cancel=$true;Tell 'Wait for the current export to finish before closing.'}})
Refresh 0
[void]$window.ShowDialog()
$timer.Stop()
