TRICKS STUDIO | Windows desktop preview

Download and run TricksStudioSetup.exe on Windows 10 or 11 (x64).
Setup installs Tricks Studio for your Windows account, adds a Start menu
shortcut, and opens the editor. If FFmpeg is missing, setup uses WinGet to
install it when available. WinGet may ask for input and needs internet access.

If FFmpeg setup does not complete, run this in Windows Terminal:
  winget install --id Gyan.FFmpeg --exact --source winget --scope user
Alternatively, place ffmpeg.exe and ffprobe.exe in:
  %LOCALAPPDATA%\Tricks Studio

This unsigned preview has one sequential video track. Project files link
to the original media. Keep those files in place when reopening a project.
