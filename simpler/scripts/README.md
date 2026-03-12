# Scripts Library

这是 Simpler 的脚本库（Library）。发布包中会有一个与 `Simpler.exe` 同级的 `scripts/` 文件夹，
你把脚本放进这个文件夹，面板就会自动识别并生成脚本卡片。

## 适用场景
- 你可以把这里当成“脚本插件仓库”。
- 只需把需要的脚本复制到发布包的 `scripts/` 目录即可生效。

## 基本约定
每个脚本都必须提供一个 `run()` 函数，启动器会调用它。

可选元数据：
- `NAME`：显示名称
- `DESCRIPTION`：描述
- `ICON`：图标（emoji 或字符）

## 示例
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

## 注意事项
- 请使用 UTF‑8 编码保存脚本（避免中文乱码）。
- 不建议在 `run()` 里做长时间阻塞任务；需要的话请自行开线程或子进程。
