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
