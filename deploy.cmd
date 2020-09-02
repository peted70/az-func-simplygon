@if "%SCM_TRACE_LEVEL%" NEQ "4" @echo off

:: ----------------------
:: KUDU Deployment Script
:: Version: 1.0.17
:: ----------------------

:: Prerequisites
:: -------------

:: Verify node.js installed
where node 2>nul >nul
IF %ERRORLEVEL% NEQ 0 (
  echo Missing node.js executable, please install node.js, if already installed make sure it can be reached from current environment.
  goto error
)

:: Setup
:: -----

setlocal enabledelayedexpansion

SET ARTIFACTS=%~dp0%..\artifacts

IF NOT DEFINED DEPLOYMENT_SOURCE (
  SET DEPLOYMENT_SOURCE=%~dp0%.
)

IF NOT DEFINED DEPLOYMENT_TARGET (
  SET DEPLOYMENT_TARGET=%ARTIFACTS%\wwwroot
)

IF NOT DEFINED NEXT_MANIFEST_PATH (
  SET NEXT_MANIFEST_PATH=%ARTIFACTS%\manifest

  IF NOT DEFINED PREVIOUS_MANIFEST_PATH (
    SET PREVIOUS_MANIFEST_PATH=%ARTIFACTS%\manifest
  )
)

IF NOT DEFINED KUDU_SYNC_CMD (
  :: Install kudu sync
  echo Installing Kudu Sync
  call npm install kudusync -g --silent
  IF !ERRORLEVEL! NEQ 0 goto error

  :: Locally just running "kuduSync" would also work
  SET KUDU_SYNC_CMD=%appdata%\npm\kuduSync.cmd
)

::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::
:: Setup Simplygon
:: ----------
echo Setting up Simplygon

call :ExecuteCmd "curl.exe" -o "%DEPLOYMENT_SOURCE%\Simplygon9.zip" "https://download.simplygon.com/%APPSETTING_SIMPLYGON_9_VERSION%.zip"
IF !ERRORLEVEL! NEQ 0 goto error

call :ExecuteCmd "powershell.exe" -nologo -noprofile -command "& { Add-Type -A 'System.IO.Compression.FileSystem'; [IO.Compression.ZipFile]::ExtractToDirectory('%DEPLOYMENT_SOURCE%\Simplygon9.zip', '.'); }"
IF !ERRORLEVEL! NEQ 0 goto error

call :ExecuteCmd "move %DEPLOYMENT_SOURCE%\SimplygonSDK* %DEPLOYMENT_SOURCE%\Simplygon9"
IF !ERRORLEVEL! NEQ 0 goto error

call :ExecuteCmd "powershell.exe" -File "%DEPLOYMENT_SOURCE%\setup_simplygon.ps1" -SimplygonLicenseKey %APPSETTING_SIMPLYGON_9_LICENSE%
IF !ERRORLEVEL! NEQ 0 goto error

::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::
:: Deployment
:: ----------

echo Handling function App deployment with Msbuild16.

:: 1. Restore, Build and publish
call :ExecuteCmd "%MSBUILD_16_DIR%\MSBuild.exe" /restore "%DEPLOYMENT_SOURCE%\src\SimplygonFunctionApp\SimplygonFunctionApp.csproj" /p:DeployOnBuild=true /p:configuration=Release /p:publishurl="%DEPLOYMENT_TEMP%" %SCM_BUILD_ARGS%
IF !ERRORLEVEL! NEQ 0 goto error

:: 2. KuduSync
IF /I "%IN_PLACE_DEPLOYMENT%" NEQ "1" (
  call :ExecuteCmd "%KUDU_SYNC_CMD%" -v 50 -f "%DEPLOYMENT_TEMP%" -t "%DEPLOYMENT_TARGET%" -n "%NEXT_MANIFEST_PATH%" -p "%PREVIOUS_MANIFEST_PATH%" -i ".git;.hg;.deployment;deploy.cmd"
  IF !ERRORLEVEL! NEQ 0 goto error
)

::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::
goto end

:: Execute command routine that will echo out when error
:ExecuteCmd
setlocal
set _CMD_=%*
call %_CMD_%
if "%ERRORLEVEL%" NEQ "0" echo Failed exitCode=%ERRORLEVEL%, command=%_CMD_%
exit /b %ERRORLEVEL%

:error
endlocal
echo An error has occurred during web site deployment.
call :exitSetErrorLevel
call :exitFromFunction 2>nul

:exitSetErrorLevel
exit /b 1

:exitFromFunction
()

:end
endlocal
echo Finished successfully.
