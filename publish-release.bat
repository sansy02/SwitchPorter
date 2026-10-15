@echo off
chcp 65001 >nul
cd /d "%~dp0"
echo 正在打包 SwitchAlbum 新版本（单文件 exe，约 1-3 分钟）...
dotnet publish SwitchAlbum -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish\single-v1.1.0 > publish-release.log 2>&1
if errorlevel 1 (
  echo 打包失败，详情见 publish-release.log
  pause
  exit /b 1
)
powershell -NoProfile -Command "Compress-Archive -Path 'publish\single-v1.1.0\SwitchAlbum.exe' -DestinationPath 'publish\SwitchAlbum-v1.1.0-single.zip' -Force"
echo 完成！新版 exe：publish\single-v1.1.0\SwitchAlbum.exe
start "" "publish\single-v1.1.0"
