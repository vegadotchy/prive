@echo off
REM ===========================================================================
REM  IATECH-SHIELD PRO - construit l'installateur .exe en une commande.
REM  Prerequis : .NET SDK 8 + Inno Setup (iscc dans le PATH).
REM ===========================================================================
setlocal

echo [1/2] Publication de l'executable autonome (win-x64)...
dotnet publish src\IatechShield\IatechShield.csproj -c Release -r win-x64 ^
  --self-contained true -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true
if errorlevel 1 (
  echo ECHEC de la publication .NET.
  exit /b 1
)

echo [2/2] Construction de l'installateur Inno Setup...
iscc installer\iatech-shield.iss
if errorlevel 1 (
  echo ECHEC de la construction de l'installateur.
  echo Verifiez qu'Inno Setup est installe et que 'iscc' est dans le PATH.
  exit /b 1
)

echo.
echo Termine. Installateur disponible dans : installer\Output\
endlocal
