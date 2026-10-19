; 大学事项综合待办（UniversityTodos）安装脚本
; 用 NSIS 3 编译；构建入口是 tools\build-installer.ps1。
;
; 设计要点：
;   · 默认按用户安装到 %LOCALAPPDATA%\Programs\大学事项综合待办（不需要管理员，
;     数据与缓存都写在这个目录下，卸载/备份只面对一个路径）。
;   · 安装后在 exe 旁写 portable.flag，应用据此把数据放 Data、WebView2 缓存放 Cache。
;   · 桌面 + 开始菜单快捷方式；卸载时默认保留 Data（设置/待办/登录态），
;     只有用户明确选择「不保留」才删。
;   · 首次安装可勾选迁移旧版数据：%LOCALAPPDATA%\HomeworkReminder（改名前的目录），
;     或 %LOCALAPPDATA%\UniversityTodos（新名字的 zip 版）。

Unicode true

!include "MUI2.nsh"
!include "nsDialogs.nsh"
!include "LogicLib.nsh"

!ifndef APP_VERSION
  !define APP_VERSION "1.4.4"
!endif
!ifndef PAYLOAD_DIR
  !define PAYLOAD_DIR "publish\trim"
!endif
!ifndef OUTPUT_FILE
  !define OUTPUT_FILE "publish\UniversityTodos-Setup-${APP_VERSION}.exe"
!endif
!ifndef OLD_DATA_DIR
  !define OLD_DATA_DIR "$LOCALAPPDATA\HomeworkReminder"
!endif

!define APP_NAME "大学事项综合待办"
!define APP_EXE "UniversityTodos.Desktop.exe"
!define APP_PUBLISHER "linpigeon"
!define UNINST_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\UniversityTodos"
!define APP_REG_KEY "Software\UniversityTodos"

Name "${APP_NAME}"
OutFile "${OUTPUT_FILE}"
InstallDir "$LOCALAPPDATA\Programs\${APP_NAME}"
InstallDirRegKey HKCU "${APP_REG_KEY}" "InstallDir"
RequestExecutionLevel user
SetCompressor /SOLID lzma

VIProductVersion "${APP_VERSION}.0"
VIAddVersionKey /LANG=2052 "ProductName" "${APP_NAME}"
VIAddVersionKey /LANG=2052 "FileDescription" "${APP_NAME} 安装程序"
VIAddVersionKey /LANG=2052 "FileVersion" "${APP_VERSION}"
VIAddVersionKey /LANG=2052 "ProductVersion" "${APP_VERSION}"
VIAddVersionKey /LANG=2052 "CompanyName" "${APP_PUBLISHER}"
VIAddVersionKey /LANG=2052 "LegalCopyright" "Copyright (c) 2026 linpigeon"

!define MUI_ABORTWARNING
!define MUI_ICON "..\UniversityTodos\Assets\app.ico"
!define MUI_UNICON "..\UniversityTodos\Assets\app.ico"
!define MUI_FINISHPAGE_RUN "$INSTDIR\${APP_EXE}"
!define MUI_FINISHPAGE_RUN_TEXT "立即运行 ${APP_NAME}"
!define MUI_FINISHPAGE_LINK "项目主页：github.com/linpigeon/UniversityTodos"
!define MUI_FINISHPAGE_LINK_LOCATION "https://github.com/linpigeon/UniversityTodos"

Var MigrateOldData
Var OldDataDir
Var MigrationDialog
Var MigrationCheckbox

Function .onInit
  ; 默认迁移：静默安装没有向导页，也应当把旧数据带过来。
  StrCpy $MigrateOldData 1
  StrCpy $OldDataDir "${OLD_DATA_DIR}"

  ; 从「新名字的 zip 版」升级上来的用户，数据在 %LOCALAPPDATA%\UniversityTodos；
  ; 旧目录没有数据时改从那里迁移，避免装完看不到原有待办。
  ${IfNot} ${FileExists} "$OldDataDir\settings.json"
  ${AndIfNot} ${FileExists} "$OldDataDir\session.json"
  ${AndIfNot} ${FileExists} "$OldDataDir\todos.json"
    ${If} ${FileExists} "$LOCALAPPDATA\UniversityTodos\settings.json"
      StrCpy $OldDataDir "$LOCALAPPDATA\UniversityTodos"
    ${EndIf}
  ${EndIf}

  ; 程序在运行时替换 exe 会失败，先让用户退出，避免装到一半留下半套文件。
  FindWindow $0 "" "${APP_NAME}"
  StrCmp $0 0 app_not_running
  IfSilent 0 app_running_gui
    SetErrorLevel 2
    Quit
  app_running_gui:
    MessageBox MB_OK|MB_ICONSTOP "检测到「${APP_NAME}」正在运行。$\r$\n请先退出程序（托盘图标 → 退出），再重新运行安装程序。" IDOK
    Quit
  app_not_running:
FunctionEnd

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
Page custom MigrationPageCreate MigrationPageLeave
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "SimpChinese"

