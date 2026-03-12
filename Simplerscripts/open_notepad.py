# -*- coding: utf-8 -*-
"""打开记事本"""

NAME = "打开记事本"
DESCRIPTION = "启动 Windows 记事本"

def run():
    import subprocess
    subprocess.Popen("notepad.exe")
