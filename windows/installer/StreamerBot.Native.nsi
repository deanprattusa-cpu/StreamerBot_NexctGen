Unicode true
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "x64.nsh"
!include "WinVer.nsh"
!ifndef PAYLOAD
!error "Define PAYLOAD as the validated staging directory"
!endif
!ifndef UNINSTALL_INCLUDE
!error "Define UNINSTALL_INCLUDE as the generated payload deletion list"
!endif
!ifndef INSTALLER_OUT
!define INSTALLER_OUT "StreamerBot-Native-Setup.exe"
!endif
Name "StreamerBot Native Preview"
OutFile "${INSTALLER_OUT}"
InstallDir "$LOCALAPPDATA\Programs\StreamerBotNative"
RequestExecutionLevel user
SetCompressor /SOLID lzma
SetCompressorDictSize 64
VIProductVersion "0.3.0.0"
VIAddVersionKey "ProductName" "StreamerBot Native Preview"
VIAddVersionKey "FileDescription" "StreamerBot native Windows migration preview installer"
VIAddVersionKey "FileVersion" "0.3.0"
VIAddVersionKey "LegalCopyright" "StreamerBot contributors"
!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TEXT "Install the C# / WinUI 3 migration preview.$\r$\n$\r$\nSome original functionality is still being ported. Keep your original app; read NATIVE-PARITY.md after installation.$\r$\n$\r$\n.NET and Windows App SDK are bundled. Voice models and the optional private Python voice runtime are downloaded separately from the app."
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_RUN "$INSTDIR\StreamerBot.Native.exe"
!define MUI_FINISHPAGE_RUN_NOTCHECKED
!define MUI_FINISHPAGE_SHOWREADME "$INSTDIR\NATIVE-PARITY.md"
!define MUI_FINISHPAGE_SHOWREADME_NOTCHECKED
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"
Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_ICONSTOP "Windows x64 is required."
    Abort
  ${EndIf}
  ${IfNot} ${AtLeastWin11}
    MessageBox MB_ICONSTOP "This preview requires Windows 11."
    Abort
  ${EndIf}
  SetShellVarContext current
FunctionEnd
Section "Native application"
  SetOutPath "$INSTDIR"
  ClearErrors
  File /r "${PAYLOAD}\*"
  ${If} ${Errors}
    MessageBox MB_ICONSTOP "Installation could not complete. Close StreamerBot and retry."
    Abort
  ${EndIf}
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  CreateDirectory "$SMPROGRAMS\StreamerBot Native"
  CreateShortcut "$SMPROGRAMS\StreamerBot Native\StreamerBot Native.lnk" "$INSTDIR\StreamerBot.Native.exe"
  CreateShortcut "$SMPROGRAMS\StreamerBot Native\Migration notes.lnk" "$INSTDIR\NATIVE-PARITY.md"
  CreateShortcut "$SMPROGRAMS\StreamerBot Native\Uninstall.lnk" "$INSTDIR\Uninstall.exe"
  CreateShortcut "$DESKTOP\StreamerBot Native.lnk" "$INSTDIR\StreamerBot.Native.exe"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\StreamerBotNative" "DisplayName" "StreamerBot Native Preview"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\StreamerBotNative" "DisplayVersion" "0.3.0"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\StreamerBotNative" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\StreamerBotNative" "UninstallString" '$\"$INSTDIR\Uninstall.exe$\"'
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\StreamerBotNative" "NoModify" 1
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\StreamerBotNative" "NoRepair" 1
SectionEnd
Section "Uninstall"
  SetShellVarContext current
  ; Never recurse outside application files. Profiles / caches are a sibling folder.
  Delete "$DESKTOP\StreamerBot Native.lnk"
  RMDir /r "$SMPROGRAMS\StreamerBot Native"
  !include "${UNINSTALL_INCLUDE}"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\StreamerBotNative"
SectionEnd
