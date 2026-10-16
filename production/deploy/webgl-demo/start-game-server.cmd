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
REM 出生点 2026-10-17 第三次搬迁到 (-15,0.3,-30)（地面实测 y≈0.256；历史：08-27 原点系 →
REM   10-13 湖东岸 (10,0.21,-40) → 此处，服务端布局同批收紧收进陆地——旧 [3] (0,0,40) 在湖底 -9.08）。
REM   六槽位到出生点的「感知半径 + 巡逻半径」之和**全部有余量**（最近的是 (0,0,-35) 的 [2] Slime：
REM   15.8m > 8+5，余量 +2.8m）——开局站在原地不动**不会被任何怪发现**（守备见 Server.Tests 的
REM   New_Layout_Slots_Stay_Out_Of_Reach_Of_Demo_Spawn（全六槽位）与
REM   Idle_Player_At_Demo_Spawn_Sees_No_Cast_From_Any_Monster）。
REM   但这只保住**出生点**：朝 (0,0,-35) 走约 3m 就会踏进 [2] Slime 的感知圈（[0] Wolf 现在在
REM   (-30,0,0)、距出生点 33.5m，巡逻圈够不着——已不是最近的那只），之后照样会被追上打死、
REM   被送回选人界面重登。演示时留意走位（想活着就得动起来或打回去）。
REM 开屏取景：出生朝向 +Z（正对原点方向）。六只怪都在 80m AOI 视野内（AoiGrid.SightRadius），
REM   最远 [5] SpiderBoss 64.0m ⇒ 浮层"远程实体"稳定 6（旧出生点时有 80m 边缘进出波动，已消失）。
REM   要**把相机转过去**才看得全新怪。production/verify/t8_monsters.png 与 t13_spawn_*.png 都拍于
REM   旧出生点 ((10,0.21,-40))，不再等同"开屏所见"——重启服务端后请重新取景。
REM 2026-10-17 实测（与新布局无关、与 --bots 有关）：40 个 bot 的人群会把怪挡在半路——
REM   --bots 40 --bots-radius 60 下挂机观察者 60s 零施法（狼最近只到 8.6m/14.0m）；同一构建
REM   去掉 bots 后 0.6~19s 就被追上打。演示要"被打"的效果就少放 bot 或让玩家自己走上去。
dist\server\Server3C.exe 8000 --monsters 6 --bots 100 --ws-prefix http://+:8000/
