@echo off
REM ===========================================================================
REM  IATECH-SHIELD PRO - construit l'installateur .exe en une commande.
REM  Produit l'interface graphique + la CLI, puis l'installateur Windows.
REM  Prerequis : .NET SDK 8 + Inno Setup (iscc dans le PATH).
REM ===========================================================================
setlocal
set DIST=dist

echo [1/4] Nettoyage...
if exist %DIST% rmdir /s /q %DIST%
mkdir %DIST%

echo [2/4] Publication de l'interface graphique (WPF, win-x64)...
dotnet publish src\IatechShield.Gui\IatechShield.Gui.csproj -c Release -r win-x64 ^
  --self-contained true -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true -o %DIST%
if errorlevel 1 ( echo ECHEC publication GUI. & exit /b 1 )

echo [3/4] Publication de la CLI (win-x64)...
dotnet publish src\IatechShield\IatechShield.csproj -c Release -r win-x64 ^
  --self-contained true -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true -o %DIST%
if errorlevel 1 ( echo ECHEC publication CLI. & exit /b 1 )

echo [4/4] Construction de l'installateur Inno Setup...
iscc installer\iatech-shield.iss
if errorlevel 1 (
  echo ECHEC de la construction de l'installateur.
  echo Verifiez qu'Inno Setup est installe et que 'iscc' est dans le PATH.
  exit /b 1
)

echo.
echo Termine. Installateur disponible dans : installer\Output\
endlocal
