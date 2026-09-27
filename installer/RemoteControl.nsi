Unicode true
!include "MUI2.nsh"
!include "x64.nsh"
!include "LogicLib.nsh"

!ifndef VERSION
  !define VERSION "1.1.0"
!endif
!ifndef PUBLISH
  !error "PUBLISH must point to the self-contained publish directory"
!endif
!ifndef PAYLOAD
  !error "PAYLOAD must point to verified FakerInput files"
!endif
!ifndef OUTPUT
  !error "OUTPUT must point to the installer exe"
!endif

Name "RemoteControl ${VERSION}"
OutFile "${OUTPUT}"
InstallDir "$PROGRAMFILES64\RemoteControl"
InstallDirRegKey HKLM "Software\bhome-lab\RemoteControl" "InstallDir"
RequestExecutionLevel admin
SetCompressor /SOLID zlib
ShowInstDetails show
ShowUninstDetails show
BrandingText "bhome-lab RemoteControl"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_ICONSTOP "RemoteControl requires 64-bit Windows."
    Abort
  ${EndIf}
  SetRegView 64
FunctionEnd

Section "RemoteControl" SEC_MAIN
  SetRegView 64
  InitPluginsDir
  SetOutPath "$PLUGINSDIR"
  File "${__FILEDIR__}\Stop-Existing-Service.ps1"
  nsExec::ExecToLog '"$WINDIR\Sysnative\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "$PLUGINSDIR\Stop-Existing-Service.ps1"'
  Pop $0
  ${If} $0 != 0
    MessageBox MB_ICONSTOP "Could not stop RemoteControlSvc before update (exit $0)."
    Abort
  ${EndIf}

  SetOutPath "$PLUGINSDIR"
  File "${PAYLOAD}\FakerInput_Setup_0.1.1_x64.msi"
  DetailPrint "Installing signed FakerInput 0.1.1..."
  nsExec::ExecToLog '"$WINDIR\Sysnative\msiexec.exe" /i "$PLUGINSDIR\FakerInput_Setup_0.1.1_x64.msi" /qn /norestart'
  Pop $0
  ${If} $0 != 0
  ${AndIf} $0 != 3010
    MessageBox MB_ICONSTOP "FakerInput installation failed (exit $0)."
    Abort
  ${EndIf}

  SetOutPath "$INSTDIR"
  File /r /x "*.pdb" /x "install-service.ps1" "${PUBLISH}\*"
  File "${PAYLOAD}\FakerInputWrapper.dll"
  File "${PAYLOAD}\FakerInputDll.dll"

  nsExec::ExecToLog '"$WINDIR\Sysnative\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "$INSTDIR\Manage-Service.ps1" -Action Install -InstallDir "$INSTDIR"'
  Pop $0
  ${If} $0 != 0
    MessageBox MB_ICONSTOP "RemoteControlSvc setup failed (exit $0)."
    Abort
  ${EndIf}

  WriteUninstaller "$INSTDIR\Uninstall.exe"
  WriteRegStr HKLM "Software\bhome-lab\RemoteControl" "InstallDir" "$INSTDIR"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\RemoteControl" "DisplayName" "RemoteControl"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\RemoteControl" "DisplayVersion" "${VERSION}"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\RemoteControl" "Publisher" "bhome-lab"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\RemoteControl" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\RemoteControl" "NoModify" 1
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\RemoteControl" "NoRepair" 1

  DetailPrint "Running installation diagnostics..."
  nsExec::ExecToStack '"$WINDIR\Sysnative\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "$INSTDIR\Test-Installation.ps1" -InstallDir "$INSTDIR" -ReportPath "$INSTDIR\install-diagnostics.json"'
  Pop $0
  Pop $1
  FileOpen $2 "$INSTDIR\install-diagnostics.log" w
  FileWrite $2 "$1"
  FileClose $2
  ${If} $0 != 0
    MessageBox MB_ICONEXCLAMATION "RemoteControl was installed, but diagnostics found a problem. Run Test-Installation.ps1 after checking the log."
    SetErrorLevel 1
  ${EndIf}
SectionEnd

Section "Uninstall"
  SetRegView 64
  nsExec::ExecToLog '"$WINDIR\Sysnative\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "$INSTDIR\Manage-Service.ps1" -Action Uninstall -InstallDir "$INSTDIR"'
  Pop $0
  ${If} $0 != 0
    MessageBox MB_ICONSTOP "Could not remove RemoteControlSvc (exit $0)."
    Abort
  ${EndIf}
  DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\RemoteControl"
  DeleteRegKey HKLM "Software\bhome-lab\RemoteControl"
  RMDir /r "$INSTDIR"
  ; FakerInput is shared with other applications and is intentionally retained.
SectionEnd
