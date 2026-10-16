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
REM 出生点 2026-10-13 搬到湖东岸 (10,0.21,-40)（旧点 (10,0.16,-15.52)）：新点到六只怪的
REM   「感知半径 + 巡逻半径」之和**全部有余量**（最近的是 [0] Wolf：40.1m > 15+10）——
REM   开局站在原地不动**不会被任何怪发现**（活体验证：--monsters 6 挂机 3 分钟 0 伤害、未阵亡；
REM   守备见 Server.Tests 的 New_Layout_Slots_Stay_Out_Of_Reach_Of_Demo_Spawn（全六槽位）与
REM   Idle_Player_At_Demo_Spawn_Sees_No_Cast_From_Any_Monster）。
REM   但这只保住**出生点**：离开它朝原点方向走约 15m 就会踏进 [0] Wolf 的感知圈
REM   （40.1m − 巡逻半径 10m − 感知 15m ≈ 15m），之后照样会被追上打死、被送回选人界面重登。
REM   演示时留意走位（想活着就得动起来或打回去）。
REM 开屏取景：出生朝向 +Z（正对原点方向）。六只怪都在 80m AOI 视野内（AoiGrid.SightRadius），
REM   但 [0] Wolf 在正前方 40m、两只新怪偏左（DogKnight 左前 27°/67m，SpiderBoss 左侧 73°/52m）——
REM   要**把相机转过去**才看得全新怪。production/verify/t8_monsters.png 拍于旧出生点，方位参考
REM   仍近，但取景不再等同"开屏所见"（新取景见 t13_spawn_*.png）。
REM 2026-10-17 第三次搬迁：出生点 → (-15,0.3,-30)（地面实测 y≈0.256），布局同批收紧收进陆地（旧 [3] (0,0,40) 在湖底 −9.08）。
REM   仍全六槽位达标——最近的是 [2] Slime（15.8m > 8+5，余量 +2.8m）：挂机安全不变量未变，离开出生点朝 (0,0,-35) 走约 3m 才进它的感知圈；
REM   六只怪全在出生点 80m AOI 内（最远 [5] 64.0m）⇒ 浮层"远程实体"不再有 80m 边缘进出波动。
dist\server\Server3C.exe 8000 --monsters 6 --bots 100 --ws-prefix http://+:8000/
