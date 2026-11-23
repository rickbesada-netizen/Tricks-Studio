TRICKS STUDIO | Windows desktop preview

Requirements
  Windows 10 or 11 on an x64 PC, with Windows PowerShell 5.1 and WPF.
  FFmpeg and FFprobe are used for clip inspection and MP4 export.

Start editing
  1. Extract Tricks-Studio-Windows.zip.
  2. Install FFmpeg. If you use Windows Package Manager, run this command
     in Terminal:
       winget install --id Gyan.FFmpeg --exact
     Open a new Windows session after installing if Windows has not picked
     up the updated PATH. Alternatively, place ffmpeg.exe and ffprobe.exe
     beside TricksStudio.exe.
  3. Open TricksStudio.exe. Click Install app to add a Start menu shortcut.
  4. Add video clips. Select a clip to scrub it, adjust its source in/out
     points, toggle its audio, or split it at the current position.
  5. Save a .fcstudio project or export the sequence to a 1080p MP4.

Project files reference the original videos by path. Keep them in place
when you reopen a saved project. Preview playback uses Windows media
codecs. MP4 export uses the codecs available in your FFmpeg build.
The launcher is unsigned. This preview has one sequential video track.

Source
  source/editor.ps1 contains the native WPF editor.
  source/launcher.c and source/build.py build the standalone launcher
  on Linux with Python 3, GCC and GNU binutils.
