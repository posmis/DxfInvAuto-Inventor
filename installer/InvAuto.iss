; Установщик InvAuto (Inno Setup 6.3+, https://jrsoftware.org/isinfo.php). Манифест InvAuto.addin входит
; в выходную папку сборки — он и есть «регистрация» плагина в Inventor, реестр не нужен.
; Обычно собирается скриптом tools\build-release.ps1. Вручную:
;   ISCC.exe installer\InvAuto.iss /DInventorVersion=2025 /DSourceDir=..\src\InvAuto.Inventor\bin\Release\net8.0-windows
; Результат: installer\Output\InvAuto-<версия>-Inventor<версия Inventor>-setup.exe

#ifndef InventorVersion
  #define InventorVersion "2025"
#endif
#ifndef SourceDir
  #define SourceDir "..\src\InvAuto.Inventor\bin\Release\net8.0-windows"
#endif
#ifndef AppVersion
  ; Версия — из собранной InvAuto.dll (Directory.Build.props → 1.0.0).
  #define FileVer GetVersionNumbersString(AddBackslash(SourceDir) + "InvAuto.dll")
  #define AppVersion Copy(FileVer, 1, RPos(".", FileVer) - 1)
#endif

[Setup]
; AppId не менять: по нему установщик находит и обновляет прежнюю версию.
AppId={{6F4C2A1E-3B7D-4E59-9C1A-8D2F5B7E4A10}
AppName=InvAuto для Inventor {#InventorVersion}
AppVersion={#AppVersion}
AppVerName=InvAuto {#AppVersion} для Inventor {#InventorVersion}
AppPublisher=InvAuto
; Inventor ищет плагины в ApplicationPlugins: для текущего пользователя (%APPDATA%) или для всех (%PROGRAMDATA%).
; По умолчанию — для текущего пользователя, без прав администратора; в начале можно выбрать «для всех».
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={autoappdata}\Autodesk\ApplicationPlugins\InvAuto
DisableDirPage=yes
DisableProgramGroupPage=yes
UninstallDisplayName=InvAuto {#AppVersion} (Inventor {#InventorVersion})
OutputDir=Output
OutputBaseFilename=InvAuto-{#AppVersion}-Inventor{#InventorVersion}-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupLogging=yes

[Languages]
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"

[Files]
; Всё из выходной папки сборки, кроме отладочных файлов. Autodesk.Inventor.Interop.dll не копируется (Private=false).
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "*.pdb,*.xml"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
; Обновление: старые DLL удаляются, чтобы не осталось файлов прежней версии.
Type: files; Name: "{app}\*.dll"

[Messages]
ru.WelcomeLabel2=Будет установлен плагин InvAuto {#AppVersion} для Autodesk Inventor {#InventorVersion}.%n%nПеред установкой закройте Inventor.

[Code]
// Inventor держит DLL плагина открытыми — установка поверх запущенного Inventor не удастся.
function InventorRunning: Boolean;
var
  Locator, Service, Processes: Variant;
begin
  Result := False;
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Service := Locator.ConnectServer('.', 'root\CIMV2');
    Processes := Service.ExecQuery('SELECT ProcessId FROM Win32_Process WHERE Name = ''Inventor.exe''');
    Result := Processes.Count > 0;
  except
    Result := False;
  end;
end;

function InitializeSetup: Boolean;
begin
  Result := True;
  while InventorRunning do
  begin
    if MsgBox('Inventor запущен. Закройте его и нажмите «Повтор», иначе плагин не обновится.',
              mbError, MB_RETRYCANCEL) = IDCANCEL then
    begin
      Result := False;
      Exit;
    end;
  end;
end;

function InitializeUninstall: Boolean;
begin
  Result := True;
  if InventorRunning then
  begin
    MsgBox('Закройте Inventor перед удалением InvAuto.', mbError, MB_OK);
    Result := False;
  end;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpFinished then
    WizardForm.FinishedLabel.Caption :=
      'InvAuto установлен. Запустите Inventor — кнопки появятся на вкладке «Среды», панель InvAuto.' + #13#10#13#10 +
      'Настройки и библиотека материалов хранятся в %APPDATA%\InvAuto и при обновлении или удалении не затрагиваются.';
end;
