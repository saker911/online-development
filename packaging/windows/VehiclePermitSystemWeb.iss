#define MyAppName "VehiclePermitSystem"
#define MyAppDisplayName "نظام تصاريح المركبات"
#define MyAppPublisher "Vehicle Permit System"
#define MyAppExeName "VehiclePermitSystemWeb.exe"
#define MyAppServiceName "VehiclePermitSystem"
#define MyAppTaskName "VehiclePermitSystemStartup"
#define MyAppLegacyTaskName1 "VehiclePermitSystemWeb"
#define MyAppLegacyTaskName2 "VehiclePermitSystemService"
#define MyAppServiceInstallScript "install-service.ps1"
#define MyAppServiceUninstallScript "uninstall-service.ps1"
#define MyAppVersion GetEnv("VPS_APP_VERSION")
#define MyAppSourceDir GetEnv("VPS_APP_SOURCE_DIR")
#define MyAppOutputDir GetEnv("VPS_INSTALLER_OUTPUT_DIR")

[Setup]
AppId={{9E2B6BA1-4AF9-40D9-89D1-4A46893011B7}
AppName={#MyAppDisplayName}
AppVerName={#MyAppDisplayName} {#MyAppVersion}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\Vehicle Permit System
UsePreviousAppDir=no
DefaultGroupName={#MyAppDisplayName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
OutputDir={#MyAppOutputDir}
OutputBaseFilename=VehiclePermitSystem-Setup-{#MyAppVersion}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupLogging=yes
RestartIfNeededByRun=no

[Languages]
Name: "arabic"; MessagesFile: "compiler:Languages\Arabic.isl"

[Tasks]
Name: "desktopicon"; Description: "إنشاء اختصار على سطح المكتب"; GroupDescription: "الخيارات الإضافية:";

[Dirs]
Name: "{commonappdata}\Vehicle Permit System"
Name: "{commonappdata}\Vehicle Permit System\data"; Permissions: users-modify
Name: "{commonappdata}\Vehicle Permit System\uploads"; Permissions: users-modify
Name: "{commonappdata}\Vehicle Permit System\config"; Permissions: users-modify
Name: "{commonappdata}\Vehicle Permit System\logs"; Permissions: users-modify
Name: "{commonappdata}\Vehicle Permit System\backups"; Permissions: users-modify

[Files]
Source: "{#MyAppSourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#MyAppSourceDir}\{#MyAppExeName}"; Flags: dontcopy
Source: "{#SourcePath}\prepare-upgrade.ps1"; Flags: dontcopy
Source: "{#SourcePath}\installer-common.ps1"; Flags: dontcopy

[Icons]
Name: "{group}\تشغيل النظام محلياً"; Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File ""{app}\tools\open-local.ps1"""; IconFilename: "{app}\{#MyAppExeName}"
Name: "{group}\تشغيل النظام بالخلفية"; Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File ""{app}\tools\ensure-background.ps1"""; IconFilename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\تشغيل النظام محلياً"; Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File ""{app}\tools\open-local.ps1"""; IconFilename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{autodesktop}\تشغيل النظام بالخلفية"; Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File ""{app}\tools\ensure-background.ps1"""; IconFilename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-ExecutionPolicy Bypass -NoProfile -NonInteractive -File ""{app}\tools\{#MyAppServiceInstallScript}"" -BindAddress ""127.0.0.1"" -Port 5000"; Flags: runhidden waituntilterminated
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File ""{app}\tools\open-local.ps1"""; Description: "فتح النظام الآن"; Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallRun]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-ExecutionPolicy Bypass -NoProfile -NonInteractive -File ""{app}\tools\{#MyAppServiceUninstallScript}"""; Flags: runhidden waituntilterminated; RunOnceId: "RemoveWindowsService"

[Code]
var
	InstallModeLabel: TNewStaticText;
	UpgradeModeDetected: Boolean;
	RepairModeDetected: Boolean;

procedure AppendBootstrapLog(const Message: string);
var
	LogDirectory: string;
	LogPath: string;
	Entry: string;
begin
	LogDirectory := ExpandConstant('{commonappdata}\Vehicle Permit System\logs');
	ForceDirectories(LogDirectory);
	LogPath := LogDirectory + '\setup-bootstrap.log';
	Entry := '[' + GetDateTimeString('yyyy-mm-dd hh:nn:ss', '-', ':') + '] ' + Message + #13#10;
	SaveStringToFile(LogPath, Entry, True);
end;

function QuoteParameter(const Value: string): string;
begin
	Result := '"' + Value + '"';
end;

function GetPowerShellPath(): string;
begin
	Result := ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe');
end;

function ExecBestEffort(const Filename: string; const Parameters: string): Boolean;
var
	ResultCode: Integer;
begin
	Result := Exec(Filename, Parameters, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
	if Result then
		AppendBootstrapLog('Fallback command finished: ' + Filename + ' ' + Parameters + ' (ResultCode=' + IntToStr(ResultCode) + ')')
	else
		AppendBootstrapLog('Fallback command failed to start: ' + Filename + ' ' + Parameters + ' (ResultCode=' + IntToStr(ResultCode) + ')');
end;

procedure StopServiceBestEffort();
begin
	if RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\{#MyAppServiceName}') then
	begin
		AppendBootstrapLog('Fallback: attempting to stop Windows service.');
		ExecBestEffort(ExpandConstant('{sys}\sc.exe'), 'stop ' + QuoteParameter('{#MyAppServiceName}'));
		Sleep(3000);
	end
	else
	begin
		AppendBootstrapLog('Fallback: Windows service not detected.');
	end;
end;

procedure StopScheduledTaskBestEffort(const TaskName: string);
begin
	if TaskName = '' then
		Exit;

	AppendBootstrapLog('Fallback: attempting to remove scheduled task ' + TaskName + '.');
	ExecBestEffort(ExpandConstant('{sys}\schtasks.exe'), '/End /TN ' + QuoteParameter(TaskName));
	ExecBestEffort(ExpandConstant('{sys}\schtasks.exe'), '/Delete /TN ' + QuoteParameter(TaskName) + ' /F');
end;

procedure StopExecutableBestEffort();
begin
	AppendBootstrapLog('Fallback: attempting to stop application process.');
	ExecBestEffort(ExpandConstant('{sys}\taskkill.exe'), '/IM ' + QuoteParameter('{#MyAppExeName}') + ' /F /T');
	Sleep(2000);
end;

function RunPrepareUpgradeFallback(): Boolean;
begin
	AppendBootstrapLog('Fallback prepare path started.');
	StopScheduledTaskBestEffort('{#MyAppTaskName}');
	StopScheduledTaskBestEffort('{#MyAppLegacyTaskName1}');
	StopScheduledTaskBestEffort('{#MyAppLegacyTaskName2}');
	StopServiceBestEffort();
	StopExecutableBestEffort();
	AppendBootstrapLog('Fallback prepare path completed. ProgramData content will remain untouched during file replacement.');
	Result := True;
end;

function HasPriorInstallationSignals(): Boolean;
begin
	Result :=
		DirExists(WizardDirValue) or
		FileExists(AddBackslash(WizardDirValue) + '{#MyAppExeName}') or
		RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\{#MyAppServiceName}') or
		RegKeyExists(HKLM, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#SetupSetting("AppId")}_is1');
end;

function ResolveStorageRoot(): string;
begin
	if DirExists(ExpandConstant('{commonappdata}\Vehicle Permit System')) then
	begin
		Result := ExpandConstant('{commonappdata}\Vehicle Permit System');
		Exit;
	end;

	if DirExists(ExpandConstant('{commonappdata}\VehiclePermitSystemWeb')) then
	begin
		Result := ExpandConstant('{commonappdata}\VehiclePermitSystemWeb');
		Exit;
	end;

	Result := ExpandConstant('{commonappdata}\Vehicle Permit System');
end;

function HasPersistedRuntimeState(): Boolean;
var
	StorageRoot: string;
begin
	StorageRoot := ResolveStorageRoot();
	Result :=
		FileExists(AddBackslash(StorageRoot) + 'data\vehicle-permit-system.db') or
		FileExists(AddBackslash(StorageRoot) + 'config\appsettings.runtime.json');
end;

function GetInstallerModeMessage(): string;
begin
	UpgradeModeDetected := HasPersistedRuntimeState();
	RepairModeDetected := (not UpgradeModeDetected) and HasPriorInstallationSignals();

	if UpgradeModeDetected then
	begin
		Result := 'وضع التشغيل: ترقية على بيانات موجودة. سيتم تنفيذ Schema upgrades واستبدال ملفات البرنامج فقط مع الحفاظ على ProgramData وقاعدة البيانات والملفات المرفوعة والسجلات والنسخ الاحتياطية.';
		Exit;
	end;

	if RepairModeDetected then
	begin
		Result := 'وضع التشغيل: إصلاح أو إعادة تثبيت فوق بقايا نسخة سابقة دون بيانات تشغيل مؤكدة. لن يتم حذف ProgramData، وإذا لم توجد قاعدة بيانات مهيأة فسيعمل النظام كتثبيت جديد.';
		Exit;
	end;

	Result := 'وضع التشغيل: تثبيت جديد. لم يتم العثور على قاعدة بيانات أو إعدادات تشغيل سابقة، لذلك سيظهر First Run Setup Wizard بعد اكتمال التثبيت.';
end;

procedure InitializeWizard();
begin
	InstallModeLabel := TNewStaticText.Create(WizardForm);
	InstallModeLabel.Parent := WizardForm.WelcomePage;
	InstallModeLabel.Left := WizardForm.WelcomeLabel2.Left;
	InstallModeLabel.Top := WizardForm.WelcomeLabel2.Top + WizardForm.WelcomeLabel2.Height + ScaleY(12);
	InstallModeLabel.Width := WizardForm.WelcomeLabel2.Width;
	InstallModeLabel.Height := ScaleY(52);
	InstallModeLabel.AutoSize := False;
	InstallModeLabel.WordWrap := True;
	InstallModeLabel.Caption := GetInstallerModeMessage();
	InstallModeLabel.Font.Style := [fsBold];
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
	ResultCode: Integer;
	HelperPath: string;
	ScriptPath: string;
	ProbePath: string;
	Parameters: string;
	BootstrapLogPath: string;
	PowerShellPath: string;
begin
	Result := '';
	AppendBootstrapLog('PrepareToInstall started. WizardDir=' + WizardDirValue);

	if not HasPriorInstallationSignals() then
	begin
		AppendBootstrapLog('Fresh install detected. Skipping prepare-upgrade.');
		exit;
	end;

	ExtractTemporaryFile('installer-common.ps1');
	ExtractTemporaryFile('prepare-upgrade.ps1');
	ExtractTemporaryFile('{#MyAppExeName}');
	HelperPath := ExpandConstant('{tmp}\installer-common.ps1');
	ScriptPath := ExpandConstant('{tmp}\prepare-upgrade.ps1');
	ProbePath := ExpandConstant('{tmp}\{#MyAppExeName}');
	BootstrapLogPath := ExpandConstant('{commonappdata}\Vehicle Permit System\logs\setup-bootstrap.log');
	PowerShellPath := GetPowerShellPath();
	Parameters := '-ExecutionPolicy Bypass -NoProfile -NonInteractive -File ' + QuoteParameter(ScriptPath) + ' -ServiceName ' + QuoteParameter('{#MyAppServiceName}') + ' -TaskName ' + QuoteParameter('{#MyAppTaskName}') + ' -InstallDirectory ' + QuoteParameter(WizardDirValue) + ' -HelperScriptPath ' + QuoteParameter(HelperPath) + ' -BootstrapLogPath ' + QuoteParameter(BootstrapLogPath) + ' -ProbeExecutablePath ' + QuoteParameter(ProbePath);
	AppendBootstrapLog('Calling PowerShell prepare-upgrade. ScriptPath=' + ScriptPath);
	AppendBootstrapLog('Resolved PowerShell path=' + PowerShellPath);

	if not FileExists(PowerShellPath) then
	begin
		AppendBootstrapLog('PowerShell executable was not found at the resolved system path. Switching to fallback prepare path.');
		if RunPrepareUpgradeFallback() then
		begin
			AppendBootstrapLog('PrepareToInstall continued after fallback prepare path.');
			Exit;
		end;

		Result := 'تعذر تجهيز التثبيت أو الترقية قبل نسخ الملفات. راجع setup-bootstrap.log و installer.log داخل ProgramData.';
		Exit;
	end;

	if not Exec(PowerShellPath, Parameters, ExpandConstant('{tmp}'), SW_HIDE, ewWaitUntilTerminated, ResultCode) then
	begin
		AppendBootstrapLog('PowerShell execution failed before completion. ResultCode=' + IntToStr(ResultCode));
		if RunPrepareUpgradeFallback() then
		begin
			AppendBootstrapLog('PrepareToInstall continued after fallback prepare path because PowerShell did not start cleanly.');
			Exit;
		end;

		Result := 'تعذر تجهيز التثبيت أو الترقية قبل نسخ الملفات. راجع setup-bootstrap.log و installer.log داخل ProgramData.';
		Exit;
	end;

	AppendBootstrapLog('PowerShell prepare-upgrade finished. ResultCode=' + IntToStr(ResultCode));

	if ResultCode <> 0 then
	begin
		AppendBootstrapLog('PrepareToInstall detected a non-zero exit code. Switching to fallback prepare path.');
		if RunPrepareUpgradeFallback() then
		begin
			AppendBootstrapLog('PrepareToInstall continued after fallback prepare path because PowerShell returned a non-zero exit code.');
			Exit;
		end;

		AppendBootstrapLog('PrepareToInstall failed because prepare-upgrade returned non-zero exit code and the fallback path was unavailable.');
		Result := 'فشلت خطوة التحقق أو تجهيز التحديث قبل التثبيت. راجع setup-bootstrap.log و installer.log داخل ProgramData.';
	end
	else
	begin
		AppendBootstrapLog('PrepareToInstall completed successfully.');
	end;
end;
