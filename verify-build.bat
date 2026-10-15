@echo off
chcp 65001 >nul
cd /d "%~dp0"
echo ==== dotnet build ==== > verify-build.log
dotnet build SwitchAlbum.sln -c Debug -nologo -v q -clp:NoSummary >> verify-build.log 2>&1
echo BUILD_EXIT=%ERRORLEVEL% >> verify-build.log
echo ==== tests ==== >> verify-build.log
dotnet run --project SwitchAlbum.TestsHarness --no-build >> verify-build.log 2>&1
echo TEST_EXIT=%ERRORLEVEL% >> verify-build.log
echo ==== smoke ==== >> verify-build.log
dotnet run --project SwitchAlbum --no-build -- --smoke-test >> verify-build.log 2>&1
timeout /t 20 /nobreak >nul
type "%TEMP%\SwitchAlbumSmokeTest.txt" >> verify-build.log 2>&1
echo DONE >> verify-build.log
