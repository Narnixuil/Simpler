# Scripts Library (Repo)

这是脚本库根目录，用来存放可复用的脚本。你可以按需把脚本复制到发布包里与 `Simpler.exe` 同级的 `scripts/` 文件夹中，
启动器会自动识别并生成脚本卡片。

## 使用方式
1. 从这里挑选脚本
2. 复制到发布包中的 `scripts/`
3. 启动 Simpler，即可在面板看到脚本

## 规范
- 每个脚本必须提供 `run()` 函数
- 建议使用 UTF‑8 编码保存
- 可选字段：`NAME`、`DESCRIPTION`、`ICON`
