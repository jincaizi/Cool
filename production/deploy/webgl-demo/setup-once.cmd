@echo off
REM ============================================================================
REM  在云服务器上**以管理员身份运行一次**。
REM
REM  做两件事，都是 Windows 的、跟本项目无关的前置条件：
REM    1) URL ACL —— HttpListener 绑非 localhost 前缀（http://+:8080/ 这类）默认被拒。
REM       T8 实测过：不加这条，服务进程直接 "拒绝访问" 起不来。
REM    2) 本机防火墙放行 8080 / 8000 的入站。
REM
REM  注意：腾讯云的**安全组**是另一道门，必须在控制台放行同样的两个端口，
REM  脚本改不到它。见 docs/superpowers/guides/webgl-demo-runbook.md。
REM ============================================================================
setlocal

net session >nul 2>&1
if errorlevel 1 (
  echo [ERROR] 需要管理员权限：右键本文件 -^> "以管理员身份运行"。
  exit /b 1
)

echo == URL ACL ==
netsh http add urlacl url=http://+:8080/ user=Everyone
netsh http add urlacl url=http://+:8000/ user=Everyone
echo    （若提示 "对象已存在" 说明之前加过，可忽略）

echo.
echo == 本机防火墙入站规则 ==
netsh advfirewall firewall show rule name="Cool WebGL demo 8080" >nul 2>&1
if errorlevel 1 (
  netsh advfirewall firewall add rule name="Cool WebGL demo 8080" dir=in action=allow protocol=TCP localport=8080
) else (
  echo    rule "Cool WebGL demo 8080" 已存在
)
netsh advfirewall firewall show rule name="Cool WebGL demo 8000" >nul 2>&1
if errorlevel 1 (
  netsh advfirewall firewall add rule name="Cool WebGL demo 8000" dir=in action=allow protocol=TCP localport=8000
) else (
  echo    rule "Cool WebGL demo 8000" 已存在
)

echo.
echo == 确认 ==
netsh http show urlacl | findstr /C:":8080/" /C:":8000/"
echo.
echo 完成。接下来双击 start-demo.cmd（普通权限即可）。
endlocal
