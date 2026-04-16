[Setup]
AppName=He thong Tram can Tu dong KSRD
AppVersion=1.5.6
DefaultDirName={autopf}\RangDongMineral\WeighbridgeSystem
DefaultGroupName=RangDongMineral
OutputDir=D:\Setup_Output
OutputBaseFilename=Setup_TramCan_KSRD_Design_by_SonK_v1.5.6
; Dòng này giúp BỘ CÀI ĐẶT (Setup.exe) có icon
SetupIconFile=D:\CODE\AutoWeighbridgeSystem\autoweigh.ico
Compression=lzma2/max
SolidCompression=yes
MinVersion=10.0
PrivilegesRequired=admin

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
; Lựa chọn tạo icon Desktop
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; (BỔ SUNG) Copy riêng file icon vào thư mục cài đặt của phần mềm
Source: "D:\CODE\AutoWeighbridgeSystem\autoweigh.ico"; DestDir: "{app}"; Flags: ignoreversion

; Source trỏ đến thư mục publish của Sơn (Tôi khuyên bạn nên cấp quyền ghi file cho thư mục này để Serilog lưu Log không bị lỗi)
Source: "D:\CODE\AutoWeighbridgeSystem\AutoWeighbridgeSystem\bin\Debug\net8.0-windows\Publish"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Permissions: users-modify

[Icons]
; (BỔ SUNG) Thêm thuộc tính IconFilename để ép shortcut dùng đúng file ico vừa copy
Name: "{group}\Tram Can Tu Dong"; Filename: "{app}\AutoWeighbridgeSystem.exe"; IconFilename: "{app}\autoweigh.ico"
Name: "{autodesktop}\Tram Can Tu Dong"; Filename: "{app}\AutoWeighbridgeSystem.exe"; Tasks: desktopicon; IconFilename: "{app}\autoweigh.ico"

[Run]
Filename: "{app}\AutoWeighbridgeSystem.exe"; Description: "{cm:LaunchProgram,Tram Can Tu Dong}"; Flags: nowait postinstall skipifsilent