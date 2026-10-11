#!/usr/bin/env bash
# 在**开发机**上跑一次：把要部署的东西全部准备好到 production/deploy/webgl-demo/dist/
#
#   bash production/deploy/webgl-demo/publish-server.sh
#
# 产出：
#   dist/site/     = Build/WebGL 的副本（静态站点，未压缩 —— gzip 由 StaticServer 在传输层做）
#   dist/server/   = 自包含的 win-x64 发布（Server3C.exe 游戏服务端 + StaticServer.exe 静态托管
#                    + .NET 运行时）。**自包含**意味着目标机不需要装 .NET SDK 或运行时。
#
# 目标机上要装的东西：无。整个 dist/ 拷过去就能跑。
set -euo pipefail

cd "$(dirname "$0")"
REPO_ROOT="$(cd ../../.. && pwd)"

SITE_SRC="$REPO_ROOT/Build/WebGL"
DIST="$PWD/dist"
SERVER_OUT="$DIST/server"
SITE_OUT="$DIST/site"

if [ ! -f "$SITE_SRC/index.html" ]; then
  echo "ERROR: $SITE_SRC 里没有 index.html —— 先在 Unity 里跑 Cool/Build/WebGL" >&2
  exit 1
fi

# WebGL 包必须是**未压缩**的原文件：StaticServer 在传输层 gzip，
# Unity 侧再预压缩就是双重压缩。构建脚本已钉死 Disabled 并会在产物里自查 .gz/.br。
if ls "$SITE_SRC/Build/" | grep -qE '\.(gz|br)$'; then
  echo "ERROR: $SITE_SRC/Build/ 里出现预压缩文件 —— 出包时 Unity 压缩没关掉" >&2
  ls -la "$SITE_SRC/Build/" >&2
  exit 1
fi

echo "== 1/3 发布游戏服务端（自包含 win-x64）=="
rm -rf "$SERVER_OUT"
dotnet publish "$REPO_ROOT/Server/Server3C.csproj" -c Release -r win-x64 --self-contained true \
  -o "$SERVER_OUT" -v quiet --nologo

echo "== 2/3 发布静态托管（自包含 win-x64，与游戏服务端共用同一份运行时）=="
dotnet publish "$PWD/StaticServer/StaticServer.csproj" -c Release -r win-x64 --self-contained true \
  -o "$SERVER_OUT" -v quiet --nologo

# 游戏服务端按 AppContext.BaseDirectory/Config 解析怪物配置（见 Server3C.csproj 的 None Include）。
# 少了它服务端能起来、但怪物一条都不刷 —— 静默失败，所以在这里断言。
if [ ! -f "$SERVER_OUT/Config/monster_config.json" ]; then
  echo "ERROR: 发布产物里缺 Config/monster_config.json —— 怪物不会刷，且不会有报错" >&2
  exit 1
fi
if [ ! -f "$SERVER_OUT/Server3C.exe" ] || [ ! -f "$SERVER_OUT/StaticServer.exe" ]; then
  echo "ERROR: 发布产物里缺可执行文件" >&2
  ls -la "$SERVER_OUT" >&2
  exit 1
fi

echo "== 3/3 拷贝静态站点 =="
rm -rf "$SITE_OUT"
mkdir -p "$SITE_OUT"
cp -r "$SITE_SRC/." "$SITE_OUT/"

echo
echo "完成。dist/ 的内容："
du -sh "$DIST" "$SERVER_OUT" "$SITE_OUT"
echo
echo "下一步：把整个 production/deploy/webgl-demo/ 文件夹拷贝到云服务器，"
echo "然后照 docs/superpowers/guides/webgl-demo-runbook.md 执行。"
