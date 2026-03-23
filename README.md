# AutoSLCut (Slay the Spire 2)

[English](#english) | [中文](#中文)

---

# 中文

## 📌 项目简介

AutoSLCut 是一个用于 **《Slay the Spire 2》视频创作辅助** 的 Mod。

它可以在你录制游戏时，自动记录 **SL（Save & Load）行为**，并在录制结束后生成剪辑数据，配合外部工具自动剪掉无效片段，大幅减少手动剪辑时间。

⚠️ 本 Mod **不影响游戏玩法**，仅服务于视频创作者。

---

## ✨ 功能特点

- 自动检测 Save / Load 行为
- 自动生成剪辑时间轴（JSON）
- 支持 OBS Studio 自动连接
- 配套 Python 工具一键剪辑视频
- 支持自定义 SL 偏移（适配不同机器）

---

## 🚀 使用方法

### 1️⃣ 安装 Mod

正常安装并启用本 Mod。

---

### 2️⃣ 开始录制

1. 启动游戏 & OBS Studio（顺序不限）
2. 开始录制（推荐使用 `.mkv` 格式）
3. 正常游戏（包含 SL 操作）

---

### 3️⃣ 录制结束

录制结束后，在 OBS 输出目录中会生成：

- `xxx.mkv`（原始视频）
- `xxx.json`（剪辑数据）

---

### 4️⃣ 自动剪辑

1. 将 `Cut.py` 放入该目录
2. 双击运行

脚本会自动：

- 查找同名 `.mkv` 和 `.json`
- 使用 FFmpeg 自动剪辑
- 输出最终视频：`xxx_cut.mkv`

---

## ⚙️ 参数调整（非常重要）

在 `Cut.py` 中：

```python
LOAD_OFFSET = 0.45
````

用于控制：

👉 Load 后额外裁剪的时间（秒）

---

### 如何调整？

这个值**不是固定的**，取决于：

* 你的电脑性能
* 游戏加载速度
* 使用的 SL 方式（官方 / 一键 SL）

👉 建议方法：

1. 先用默认值录制
2. 观察剪辑结果
3. 微调（±0.1 ~ 0.3）

---

## 🧰 环境要求

你需要：

* Python 3.x
* FFmpeg（已加入 PATH）

---

## 📌 适用人群

❌ 普通玩家（不推荐）
✅ 视频创作者（强烈推荐）

---

## 🎬 额外说明

* 本工具也可用于剪辑**其他视频**
* 你可以自由修改 `Cut.py`：

  * 输出格式（mp4 / mkv）
  * 编码参数
  * 剪辑策略

---

## ❤️ 支持作者

如果这个工具对你有帮助：

👉 欢迎在视频中注明：

```
Edited with AutoSLCut Mod
```

---

## 🔮 未来计划

如果使用人数较多，计划提供：

* 无需 Python 的独立剪辑工具
* GUI 界面
* 更智能的剪辑策略

---

# English

## 📌 Overview

AutoSLCut is a mod for **Slay the Spire 2** designed for content creators.

It automatically detects **Save & Load (SL)** events during recording and generates timeline data to help you cut out invalid segments with external tools.

⚠️ This mod does NOT affect gameplay.

---

## ✨ Features

* Detects Save / Load events
* Generates timeline JSON
* Auto-connects to OBS Studio
* One-click Python cutting tool
* Adjustable SL offset

---

## 🚀 Usage

### 1️⃣ Install Mod

Install and enable the mod.

---

### 2️⃣ Record Gameplay

1. Launch game & OBS (any order)
2. Start recording (recommended: `.mkv`)
3. Play normally (including SL)

---

### 3️⃣ After Recording

OBS output folder will contain:

* `xxx.mkv` (video)
* `xxx.json` (timeline)

---

### 4️⃣ Auto Cut

1. Put `Cut.py` into the folder
2. Run it

It will:

* Detect matching files
* Use FFmpeg to cut
* Output: `xxx_cut.mkv`

---

## ⚙️ Configuration

In `Cut.py`:

```python
LOAD_OFFSET = 0.45
```

Controls delay after Load.

---

### How to tune?

Depends on:

* PC performance
* Loading speed
* SL method

👉 Adjust manually.

---

## 🧰 Requirements

* Python 3.x
* FFmpeg in PATH

---

## 📌 Target Users

❌ Casual players
✅ Content creators

---

## 🎬 Notes

* Can be used for other videos
* Fully customizable script

---

## ❤️ Credits

If you use this mod:

```
Edited with AutoSLCut Mod
```

---

## 🔮 Future

* Standalone tool (no Python)
* GUI
* Smarter cutting

