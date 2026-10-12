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
REM --monsters 6：布局尾部的 DogKnight / SpiderBoss 距**玩家出生点** (10,0,-15.52) 46.5m / 50.9m，
REM   都在各自「感知半径 + 巡逻半径」（18+6 / 25+12）之外 —— 开局不仇恨，它们不会主动扑向你。
REM   （T8 修正：初版 DogKnight 距出生点只有 11m < 感知 18m，实测 12 分钟 8/8 次进世界打死挂机玩家。）
REM   但**别原地挂机**：[0] Wolf 的巡逻圈最近离出生点约 5.9m < 检测 15m，站着不动约 7 秒阵亡、
REM   被送回选人界面重登 —— 这在任何 --monsters ≥ 1 下都成立（槽位 [0] 总是 Wolf，躲不开）。
REM   两只新怪仍在 80m AOI 视野内（AoiGrid.SightRadius）——开屏可见，只是比初版远一倍多，
REM   别拿初版的截图当证据，要重新截（T8 的"开屏就能看见"是按旧点位 20m/42m 验的）。
REM   演示时留意走位，或按需把数量调低。
dist\server\Server3C.exe 8000 --monsters 6 --bots 100 --ws-prefix http://+:8000/