Function MigrationPageCreate
  ; 没有旧数据，或新目录里已有数据（升级/重装）时，不显示迁移页。
  ${IfNot} ${FileExists} "$OldDataDir\settings.json"
  ${AndIfNot} ${FileExists} "$OldDataDir\session.json"
  ${AndIfNot} ${FileExists} "$OldDataDir\todos.json"
    Abort
  ${EndIf}
  ${If} ${FileExists} "$INSTDIR\Data\settings.json"
    Abort
  ${EndIf}

  nsDialogs::Create 1018
  Pop $MigrationDialog
  ${If} $MigrationDialog == error
    Abort
  ${EndIf}

  ${NSD_CreateLabel} 0 0 100% 38u "检测到旧版本数据：$\r$\n$OldDataDir$\r$\n$\r$\n可以把设置、待办缓存、自建待办、课表与登录态复制到新安装目录的 Data 文件夹。"
  Pop $0

  ${NSD_CreateCheckbox} 0 48u 100% 14u "迁移旧数据到新目录（推荐）"
  Pop $MigrationCheckbox
  ${NSD_SetState} $MigrationCheckbox ${BST_CHECKED}

  ${NSD_CreateLabel} 0 72u 100% 40u "说明：登录态是 Windows DPAPI 密文，只有同一台机器的同一 Windows 用户能解密；换机/换用户后需要重新登录。迁移前建议先备份旧目录。"
  Pop $0

  nsDialogs::Show
FunctionEnd

Function MigrationPageLeave
  ${NSD_GetState} $MigrationCheckbox $0
  ${If} $0 == ${BST_CHECKED}
    StrCpy $MigrateOldData 1
  ${Else}
    StrCpy $MigrateOldData 0
  ${EndIf}
FunctionEnd

Section "安装"
  SetOutPath "$INSTDIR"

  ; 载荷来自 publish\trim：单个 exe（约 43 MB）。
  File "${PAYLOAD_DIR}\${APP_EXE}"

  ; 旧名的 exe 与新版装在同一目录时会变成两个入口，清掉它（文件被占用时卸不掉，
  ; 上面的窗口检测已先要求用户退出）。
  Delete "$INSTDIR\HomeworkReminder.Desktop.exe"

  ; 安装标记：应用据此把 Data / Cache 放在安装目录下。
  CreateDirectory "$INSTDIR\Data"
  CreateDirectory "$INSTDIR\Cache"
  FileOpen $0 "$INSTDIR\portable.flag" w
  FileWrite $0 "portable$\r$\n"
  FileClose $0

  ; 只在 Data 里还没有设置时迁移，避免重装/升级覆盖新数据。
  ${If} $MigrateOldData == 1
  ${AndIfNot} ${FileExists} "$INSTDIR\Data\settings.json"
    CopyFiles /SILENT "$OldDataDir\*.json" "$INSTDIR\Data"
  ${EndIf}

  SetShellVarContext current

  WriteUninstaller "$INSTDIR\uninstall.exe"

  CreateDirectory "$SMPROGRAMS\${APP_NAME}"
  CreateShortCut "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\${APP_EXE}" 0
  CreateShortCut "$SMPROGRAMS\${APP_NAME}\卸载 ${APP_NAME}.lnk" "$INSTDIR\uninstall.exe"
  CreateShortCut "$DESKTOP\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\${APP_EXE}" 0

  WriteRegStr HKCU "${APP_REG_KEY}" "InstallDir" "$INSTDIR"
  WriteRegStr HKCU "${UNINST_KEY}" "DisplayName" "${APP_NAME}"
  WriteRegStr HKCU "${UNINST_KEY}" "DisplayVersion" "${APP_VERSION}"
  WriteRegStr HKCU "${UNINST_KEY}" "Publisher" "${APP_PUBLISHER}"
  WriteRegStr HKCU "${UNINST_KEY}" "DisplayIcon" "$INSTDIR\${APP_EXE}"
  WriteRegStr HKCU "${UNINST_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINST_KEY}" "UninstallString" '"$INSTDIR\uninstall.exe"'
  WriteRegDWORD HKCU "${UNINST_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINST_KEY}" "NoRepair" 1
SectionEnd

Section "Uninstall"
  SetShellVarContext current

  Delete "$DESKTOP\${APP_NAME}.lnk"
  Delete "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk"
  Delete "$SMPROGRAMS\${APP_NAME}\卸载 ${APP_NAME}.lnk"
  RMDir "$SMPROGRAMS\${APP_NAME}"

  DeleteRegKey HKCU "${UNINST_KEY}"
  DeleteRegKey HKCU "${APP_REG_KEY}"

  ; 缓存可以重建，直接删；Data 里是设置/待办/登录态，默认保留。
  RMDir /r "$INSTDIR\Cache"

  ; 静默卸载一律保留数据，避免脚本化卸载误删。
  IfSilent un_keep_data
  MessageBox MB_YESNO|MB_ICONQUESTION "是否保留 Data 目录？$\r$\n$\r$\n$INSTDIR\Data$\r$\n$\r$\n选择「是」会保留设置、待办、课表与登录态；选择「否」将一并删除。" IDYES un_keep_data
  RMDir /r "$INSTDIR\Data"

  un_keep_data:
  Delete "$INSTDIR\${APP_EXE}"
  Delete "$INSTDIR\portable.flag"
  Delete "$INSTDIR\uninstall.exe"
  RMDir "$INSTDIR"
SectionEnd
