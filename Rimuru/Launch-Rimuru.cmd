@echo off
cd /d "%~dp0"
if exist "VPet-Simulator.Windows.exe" (
  start "" "VPet-Simulator.Windows.exe"
) else if exist "dist\Rimuru-VPet\VPet-Simulator.Windows.exe" (
  start "" /D "%~dp0dist\Rimuru-VPet" "%~dp0dist\Rimuru-VPet\VPet-Simulator.Windows.exe"
) else (
  echo Run Build-Rimuru.ps1 first.
  pause
)
