@echo off
rem Wrapper så publiceringen går att starta från cmd eller genom dubbelklick.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0publish.ps1" %*
