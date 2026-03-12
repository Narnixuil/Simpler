# -*- coding: utf-8 -*-
"""Open Website"""

NAME = "Open Website"
DESCRIPTION = "Open a web page in the default browser"

URL = "https://example.com"

def run():
    import os
    import webbrowser
    try:
        os.startfile(URL)
    except Exception:
        webbrowser.open(URL, new=2)
