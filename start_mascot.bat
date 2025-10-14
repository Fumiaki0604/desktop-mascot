@echo off
cd /d "C:\DesktopMascot_Enhanced"
call "C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\Tools\VsDevCmd.bat"
dotnet run --project DesktopMascotEnhanced.csproj
pause