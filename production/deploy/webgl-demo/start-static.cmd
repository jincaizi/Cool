@echo off
REM 静态站点（WebGL 包）—— 对外监听 8080，边传边 gzip。
REM 单独跑它用于排错；正常演示用 start-demo.cmd 一起起。
cd /d "%~dp0"

if not exist "dist\site\index.html" (
  echo [ERROR] 找不到 dist\site\index.html
  echo         在开发机上跑一次：bash production/deploy/webgl-demo/publish-server.sh
  exit /b 1
)

REM http://+:8080/ 需要先跑 setup-once.cmd（URL ACL）。只想本机看就换成
REM   --prefix http://127.0.0.1:8080/   （免管理员）
dist\server\StaticServer.exe --root "dist\site" --port 8080 --prefix http://+:8080/
