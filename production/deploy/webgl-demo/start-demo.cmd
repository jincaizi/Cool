@echo off
REM ============================================================================
REM  演示开始 —— 在云服务器上双击这一个文件。
REM
REM  前提：已经跑过一次 setup-once.cmd（管理员），并且安全组放行了 8080 与 8000。
REM  起两个窗口（各自保留日志，出问题能直接看）：
REM     静态站点  8080   <- 面试官打开 http://<公网IP>:8080/
REM     游戏服务端 8000  <- 页面里的服务器地址填 <公网IP>:8000，选"联网"
REM ============================================================================
cd /d "%~dp0"

if not exist "dist\server\StaticServer.exe" goto :missing
if not exist "dist\server\Server3C.exe" goto :missing

echo 启动静态站点（8080）...
start "静态站点 8080" cmd /k start-static.cmd

echo 等待静态站点起来...
timeout /t 3 /nobreak >nul

echo 启动游戏服务端（8000）...
start "游戏服务端 8000" cmd /k start-game-server.cmd

echo.
echo 两个窗口都已启动。自测：
echo   1) 本机浏览器打开 http://127.0.0.1:8080/ ，能出选人界面即静态站 OK
echo   2) 面试官的设备打开 http://^<公网IP^>:8080/
echo      地址栏填 ^<公网IP^>:8000，端口 8000，选"联网"，点确认
echo.
echo 停服：关掉那两个窗口（或 Ctrl+C）。
goto :eof

:missing
echo [ERROR] dist\ 不完整 —— 先在开发机上跑：
echo         bash production/deploy/webgl-demo/publish-server.sh
exit /b 1
