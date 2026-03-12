# -*- coding: utf-8 -*-
"""Open Notepad"""

NAME = "Open Notepad"
DESCRIPTION = "Launch Windows Notepad"

def run():
    import subprocess
    subprocess.Popen("notepad.exe")
