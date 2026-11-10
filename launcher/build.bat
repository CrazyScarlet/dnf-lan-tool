@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo [x] 找不到 csc.exe，请确认已安装 .NET Framework 4.x
  exit /b 1
)

"%CSC%" /nologo /target:winexe /optimize+ /out:DnfLanTool.exe ^
  /r:System.dll /r:System.Core.dll /r:System.Windows.Forms.dll ^
  /r:System.Drawing.dll /r:System.Web.Extensions.dll ^
  /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll ^
  "DNF联机工具源码.cs"

if errorlevel 1 (
  echo [x] 编译失败
  exit /b 1
)
echo [ok] 已生成 DnfLanTool.exe
