@echo off
REM Construit IATechTradingBot.exe localement (Windows).
REM Prerequis : Python 3.10+ installe avec "py" dans le PATH.
py -m pip install --upgrade pyinstaller
py -m PyInstaller --onefile --noconsole --name IATechTradingBot main.py
echo.
echo Termine : dist\IATechTradingBot.exe
pause
