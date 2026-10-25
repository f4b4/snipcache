@echo off
rem Delete temporary files older than 7 days.
forfiles /p "%TEMP%" /s /m *.* /d -7 /c "cmd /c del /q @path" 2>nul
echo Done.
