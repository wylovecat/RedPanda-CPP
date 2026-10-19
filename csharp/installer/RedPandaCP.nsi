Unicode true
!include "MUI2.nsh"

!ifndef VERSION
  !define VERSION "0.6.0"
!endif
!ifndef VERSION4
  !define VERSION4 "0.6.0.0"
!endif
!ifndef SOURCE
  !error "SOURCE is required"
!endif
!ifndef OUTFILE
  !define OUTFILE "RedPandaCP-setup.exe"
!endif
!ifndef APPICON
  !define APPICON ""
!endif

!define APPNAME "RedPanda CP 算法竞赛版"
!define COMPANY "RedPanda CP"
!define UNINSTKEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\RedPandaCP"

Name "${APPNAME}"
OutFile "${OUTFILE}"
InstallDir "$PROGRAMFILES64\RedPandaCP"
InstallDirRegKey HKLM "${UNINSTKEY}" "InstallLocation"
RequestExecutionLevel admin
SetCompressor /SOLID lzma
BrandingText "${APPNAME} v${VERSION}"

VIProductVersion "${VERSION4}"
VIAddVersionKey "ProductName" "${APPNAME}"
VIAddVersionKey "CompanyName" "${COMPANY}"
VIAddVersionKey "FileDescription" "${APPNAME} 安装程序"
VIAddVersionKey "FileVersion" "${VERSION}"
VIAddVersionKey "ProductVersion" "${VERSION}"
VIAddVersionKey "LegalCopyright" ""

!define MUI_ABORTWARNING
!if "${APPICON}" != ""
  !define MUI_ICON "${APPICON}"
  !define MUI_UNICON "${APPICON}"
!endif
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "SimpChinese"

Section "主程序" SEC_MAIN
  SetOutPath "$INSTDIR"
  File "${SOURCE}"

  WriteUninstaller "$INSTDIR\Uninstall.exe"

  CreateDirectory "$SMPROGRAMS\RedPanda CP"
  CreateShortcut "$SMPROGRAMS\RedPanda CP\RedPanda CP.lnk" "$INSTDIR\RedPandaCP.exe"
  CreateShortcut "$SMPROGRAMS\RedPanda CP\卸载 RedPanda CP.lnk" "$INSTDIR\Uninstall.exe"
  CreateShortcut "$DESKTOP\RedPanda CP.lnk" "$INSTDIR\RedPandaCP.exe"

  WriteRegStr HKLM "${UNINSTKEY}" "DisplayName" "${APPNAME}"
  WriteRegStr HKLM "${UNINSTKEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKLM "${UNINSTKEY}" "Publisher" "${COMPANY}"
  WriteRegStr HKLM "${UNINSTKEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKLM "${UNINSTKEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKLM "${UNINSTKEY}" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
  WriteRegDWORD HKLM "${UNINSTKEY}" "NoModify" 1
  WriteRegDWORD HKLM "${UNINSTKEY}" "NoRepair" 1
SectionEnd

Section "Uninstall"
  Delete "$DESKTOP\RedPanda CP.lnk"
  Delete "$SMPROGRAMS\RedPanda CP\RedPanda CP.lnk"
  Delete "$SMPROGRAMS\RedPanda CP\卸载 RedPanda CP.lnk"
  RMDir "$SMPROGRAMS\RedPanda CP"

  Delete "$INSTDIR\RedPandaCP.exe"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"

  DeleteRegKey HKLM "${UNINSTKEY}"
SectionEnd
