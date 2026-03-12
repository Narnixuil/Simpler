# Simpler

一个轻量的 Windows 脚本启动器（托盘 + 全局热键），用于快速运行本地 Python 脚本。

## 功能概览
- 系统托盘应用（pystray）
- 全局热键唤起面板（keyboard）
- 可选鼠标中键唤起面板（pynput）
- 单实例运行（本地 IPC）
- 自动扫描 `scripts/` 目录脚本
- UI 面板支持搜索过滤

## 快速开始
1. 安装依赖：
   - `python -m pip install -r requirements.txt`
2. 运行：
   - `python main.py`
3. 打开面板：
   - 默认热键：`Ctrl + ``（Esc 下方的反引号键）`
   - 或托盘菜单：`显示面板`

## 使用流程
1. 编写脚本并放入 `scripts/` 目录
2. 在脚本中提供 `run()` 函数（启动器会调用该函数）
3. 使用热键/托盘菜单打开面板
4. 点击卡片运行脚本

## 脚本规范
把脚本放在 `scripts/` 目录下（默认），并提供 `run()` 函数：

```python
# -*- coding: utf-8 -*-
"""打开记事本"""

NAME = "打开记事本"
DESCRIPTION = "启动 Windows 记事本"

# ICON = "📝"  # 可选：用 emoji 做图标

def run():
    import subprocess
    subprocess.Popen("notepad.exe")
```

可选字段：
- `NAME`：显示名称
- `DESCRIPTION`：描述
- `ICON`：图标（emoji 或字符）

### 一个网页脚本示例
```python
# -*- coding: utf-8 -*-
"""打开网站"""

NAME = "打开网站"
DESCRIPTION = "使用默认浏览器打开一个网页"

URL = "https://example.com"

def run():
    import os
    import webbrowser
    try:
        os.startfile(URL)
    except Exception:
        webbrowser.open(URL, new=2)
```

## 脚本库（Library）
- 仓库根目录有 `Simplerscripts/`，用于集中存放/管理可复用脚本。
- 发布包里与 `Simpler.exe` 同级的 `scripts/` 才是运行时加载目录。
- 你可以从 `Simplerscripts/` 里挑选脚本复制到发布包的 `scripts/`。

## 配置
配置文件：`config.json`

可用项：
- `hotkey`：全局热键（默认 `ctrl+``）
- `mouse_middle_key`：是否启用鼠标中键（true/false）
- `scripts_dir`：脚本目录（相对 BASE_DIR）
- `theme`：`dark` 或 `light`
- `window_width`：窗口宽度（像素）
- `log_file`：日志文件名

示例：
```json
{
  "hotkey": "ctrl+`",
  "mouse_middle_key": false,
  "scripts_dir": "scripts",
  "theme": "dark",
  "window_width": 600,
  "log_file": "simpler.log"
}
```

## 托盘菜单说明
- `显示面板`：打开脚本面板
- `开机自启动`：切换是否随系统启动
- `打开脚本目录`：打开脚本文件夹
- `退出`：退出程序

## 日志
日志文件位于 `BASE_DIR/simpler.log`，用于排查热键注册、脚本执行与异常。

## 单实例机制
程序使用本地端口 `127.0.0.1:57832` 作为 IPC 通道：
- 若已有实例在运行，新启动的实例会向已有实例发送 `SHOW` 指令并退出

## 常见问题
- 面板打不开：检查热键是否被系统/安全软件拦截，必要时以管理员权限运行。
- 脚本显示灰色：脚本无法解析或缺少 `run()`。
- 点击无反应：查看 `simpler.log` 是否有 `Starting script` 日志。
- 中文乱码：确保脚本保存为 UTF‑8 编码。

## 目录结构
```
simpler/
├── main.py
├── config.json
├── requirements.txt
├── README.md
└── scripts/
    ├── open_notepad.py
    └── open_website.py
```
