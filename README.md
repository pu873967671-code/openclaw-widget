# OpenClaw Widget

> **平台：Windows（WPF/.NET Framework）**

一只放在桌面右下角的 OpenClaw 状态小组件（托盘 + 悬浮信息面板）。

## 主要功能

- **状态可视化**
  - 圆环边框 + 右下角状态点实时显示健康状态
  - 健康：绿色（`#4ADE80`）
  - 不健康/离线：红色（`#EF4444`）

- **定制化龙虾 UI**
  - 中心图标已替换为静态龙虾素材（用户提供）
  - 背景色使用深蓝灰（`#182633`）
  - 边框增加轻微同色外发光（随状态色同步）

- **自动刷新与状态拉取**
  - 启动时自动刷新一次
  - 默认每 60 秒轮询状态接口（`http://localhost:4200/status`）
  - 托盘标题同步显示 `Healthy / Unhealthy / Offline`

- **离线自恢复（防抖）**
  - 当检测到 `Offline/Unhealthy` 时，尝试自动拉起网关
  - 通过 WSL 执行：`openclaw gateway start || openclaw gateway`
  - 内置冷却时间（2 分钟）避免频繁重复触发

- **交互体验**
  - 悬浮显示信息面板（版本、状态、简要信息）
  - 双击图标可触发刷新与轻微反馈动画
  - 托盘菜单支持常见操作（显示/退出等）

## 项目结构（简）

- `Widget.cs`：主窗口、状态刷新、UI 与恢复逻辑
- `assets/lobster_static/lobster.png`：静态龙虾主图
- `build.bat`：本地编译脚本
- `OpenClawWidget.exe`：编译产物

## 构建与运行（Windows）

```bat
build.bat
OpenClawWidget.exe
```

## 说明

- 当前版本以“识别度 + 稳定性”为优先，采用静态龙虾方案（不使用帧动画）。
- 如需后续扩展，可继续增加：状态文本常驻、可配置主题、刷新间隔配置化、告警声音/通知等。
