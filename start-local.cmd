@echo off
REM Double-click this to run Win Master on your PC.
REM Opens on http://localhost:5249/  (login: admin@winmaster.io / Win@12345)
cd /d "%~dp0"
powershell -ExecutionPolicy Bypass -NoProfile -File "%~dp0run-local.ps1"
pause
