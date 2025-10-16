!ifndef SourceDir
!define SourceDir "."
!endif

!ifndef AppIcon
!define AppIcon "${SourceDir}/Assets/AppIcon.ico"
!endif

!ifndef InstallerOut
!define InstallerOut "SnapDescribeSetup.exe"
!endif

!define PRODUCT_NAME "SnapDescribe"
!define PRODUCT_VERSION "${InstallVersion}"
!define PRODUCT_PUBLISHER "SnapDescribe"

OutFile "${InstallerOut}"
InstallDir "$PROGRAMFILES64\SnapDescribe"
RequestExecutionLevel highest
Icon "${AppIcon}"
UninstallIcon "${AppIcon}"

Page directory
Page instfiles
UninstPage uninstConfirm
UninstPage instfiles

Section "Install"
  nsExec::ExecToStack "\"${SourceDir}\SnapDescribe.exe\" --shutdown"
  Pop $0
  Pop $1
  Sleep 700

  nsExec::ExecToStack "taskkill /im SnapDescribe.exe /F"
  Pop $0 ; exit code
  Pop $1 ; output (ignored)
  Sleep 300

  SetOutPath "$INSTDIR"
  File "${SourceDir}/SnapDescribe.exe"

  ; optional debugging symbols
  IfFileExists "${SourceDir}/SnapDescribe.pdb" 0 +2
    File "${SourceDir}/SnapDescribe.pdb"

  SetOutPath "$INSTDIR\tessdata"
  File "${SourceDir}/tessdata/eng.traineddata"
  File "${SourceDir}/tessdata/chi_sim.traineddata"

  IfFileExists "${SourceDir}/x64/*.*" 0 +3
    SetOutPath "$INSTDIR\x64"
    File /r "${SourceDir}/x64/*"

  IfFileExists "${SourceDir}/x86/*.*" 0 +3
    SetOutPath "$INSTDIR\x86"
    File /r "${SourceDir}/x86/*"

  SetOutPath "$INSTDIR"
  CreateShortcut "$DESKTOP\SnapDescribe.lnk" "$INSTDIR\SnapDescribe.exe"

  WriteUninstaller "$INSTDIR\Uninstall.exe"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}" "DisplayName" "${PRODUCT_NAME}"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}" "DisplayIcon" "$INSTDIR\SnapDescribe.exe"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}" "Publisher" "${PRODUCT_PUBLISHER}"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}" "DisplayVersion" "${PRODUCT_VERSION}"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}" "UninstallString" "$INSTDIR\Uninstall.exe"
  Exec "$INSTDIR\SnapDescribe.exe"
SectionEnd

Section "Uninstall"
  Delete "$DESKTOP\SnapDescribe.lnk"
  DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}"

  RMDir /r "$INSTDIR"

  StrCpy $0 "$APPDATA\SnapDescribe"
  IfFileExists "$0\*.*" 0 +2
    RMDir /r "$0"

  StrCpy $1 "$LOCALAPPDATA\SnapDescribe"
  IfFileExists "$1\*.*" 0 +2
    RMDir /r "$1"
SectionEnd
