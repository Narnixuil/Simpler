# Simpler

A lightweight Windows tray app that runs small text and file scripts from a searchable launcher.
Built on Windows.

## Requirements
- .NET 8 SDK (Windows)
- Python 3 (optional, required for `.py` scripts)

## Run
```powershell
dotnet run --project "D:\Programs\Simpler\Simpler.Host\Simpler.Host.csproj"
```

## Usage
- Press `Ctrl + \`` to open/close the launcher.
- Click a script card to run it.
- Click outside the launcher to close it.
- Right-click a card and choose `Edit` to open the script JSON file.
- Click the `+` button to create a new script from `scripts/Template.json`.

## Scripts
Scripts live in `scripts/` at the solution root (or next to the exe in a release zip).
They are not bundled into the exe, so you can add/remove/modify scripts freely.

The release zip includes `scripts/README.md` and `scripts/Template.json`.
- https://github.com/Narnixuil/Simpler/tree/main/scripts

### Available Scripts
- Batch Download Links: Download files from URLs in selected text
- Clean Paste: Normalize copied text (remove extra spaces/lines)
- EVER Rename: Batch rename files with a dedicated editor
- Folder Preview: Preview folder tree in a dialog
- Lower Case: Convert selected text to lower case
- Move to New Folder: Move selected files/folders into a newly created folder
- Pin Window: Toggle topmost for the active window
- Power Paste: Queue clipboard text and paste sequentially
- Prefix by Folder: Prefix selected files with their parent folder name
- Screenshot Blur: Screenshot with blurred background highlight
- Screenshot Highlight: Screenshot with dimmed background highlight
- Search Everything: Search selected text or filename in Everything
- Title Case: Convert selected text to title case
- Unlock and Delete: Force close locking processes and delete selected files or folders
- Upper Case: Convert selected text to upper case
- Web Visual Edit: Enable in-page visual editing by typing a javascript: URL into the address bar
### C# (.csx)
Entry point:
```csharp
async Task Run(ScriptContext context)
```

### JavaScript (.js)
Entry point:
```js
function run(context) { }
```

### Python (.py)
Entry point:
```py
def run(ctx):
    pass
```

### PowerShell (.ps1)
Entry point:
```powershell
function Run {
    param($ctx)
}
```

## Logging
- Logs are written only in Debug/development runs (`dotnet run`).
- Path: `bin/Debug/net8.0-windows/logs/simpler.log`
- Release builds (`Simpler.exe`) do not write runtime logs by default.
