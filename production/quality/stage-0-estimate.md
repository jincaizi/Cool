# Stage 0 Estimate — Revision 2 Scope

- Date: 2026-11-07
- Scope: [执行设计 Revision 2.1](../../docs/superpowers/specs/2026-11-07-p0-p1-quality-improvement-execution-design.md) + [原 P0/P1 计划](../../docs/superpowers/plans/2026-11-05-p0-p1-quality-improvement.md)
- Confidence: low（数量级估算，Gate 0 与首个波次完成后必须重新基线化）

## 关于原 49–86 人天

执行设计风险表引用的 **49–86 人天仅覆盖原 P0/P1 工程部分**（原 33 个任务的实现与测试），
**不是 Revision 2 的完整估算**。Revision 2 新增的移动 KCP 安全层、IPv6/NAT64、公网 WebGL 部署、
移动候选包真机验证、release governance 与阶段 0 文档回写均不在该区间内。不得把 49–86 人天
当作 Revision 2 总工期或对外承诺。

## 估算方法与假设

- 1 人天 = 1 名熟悉本仓库的高级工程师 1 个工作日，包含实现、测试与波次 Gate 证据整理。
- 低值假设：无重大隐藏耦合、测试基础设施可复用、设备/runner 无需排队。
- 高值假设：每个波次包含一次返工、发现隐藏耦合或需要修复测试基础设施。
- 不含：日历时间、评审等待、Android 参考设备与 Windows/Linux runner 的采购或排队、
  CI 队列时间、内容/美术/设计工作、商店提交。
- 阶段 0 的文档/治理工作完成；§8 激活清单待裁决，不计入下列剩余估算。

## 按波次估算

| 波次 | 任务 | 低（人天） | 高（人天） | 依据与假设 | 主要风险 / 未覆盖项 |
|---|---|---:|---:|---|---|
| 阶段 0 | 文档与治理 | — | — | 文档/治理工作完成；一致性检查 PASS；无运行时代码；不计入剩余估算 | Codex consumer sentinel PASS；Linux consumer/hash `BLOCKED_NOT_VERIFIED`；§8 激活清单待裁决 |
| Gate 0 | 1 | 3 | 5 | runner、test discovery、server profile、XML 与失败传播 | 首次 Unity 批量测试时长未知；WebGL lane 可能需额外 runner |
| P0-A | 2–4 | 5 | 8 | Bag 数据保真、伤害上下文、EventBus/技能事务 | 存量测试覆盖不足可能扩大改动面 |
| P0-B | 5–8 | 8 | 13 | 单世界、幂等登录、服务器时间移动校验、格挡与精力 | 服务端权威与客户端预测一致性；票据认证需要新的测试替身 |
| P0-C | 9–13 | 12 | 20 | 有界队列、准入背压、服务端 KCP、客户端消息泵、平台传输边界 | KCP 生命周期/soak、DNS candidate policy、IPv4/IPv6/NAT64 基础未在本机验证 |
| P0 Gate | 14 | 2 | 4 | 聚合 P0 证据并生成 Gate 报告 | 依赖前序波次证据完整；任一平台 lane 阻塞会使 Gate 为 `BLOCKED_NOT_VERIFIED` |
| P1-A | 15–18 | 12 | 18 | WorldInstance、增量 AOI、ORCA、导航规模与双实例隔离 | 隔离断言与规模测试必须同轮通过；200/500 实体规模数据依赖基准环境 |
| P1-B1 | 19–23 | 10 | 16 | 伤害管线、Sys3C 注入、ResourceManager 租约、Addressables、UI 生命周期 | Domain Reload、Addressables catalog/hash 迁移、内建 UI 加载顺序 |
| P1-B2 | 24–28 | 7 | 12 | Nameplate/FloatText 预算、局部 HitStop、VFX 池、Monster 对象池 | 真机帧时间/GC 证据依赖参考设备；池复位协议容易遗漏订阅与碰撞注册 |
| P1-C1 | 29–31 | 7 | 12 | Android/iOS 构建入口、架构守卫、CI 与 Coverage 基线 | CI runner 能力、覆盖率基线、0-tests 门禁；iOS 构建 lane 不在本机范围 |
| P1-C2 | 32 | 12 | 20 | OpenTelemetry、健康检查、WSS 部署、移动 KCP 安全 provider 集成、rekey/rotation、优雅停机 | 公网端点、证书、反向代理不在仓库内；ARM64 真机与 KCP/UDP 公网路径未验证；provider 缺陷可能触发 WSS 回退重做 |
| P1 Engineering Gate | 33 | 2 | 4 | 工程项自动化证据与报告 | 依赖 CI、候选包与 P1-C2；证据缺失时只能 `BLOCKED_NOT_VERIFIED` |
| Mobile Release Readiness | 33 | 5 | 9 | 非 Development Android 候选包、真机安装/冷启动、网络切换、前后台、30 分钟 soak | Android 参考设备可用性；NAT64/IPv6 与真实网络环境不在本机复现 |
| WebGL Public Deployment Readiness | 33 | 4 | 7 | 公网 WSS、DNS/证书链、反向代理、浏览器 lane smoke | `publicEndpoints` 当前为空，必须先批准公网端点；CDN/部署 artifact 一致性依赖外部环境 |

**剩余工程估算合计（Gate 0 至三个 P1 Gate）：89–148 人天**（低值 89，高值 148）。

## 未覆盖项

- **iOS**：`NOT_IN_SCOPE`，本机无 macOS runner；不计入上述估算，也不宣称已验证。
- **移动 KCP/UDP 公网路径**：Stage 0 的 ADR-0003 POC 只验证 provider（Noise-C）集成；
  KCP/UDP 运行时、ARM64 真机、NAT64/IPv6、rekey/rotation 与公网候选包安全配置均未验证，
  相关工作计入 P1-C2 与 Mobile Release Readiness 的高值区间。
- **WebGL 公网部署**：`release-scope.yaml` 的 `publicEndpoints` 为空，公网域名、证书、
  反向代理与浏览器 lane 证据尚不存在；估算只覆盖工程侧准备，不覆盖外部基础设施审批与部署窗口。
- **安全 provider 回退**：若任务 32 集成发现 Noise-C 缺陷并回退到 WSS，P1-C2 需重做传输与部署部分，
  本估算未包含该回退成本。
- **设备与 runner 获取**：Android 参考设备、Windows/Linux CI runner 与可能的 WebGL 浏览器 lane
  的采购/配置时间未计入。
- **内容、美术、设计、商店提交与运营**：不在本估算范围。

## 使用说明

- 本文件是阶段 0 的重估基线，不是承诺；Gate 0 完成后应按实际测试时长与 runner 能力重新基线化，
  每个波次退出时更新剩余估算。
- 任何波次 Gate 为 `BLOCKED_NOT_VERIFIED` 时，不得用本估算推断该波次已验证或可发布。
