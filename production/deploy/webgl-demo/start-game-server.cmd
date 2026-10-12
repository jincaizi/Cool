@echo off
REM 游戏服务端 —— 对外监听 8000（TCP/WS 给浏览器，UDP/KCP 给原生客户端，同进程双栈）。
REM 单独跑它用于排错；正常演示用 start-demo.cmd 一起起。
cd /d "%~dp0"

if not exist "dist\server\Server3C.exe" (
  echo [ERROR] 找不到 dist\server\Server3C.exe
  echo         在开发机上跑一次：bash production/deploy/webgl-demo/publish-server.sh
  exit /b 1
)

REM --ws-prefix http://+:8000/ 是**对外必须**的：默认的 127.0.0.1 前缀只认本机 Host 头，
REM   面试官的浏览器拨 <公网IP>:8000 会连不上（T8 实测：连 TCP 都建不起来）。
REM   `+` 这个强通配前缀需要 setup-once.cmd 里的 URL ACL。
REM --bots 100 是 P3(AOI) 那条声称面要的规模（浮层"远程实体"会从 0 跳到 8-9）。
REM --monsters 6：布局尾部追加的两只（DogKnight 距原点 20m、SpiderBoss 距原点 42m）都在出生点
REM 视野内，开屏就能看见、不必走过去（T8 截图验证）。但别挂机：Wolf 开局即仇恨，DogKnight 的
REM 巡逻/追击也会很快贴上你 —— 实测进世界后不动约 7~15 秒阵亡，并被送回选人界面重登。
REM 演示时留意走位，或按需把数量调低。
dist\server\Server3C.exe 8000 --monsters 6 --bots 100 --ws-prefix http://+:8000/
