#!/usr/bin/env bash
# T9：本地把**部署路径**跑一遍 —— 静态站点由 StaticServer.exe（8090，与云上同一个二进制、
# 同一份 dist/site）提供，游戏服务端用本机 8000 上已有的那个（T8/T12 留下的 --monsters 4 --bots 100）。
# 目的：证明"页面能加载到 InWorld"。
# 注意：8000 上是 --monsters 4，出生点会被围住，约 7 秒就死 → 确认之后要**立刻**连拍，
# 否则抓到的只会是"死后回选人界面"那一幕（那本身是另一条验过的路径，但证明不了 InWorld）。
cd E:/CodeForJob/Cool || exit 1
V=production/verify/t9
STEPS=(
  - addscriptfile:production/verify/t4-webgl-inject.js
  nav:http://127.0.0.1:8090/index.html
  sleep:45000
  mark:charselect
  shot:E:/CodeForJob/Cool/$V/inworld-00-charselect.png
  click:705,513 sleep:1500
  mark:clicked-online
  click:639,553
)
for i in 2 4 6 8 10 14; do
  STEPS+=( sleep:2000 shot:E:/CodeForJob/Cool/$V/inworld-t${i}s.png )
done
STEPS+=(
  eval:"JSON.stringify({sent:window.__sentFrames,recv:window.__recvFrames,recvHist:window.__recvHist})"
  mark:end
)
node production/verify/t4-webgl-cdp.js "${STEPS[@]}" > $V/_runInWorld.log 2>&1
echo "exit=$?"
