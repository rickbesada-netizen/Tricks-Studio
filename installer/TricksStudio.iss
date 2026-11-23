[Setup]
AppId=TricksStudio.Desktop
AppName=Tricks Studio
AppVersion=0.3.0
DefaultDirName={localappdata}\Programs\Tricks Studio
DefaultGroupName=Tricks Studio
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\out
OutputBaseFilename=TricksStudioSetup
Compression=lzma2
SolidCompression=yes
UninstallDisplayIcon={app}\TricksStudio.exe
WizardStyle=modern

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\Tricks Studio"; Filename: "{app}\TricksStudio.exe"

[Run]
Filename: "{app}\TricksStudio.exe"; Description: "Open Tricks Studio"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
var
  Winget: String;
  ResultCode: Integer;
begin
  if (CurStep = ssPostInstall) and (not WizardSilent) then begin
    Winget := ExpandConstant('{localappdata}\Microsoft\WindowsApps\winget.exe');
    if FileExists(Winget) then begin
      WizardForm.StatusLabel.Caption := 'Setting up FFmpeg video tools...';
      Exec(Winget, 'install --id Gyan.FFmpeg --exact --source winget --scope user', '', SW_SHOWNORMAL, ewWaitUntilTerminated, ResultCode);
    end;
  end;
end;
