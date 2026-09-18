@echo off
setlocal enabledelayedexpansion

echo =================================================================
echo   Movie Audio Cleaner ^& Applio Dataset Preparer
echo =================================================================

if "%~1"=="" (
    echo.
    echo Usage:
    echo   prepare_movie_for_applio.bat "path\to\movie.mp4" [dataset_name] [num_speakers_or_reference_wav]
    echo.
    echo Examples:
    echo   prepare_movie_for_applio.bat "D:\movie.mp4" my_movie
    echo   prepare_movie_for_applio.bat "D:\movie.mp4" my_movie 2              (Separates into 2 actors)
    echo   prepare_movie_for_applio.bat "D:\movie.mp4" my_movie "actor_ref.wav" (Matches target actor only)
    echo.
    echo Tip: You can also drag and drop any movie or audio file onto this .bat file!
    echo.
    pause
    exit /b 1
)

set "INPUT_FILE=%~1"
set "DATASET_NAME=%~2"
set "EXTRA_OPT=%~3"

set "EXTRA_ARGS="
if not "%EXTRA_OPT%"=="" (
    if exist "%EXTRA_OPT%" (
        echo Target Actor Reference: %EXTRA_OPT%
        set EXTRA_ARGS=-tv "%EXTRA_OPT%"
    ) else (
        echo Number of Actor Clusters: %EXTRA_OPT%
        set EXTRA_ARGS=-sp %EXTRA_OPT%
    )
)

echo Input: %INPUT_FILE%
if not "%DATASET_NAME%"=="" (
    echo Dataset Name: %DATASET_NAME%
)

"C:\Applio-3.6.4\env\python.exe" "%~dp0clean_dataset_for_applio.py" -i "%INPUT_FILE%" -n "%DATASET_NAME%" %EXTRA_ARGS%

echo.
echo Dataset is ready in Applio!
echo In Applio GUI: Go to Train tab -^> Dataset Path -^> Select your dataset!
pause
