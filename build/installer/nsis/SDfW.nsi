; SDfW NSIS Installer Script
; Requires NSIS 3.11+

!include "MUI2.nsh"
!include "FileFunc.nsh"
!include "x64.nsh"
!include "WordFunc.nsh"

; --------------------------------
; General Configuration
; --------------------------------
!ifndef VERSION
  !define VERSION "0.0.0"
!endif

!ifndef PUBLISH_DIR
  !define PUBLISH_DIR "..\..\..\dist\publish"
!endif

!searchparse "${VERSION}" "" VERSION_NUMERIC "-"
!ifndef VERSION_NUMERIC
  !define VERSION_NUMERIC "${VERSION}"
!endif

Name "SDfW"
OutFile "..\..\..\dist\SDfW-${VERSION}-win-x64-Setup.exe"
InstallDir "$PROGRAMFILES64\SDfW"
InstallDirRegKey HKLM "Software\SDfW" "InstallPath"
RequestExecutionLevel admin
Unicode True

; --------------------------------
; Version Information
; --------------------------------
VIProductVersion "${VERSION_NUMERIC}.0"
VIAddVersionKey "ProductName" "SDfW"
VIAddVersionKey "CompanyName" "SDfW"
VIAddVersionKey "LegalCopyright" "SDfW"
VIAddVersionKey "FileDescription" "SDfW Installer"
VIAddVersionKey "FileVersion" "${VERSION}"
VIAddVersionKey "ProductVersion" "${VERSION}"

; --------------------------------
; Modern UI Configuration
; --------------------------------
!define MUI_ICON "..\..\..\src\Sdfw.Ui\Assets\app.ico"
!define MUI_UNICON "..\..\..\src\Sdfw.Ui\Assets\app.ico"
!define MUI_ABORTWARNING
!define MUI_WELCOMEFINISHPAGE_BITMAP "${NSISDIR}\Contrib\Graphics\Wizard\win.bmp"
!define MUI_UNWELCOMEFINISHPAGE_BITMAP "${NSISDIR}\Contrib\Graphics\Wizard\win.bmp"

; --------------------------------
; Pages
; --------------------------------
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_RUN "$INSTDIR\Sdfw.Ui.exe"
!define MUI_FINISHPAGE_RUN_TEXT "Launch SDfW"
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_WELCOME
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_UNPAGE_FINISH

; --------------------------------
; Languages
; --------------------------------
!insertmacro MUI_LANGUAGE "English"
!insertmacro MUI_LANGUAGE "PortugueseBR"

; --------------------------------
; Installer Section
; --------------------------------
Section "SDfW" SecMain
  SectionIn RO
  
  ; Check for 64-bit Windows
  ${IfNot} ${RunningX64}
    MessageBox MB_OK|MB_ICONSTOP "SDfW requires a 64-bit version of Windows."
    Abort
  ${EndIf}
  
  SetOutPath "$INSTDIR"
  
  ; Install all published files
  File /r "${PUBLISH_DIR}\*.*"
  
  ; Create uninstaller
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  
  ; Create Start Menu shortcuts
  CreateDirectory "$SMPROGRAMS\SDfW"
  CreateShortcut "$SMPROGRAMS\SDfW\SDfW.lnk" "$INSTDIR\Sdfw.Ui.exe" "" "$INSTDIR\Sdfw.Ui.exe" 0
  CreateShortcut "$SMPROGRAMS\SDfW\Uninstall SDfW.lnk" "$INSTDIR\Uninstall.exe" "" "$INSTDIR\Uninstall.exe" 0
  
  ; Create Desktop shortcut
  CreateShortcut "$DESKTOP\SDfW.lnk" "$INSTDIR\Sdfw.Ui.exe" "" "$INSTDIR\Sdfw.Ui.exe" 0
  
  ; Write registry keys for Add/Remove Programs
  WriteRegStr HKLM "Software\SDfW" "InstallPath" "$INSTDIR"
  WriteRegStr HKLM "Software\SDfW" "Version" "${VERSION}"
  
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\SDfW" "DisplayName" "SDfW"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\SDfW" "DisplayIcon" "$INSTDIR\Sdfw.Ui.exe"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\SDfW" "DisplayVersion" "${VERSION}"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\SDfW" "Publisher" "SDfW"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\SDfW" "UninstallString" "$\"$INSTDIR\Uninstall.exe$\""
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\SDfW" "QuietUninstallString" "$\"$INSTDIR\Uninstall.exe$\" /S"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\SDfW" "InstallLocation" "$INSTDIR"
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\SDfW" "NoModify" 1
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\SDfW" "NoRepair" 1
  
  ; Calculate and write estimated size
  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  IntFmt $0 "0x%08X" $0
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\SDfW" "EstimatedSize" "$0"
SectionEnd

; --------------------------------
; Uninstaller Section
; --------------------------------
Section "Uninstall"
  ; Stop the service if running (optional, in case there's a service component)
  ; nsExec::ExecToLog 'sc stop SdfwService'
  
  ; Remove Start Menu shortcuts
  Delete "$SMPROGRAMS\SDfW\SDfW.lnk"
  Delete "$SMPROGRAMS\SDfW\Uninstall SDfW.lnk"
  RMDir "$SMPROGRAMS\SDfW"
  
  ; Remove Desktop shortcut
  Delete "$DESKTOP\SDfW.lnk"
  
  ; Remove installed files
  RMDir /r "$INSTDIR"
  
  ; Remove registry keys
  DeleteRegKey HKLM "Software\SDfW"
  DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\SDfW"
SectionEnd

; --------------------------------
; Functions
; --------------------------------
Function .onInit
  ; Check if already installed and offer to uninstall first
  ReadRegStr $0 HKLM "Software\SDfW" "InstallPath"
  ${If} $0 != ""
    ${If} ${FileExists} "$0\Uninstall.exe"
      MessageBox MB_YESNO|MB_ICONQUESTION "SDfW is already installed. Do you want to uninstall the previous version first?" IDYES uninstall_previous IDNO continue_install
      uninstall_previous:
        ExecWait '"$0\Uninstall.exe" /S _?=$0'
        Delete "$0\Uninstall.exe"
        RMDir "$0"
      continue_install:
    ${EndIf}
  ${EndIf}
FunctionEnd
