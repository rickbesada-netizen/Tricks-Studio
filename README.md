# Tricks Studio

Free Windows desktop video editor preview.

**[Download TricksStudioSetup.exe](https://github.com/rickbesada-netizen/Tricks-Studio/releases/download/windows-preview-45108897392/TricksStudioSetup.exe)**

Run the installer on Windows x64. It adds a Start menu shortcut and can open the app when setup finishes. If FFmpeg is missing, install it in Windows Terminal with `winget install --id Gyan.FFmpeg --exact` and reopen Tricks Studio for video import and export.

This installer uses a compiled Windows editor and Inno Setup. It passed installation, app startup, and a custom Microsoft Defender scan on GitHub’s Windows runner. The installer is unsigned. If Windows Security blocks it, leave it blocked.

This preview includes clip import, playback, trimming, splitting, clip ordering, audio muting, project save and open, and 1080p MP4 export.
