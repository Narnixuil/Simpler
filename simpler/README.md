# Simpler

A lightweight Windows script launcher (tray app + global hotkey) for quickly running local Python scripts.

## Features
- System tray app (pystray)
- Global hotkey to open the panel (keyboard)
- Optional middle-click to open the panel (pynput)
- Single-instance via local IPC
- Auto-scan `scripts/` directory
- UI panel with search filter

## Quick Start
1. Install dependencies:
   - `python -m pip install -r requirements.txt`
2. Run:
   - `python main.py`
3. Open the panel:
   - Default hotkey: `Ctrl + `` (backtick key under Esc)
   - Or tray menu: `Show Panel`

## Usage Flow
1. Put your scripts into `scripts/`
2. Provide a `run()` function in each script
3. Open the panel via hotkey/tray menu
4. Click a card to run the script

## Script Requirements
Put scripts under `scripts/` and provide a `run()` function:

```python
# -*- coding: utf-8 -*-
"""Open Notepad"""

NAME = "Open Notepad"
DESCRIPTION = "Launch Windows Notepad"

# ICON = "icon.png"  # Optional: icon file name

def run():
    import subprocess
    subprocess.Popen("notepad.exe")
```

Optional metadata:
- `NAME`: Display name
- `DESCRIPTION`: Description
- `ICON`: Icon (emoji or text)

### Web script example
```python
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
```

## Scripts Library
- The repository root has `Simplerscripts/` for storing reusable scripts.
- The runtime `scripts/` folder is the one next to `Simpler.exe` in the release package.
- Copy scripts you want from `Simplerscripts/` into the release `scripts/` folder.

## Configuration
Config file: `config.json`

Available options:
- `hotkey`: Global hotkey (default `ctrl+``)
- `mouse_middle_key`: Enable middle mouse button (true/false)
- `scripts_dir`: Scripts directory (relative to BASE_DIR)
- `theme`: `dark` or `light`
- `window_width`: Window width (pixels)
- `log_file`: Log file name

Example:
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

## Tray Menu
- `Show Panel`: Open the script panel
- `Run at Startup`: Toggle autostart
- `Open Scripts Folder`: Open the scripts directory
- `Quit`: Exit the app

## Logs
Log file location: `BASE_DIR/simpler.log` for hotkey registration, script execution, and errors.

## Single-Instance
The app uses `127.0.0.1:57832` for IPC:
- If an instance is already running, a new instance sends `SHOW` and exits.

## FAQ
- Panel not showing: Check whether the hotkey is blocked; try running as admin.
- Script card is gray: The script failed to parse or is missing `run()`.
- Click does nothing: Check `simpler.log` for `Starting script`.
- Garbled text: Save scripts as UTF-8.

## Directory Structure
```
simpler/
|-- main.py
|-- config.json
|-- requirements.txt
|-- README.md
`-- scripts/
    |-- open_notepad.py
    `-- open_website.py
```
