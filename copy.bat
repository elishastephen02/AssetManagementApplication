@echo off
setlocal EnableDelayedExpansion

cd /d "C:\Users\elishastephen\Desktop\AssetManagementApplication"

set "SOURCE=E:\cbd pictures\Picture 21"
set "DEST=AssetManagementApp\AssetManagement\AssetManagement\wwwroot\Picture"

set /a BATCH=1

echo.
echo STARTING PICTURE UPLOAD
echo ==========================================
echo Source:
echo %SOURCE%
echo.
echo Destination:
echo %DEST%
echo ==========================================
echo.

:START_BATCH

set /a COUNT=0

echo.
echo ==========================================
echo BATCH !BATCH!
echo Maximum 1000 new JPG files
echo ==========================================
echo.

for /r "%SOURCE%" %%F in (*.jpg) do (
    if !COUNT! LSS 1000 (
        if not exist "%DEST%\%%~nxF" (
            copy "%%F" "%DEST%\" >nul
            set /a COUNT+=1
            echo COPIED !COUNT!: %%~nxF
        )
    )
)

echo.
echo ==========================================
echo BATCH !BATCH! COMPLETE
echo Copied: !COUNT!
echo ==========================================
echo.

if !COUNT! EQU 0 (
    echo No new JPG pictures found.
    echo All pictures have already been copied.
    echo.
    goto FINISHED
)

echo Adding pictures to Git...
git add "%DEST%"

echo.
echo Committing batch !BATCH!...
git commit -m "Add picture batch !BATCH!"

if errorlevel 1 (
    echo.
    echo ERROR: Git commit failed.
    echo The process has been stopped.
    pause
    exit /b 1
)

echo.
echo Pushing batch !BATCH! to GitHub...
git push origin main

if errorlevel 1 (
    echo.
    echo ERROR: Git push failed.
    echo The process has been stopped.
    echo.
    echo The pictures are still in the repository folder.
    echo You can fix the Git issue and run this script again.
    pause
    exit /b 1
)

echo.
echo ==========================================
echo BATCH !BATCH! PUSHED SUCCESSFULLY
echo ==========================================
echo.

set /a BATCH+=1

goto START_BATCH


:FINISHED

echo.
echo ==========================================
echo ALL PICTURES COMPLETE
echo ==========================================
echo No more new JPG pictures found.
echo ==========================================
echo.

pause