#!/usr/bin/env python3
"""
Simpler - Windows script launcher (single-file main.py)

Features:
- System tray app using pystray
- Global hotkey (keyboard) to show GUI
- Optional mouse middle-click to show GUI (pynput)
- Single-instance via local socket (127.0.0.1:57832)
- Scans `scripts` directory for Python scripts exposing `run()`
"""

# === Configuration ===
import sys
import os
import json
import threading
import socket
import logging
import logging.handlers
import subprocess
import tkinter as tk
from tkinter import ttk, messagebox
import time
import re
import ast
import queue
import traceback
import runpy

try:
	import keyboard
except Exception:
	keyboard = None

try:
	from pynput import mouse as pynput_mouse
except Exception:
	pynput_mouse = None

try:
	import pystray
	from PIL import Image, ImageDraw, ImageTk
except Exception:
	pystray = None

import winreg

# === Path Utilities ===
BASE_DIR = os.path.dirname(sys.executable) if getattr(sys, 'frozen', False) \
	else os.path.dirname(os.path.abspath(__file__))

def join_base(*parts):
	return os.path.join(BASE_DIR, *parts)

# === Configuration ===
DEFAULT_CONFIG = {
	"hotkey": "ctrl+`",
	"mouse_middle_key": False,
	"scripts_dir": "scripts",
	"theme": "dark",
	"window_width": 600,
	"log_file": "simpler.log"
}

CONFIG_PATH = join_base('config.json')

def load_config():
	cfg = DEFAULT_CONFIG.copy()
	if os.path.exists(CONFIG_PATH):
		try:
			with open(CONFIG_PATH, 'r', encoding='utf-8-sig') as f:
				user = json.load(f)
			if isinstance(user, dict):
				cfg.update(user)
		except Exception:
			# Invalid config, keep defaults and log later
			logger = logging.getLogger('simpler')
			logger.exception('Failed to load config.json, using defaults')
	else:
		try:
			with open(CONFIG_PATH, 'w', encoding='utf-8') as f:
				json.dump(cfg, f, indent=4, ensure_ascii=False)
		except Exception:
			pass
	# normalize scripts_dir
	cfg['scripts_dir'] = cfg.get('scripts_dir') or DEFAULT_CONFIG['scripts_dir']
	return cfg

# === Logging ===
logger = logging.getLogger('simpler')
logger.setLevel(logging.DEBUG)

# === Single Instance / IPC ===
IPC_HOST = '127.0.0.1'
IPC_PORT = 57832
ipc_server_socket = None
ipc_queue = queue.Queue()

_last_exec = {}
_last_exec_lock = threading.Lock()

def start_ipc_server():
	global ipc_server_socket
	s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
	s.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
	try:
		s.bind((IPC_HOST, IPC_PORT))
	except OSError:
		s.close()
		return False
	s.listen(5)
	ipc_server_socket = s

	def run():
		logger.info('IPC server listening on %s:%s', IPC_HOST, IPC_PORT)
		while True:
			try:
				conn, _ = s.accept()
				with conn:
					data = b''
					while True:
						chunk = conn.recv(1024)
						if not chunk:
							break
						data += chunk
					try:
						text = data.decode('utf-8').strip()
					except Exception:
						text = ''
					if text.upper().startswith('SHOW'):
						ipc_queue.put('SHOW')
			except Exception:
				logger.exception('IPC server error')
				time.sleep(1)

	t = threading.Thread(target=run, daemon=True)
	t.start()
	return True

def send_ipc_show():
	try:
		with socket.create_connection((IPC_HOST, IPC_PORT), timeout=1) as s:
			s.sendall(b'SHOW\n')
		return True
	except Exception:
		return False

# === Script Discovery ===
def discover_scripts(scripts_dir):
	results = []
	full_dir = join_base(scripts_dir)
	if not os.path.isdir(full_dir):
		return results
	for name in sorted(os.listdir(full_dir)):
		if not name.endswith('.py'):
			continue
		if name == '__init__.py' or name.startswith('_'):
			continue
		path = os.path.join(full_dir, name)
		meta = {'path': path, 'filename': name}
		try:
			data = open(path, 'rb').read()
			text = None
			for enc in ('utf-8-sig', 'utf-8', 'utf-16'):
				try:
					text = data.decode(enc)
					logger.info('Read script %s encoding=%s', path, enc)
					break
				except Exception:
					text = None
			if text is None:
				raise UnicodeDecodeError('utf-8', data, 0, 1, 'unknown encoding')
		except Exception:
			logger.exception('Failed to read script %s', path)
			meta['has_run'] = False
			meta['disabled_reason'] = 'Script encoding unreadable. Please save as UTF-8.'
			results.append(meta)
			continue
		# metadata via regex
		m = re.search(r"^NAME\s*=\s*(['\"])(.*?)\1", text, re.M)
		meta['name'] = m.group(2).strip() if m else os.path.splitext(name)[0]
		m = re.search(r"^DESCRIPTION\s*=\s*(['\"])(.*?)\1", text, re.M)
		meta['description'] = m.group(2).strip() if m else ''
		m = re.search(r"^ICON\s*=\s*(['\"])(.*?)\1", text, re.M)
		meta['icon'] = m.group(2).strip() if m else '??'
		# check for run() via ast
		try:
			tree = ast.parse(text, filename=path)
			has_run = any(isinstance(n, ast.FunctionDef) and n.name == 'run' for n in tree.body)
		except Exception:
			logger.exception('AST parse failed for %s', path)
			# fallback: quick regex detection
			has_run = re.search(r'^\s*def\s+run\s*\(', text, re.M) is not None
		meta['has_run'] = has_run
		logger.info('Discovered script: %s has_run=%s', path, has_run)
		if not has_run:
			meta['disabled_reason'] = 'Script missing run() or failed to parse.'
		results.append(meta)
	return results

# === Script Execution ===
def execute_script_inprocess(script_path):
	try:
		ns = runpy.run_path(script_path)
		fn = ns.get('run')
		if not callable(fn):
			raise RuntimeError('No run() found in script')
		fn()
		return 0
	except Exception:
		logger.exception('Failed to run script in-process: %s', script_path)
		return -1


def execute_script_async(script_path, on_finish=None):
	now = time.monotonic()
	with _last_exec_lock:
		last = _last_exec.get(script_path, 0)
		if now - last < 0.5:
			logger.info('Exec ignored (debounce): %s', script_path)
			return
		_last_exec[script_path] = now

	def run():
		logger.info('Starting script: %s', script_path)
		try:
			cwd = os.path.dirname(script_path) or BASE_DIR
			if getattr(sys, 'frozen', False):
				# When frozen, launching a new subprocess with the exe can
				# fail to initialize the embedded interpreter. Run in-process.
				code = execute_script_inprocess(script_path)
			else:
				# Execute run() inside the script to avoid no-op when the file
				# only defines run() but does not call it.
				code_str = (
					"import runpy,sys; "
					"ns=runpy.run_path(sys.argv[1]); "
					"fn=ns.get('run'); "
					"import sys as _s; "
					"(_s.exit('No run() found') if not callable(fn) else fn())"
				)
				proc = subprocess.Popen([sys.executable, '-c', code_str, script_path], cwd=cwd)
				code = proc.wait()
			logger.info('Script finished: %s exit=%s', script_path, code)
			if on_finish:
				on_finish(script_path, code)
		except Exception:
			logger.exception('Failed to execute %s', script_path)
			if on_finish:
				on_finish(script_path, -1)
	t = threading.Thread(target=run, daemon=True)
	t.start()

# === GUI ===
class SimplerGUI:
	def _load_icon_image(self, path):
		if not path:
			return None
		try:
			if path in self._icon_cache:
				return self._icon_cache[path]
			if not os.path.exists(path):
				return None
			if 'ImageTk' not in globals():
				return None
			img = Image.open(path).convert('RGBA')
			img = img.resize((32, 32), Image.LANCZOS)
			tk_img = ImageTk.PhotoImage(img)
			self._icon_cache[path] = tk_img
			return tk_img
		except Exception:
			return None

	def __init__(self, root, config):
		self.root = root
		self.config = config
		self.scripts_dir = config.get('scripts_dir')
		self.theme = config.get('theme', 'dark')
		self.window = None
		self.cards = []
		self.card_widgets = []
		self.filter_var = tk.StringVar()
		self.showing = False
		self.error_label = None
		self._last_click_time = 0.0
		self._placeholder_text = 'Search scripts...'
		self._icon_cache = {}
		self._default_icon_image = self._load_icon_image(join_base('assets', 'default_icon.png'))
		self.reload_scripts()

		# poll IPC queue
		self.root.after(200, self._poll_ipc)

	def reload_scripts(self):
		self.scripts = discover_scripts(self.scripts_dir)

	def _poll_ipc(self):
		try:
			while True:
				item = ipc_queue.get_nowait()
				if item == 'SHOW':
					self.show_window()
		except queue.Empty:
			pass
		self.root.after(200, self._poll_ipc)

	def _get_colors(self):
		if self.theme == 'light':
			return {'bg': '#f0f0f0', 'card': '#ffffff', 'card_hover': '#e8e8e8', 'text': '#000000'}
		return {'bg': '#1e1e1e', 'card': '#2d2d2d', 'card_hover': '#3a3a3a', 'text': '#ffffff'}

	def show_window(self):
		# ensure scripts fresh
		self.reload_scripts()
		if self.window and tk.Toplevel.winfo_exists(self.window):
			try:
				self.window.deiconify()
				self.window.lift()
				self.window.focus_force()
			except Exception:
				pass
			return

		colors = self._get_colors()
		w = self.config.get('window_width', 600)
		self.window = tk.Toplevel(self.root)
		self.window.overrideredirect(True)
		self.window.attributes('-topmost', True)
		self.window.configure(bg=colors['bg'])

		# position near mouse
		try:
			mx = self.root.winfo_pointerx()
			my = self.root.winfo_pointery()
		except Exception:
			mx = 100
			my = 100
		screen_w = self.window.winfo_screenwidth()
		screen_h = self.window.winfo_screenheight()
		width = w
		# estimate height: number of rows * card height + search
		cards_per_row = 3
		card_h = 70
		rows = max(1, (len(self.scripts) + cards_per_row - 1) // cards_per_row)
		est_h = min(500, 60 + rows * (card_h + 8) + 40)
		height = est_h
		x = mx
		y = my
		if x + width > screen_w:
			x = max(0, screen_w - width - 10)
		if y + height > screen_h:
			y = max(0, screen_h - height - 10)
		self.window.geometry(f"{width}x{height}+{x}+{y}")

		# close handlers
		self.window.bind('<Escape>', lambda e: self.close_window())
		self.window.bind('<FocusOut>', lambda e: self.root.after(150, self._check_focus_out))

		# Search box
		search_frame = tk.Frame(self.window, bg=colors['bg'])
		search_frame.pack(fill='x', padx=12, pady=(12, 6))
		entry = tk.Entry(search_frame, textvariable=self.filter_var, font=('Segoe UI', 11))
		entry.pack(fill='x', ipady=6)
		entry.insert(0, self._placeholder_text)
		entry.configure(fg='#888888')
		entry.focus_set()
		self.filter_var.trace_add('write', lambda *a: self._on_filter_change())

		def on_focus_in(e):
			if entry.get() == self._placeholder_text:
				entry.delete(0, 'end')
				entry.configure(fg=colors['text'])

		def on_focus_out(e):
			if not entry.get():
				entry.insert(0, self._placeholder_text)
				entry.configure(fg='#888888')

		entry.bind('<FocusIn>', on_focus_in)
		entry.bind('<FocusOut>', on_focus_out)

		# canvas for cards with scrollbar
		canvas = tk.Canvas(self.window, bg=colors['bg'], highlightthickness=0)
		canvas.pack(fill='both', expand=True, padx=12, pady=(0,12))
		inner = tk.Frame(canvas, bg=colors['bg'])
		vsb = tk.Scrollbar(self.window, orient='vertical', command=canvas.yview)
		vsb.place(relx=1.0, rely=0, relheight=1.0, anchor='ne')
		canvas.configure(yscrollcommand=vsb.set)
		canvas.create_window((0,0), window=inner, anchor='nw')

		self.card_container = inner
		inner.bind('<Configure>', lambda e: canvas.configure(scrollregion=canvas.bbox('all')))

		# error label
		self.error_label = tk.Label(self.window, text='', bg='#b22222', fg='#ffffff')

		self._refresh_cards()
		self.showing = True

	def _check_focus_out(self):
		if not self.window:
			return
		if self.window.focus_get() is None:
			self.close_window()

	def close_window(self):
		if self.window:
			try:
				self.window.destroy()
			except Exception:
				pass
		self.window = None
		self.showing = False
		self.filter_var.set('')

	def _refresh_cards(self):
		if not self.window or not tk.Toplevel.winfo_exists(self.window):
			return
		if not getattr(self, 'card_container', None):
			return
		try:
			if not self.card_container.winfo_exists():
				return
		except Exception:
			return
		# clear
		for w in self.card_widgets:
			try:
				w.destroy()
			except Exception:
				pass
		self.card_widgets = []

		q = (self.filter_var.get() or '').lower().strip()
		if q == self._placeholder_text.lower():
			q = ''
		visible = []
		for s in self.scripts:
			if not q or q in s.get('name','').lower() or q in s.get('description','').lower():
				visible.append(s)

		colors = self._get_colors()
		cards_per_row = 3
		pad = 8
		w_total = self.config.get('window_width', 600) - 24
		card_w = max(120, (w_total - pad*(cards_per_row-1)) // cards_per_row)

		row = 0
		col = 0
		for idx, s in enumerate(visible):
			frame = tk.Frame(self.card_container, width=card_w, height=70, bg=colors['card'])
			frame.grid(row=row, column=col, padx=(0 if col==0 else pad,0), pady=(0,8))
			frame.grid_propagate(False)
			# icon
			icon_value = s.get('icon','')
			icon_img = None
			if isinstance(icon_value, str) and icon_value:
				icon_path = icon_value
				if not os.path.isabs(icon_path) and (icon_path.lower().endswith(('.png', '.jpg', '.jpeg', '.gif', '.ico'))):
					icon_path = join_base(icon_path)
				icon_img = self._load_icon_image(icon_path)
			if not icon_img:
				icon_img = self._default_icon_image
			if icon_img:
				icon_lbl = tk.Label(frame, image=icon_img, bg=colors['card'])
				icon_lbl.image = icon_img
			else:
				icon_lbl = tk.Label(frame, text=icon_value or 'PY', bg=colors['card'], fg=colors['text'], font=('Segoe UI', 20), width=2)
			icon_lbl.pack(side='left', padx=8, pady=8)
			texts = tk.Frame(frame, bg=colors['card'])
			texts.pack(side='left', fill='both', expand=True, pady=8)
			name_lbl = tk.Label(texts, text=s.get('name',''), bg=colors['card'], fg=colors['text'], font=('Segoe UI', 13, 'bold'))
			name_lbl.pack(anchor='w')
			desc_lbl = tk.Label(texts, text=s.get('description',''), bg=colors['card'], fg='#9a9a9a', font=('Segoe UI', 11))
			desc_lbl.pack(anchor='w')

			disabled = not s.get('has_run', False)
			if disabled:
				frame.configure(bg='#555555')
				icon_lbl.configure(bg='#555555')
				texts.configure(bg='#555555')
				name_lbl.configure(bg='#555555')
				desc_lbl.configure(bg='#777777')

			card_widgets_list = [icon_lbl, texts, name_lbl, desc_lbl]

			def on_enter(e, f=frame, disabled=disabled, widgets=card_widgets_list):
				if not disabled and f.winfo_exists():
					f.configure(bg=colors['card_hover'])
					for w in widgets:
						try:
							w.configure(bg=colors['card_hover'])
						except Exception:
							pass
			def on_leave(e, f=frame, disabled=disabled, widgets=card_widgets_list):
				if not disabled and f.winfo_exists():
					f.configure(bg=colors['card'])
					for w in widgets:
						try:
							w.configure(bg=colors['card'])
						except Exception:
							pass
			frame.bind('<Enter>', on_enter)
			frame.bind('<Leave>', on_leave)
			for child in frame.winfo_children():
				child.bind('<Enter>', on_enter)
				child.bind('<Leave>', on_leave)

			def on_click(e, script=s, f=frame, disabled=disabled):
				logger.info('Card clicked: %s', script.get('path'))
				now = time.monotonic()
				if now - self._last_click_time < 0.35:
					logger.info('Click ignored (debounce)')
					return 'break'
				self._last_click_time = now
				# flash
				try:
					if f.winfo_exists():
						orig = f.cget('bg')
						f.configure(bg=('#b22222' if disabled else '#0078d4'))
						self.root.after(200, lambda: (f.configure(bg=orig) if f.winfo_exists() else None))
				except Exception:
					pass
				if disabled:
					# show reason banner
					try:
						reason = script.get('disabled_reason') or 'Script not executable'
						logger.warning('Script disabled: %s reason=%s', script.get('path'), reason)
						if self.window and tk.Toplevel.winfo_exists(self.window):
							self.error_label.configure(text=f"Script disabled: {reason}")
							self.error_label.place(relx=0, rely=0, relwidth=1)
							self.root.after(3000, lambda: self.error_label.place_forget())
					except Exception:
						pass
					return 'break'
				# execute
				execute_script_async(script['path'], on_finish=self._on_script_finish)
				self.close_window()
				return 'break'

			# bind click to frame and all descendants (recursive)
			def bind_all(widget):
				try:
					widget.bind('<Button-1>', on_click)
					widget.bind('<ButtonRelease-1>', on_click)
				except Exception:
					pass
				for ch in widget.winfo_children():
					bind_all(ch)
			bind_all(frame)

			self.card_widgets.append(frame)
			col += 1
			if col >= cards_per_row:
				col = 0
				row += 1

	def _on_script_finish(self, path, exitcode):
		if exitcode != 0:
			# show error banner temporarily
			try:
				if self.window and tk.Toplevel.winfo_exists(self.window):
					self.error_label.configure(text=f"Script failed: {os.path.basename(path)} (exit {exitcode})")
					self.error_label.place(relx=0, rely=0, relwidth=1)
					self.root.after(3000, lambda: self.error_label.place_forget())
			except Exception:
				pass

	def _on_filter_change(self):
		if not self.window or not tk.Toplevel.winfo_exists(self.window):
			return
		if not getattr(self, 'card_container', None):
			return
		try:
			if not self.card_container.winfo_exists():
				return
		except Exception:
			return
		val = self.filter_var.get()
		if val == self._placeholder_text:
			return
		self._refresh_cards()

# === Global Hotkey ===
hotkey_ok = False
hotkey_warning = False

def register_hotkey(hotkey_str, show_cb):
	global hotkey_ok, hotkey_warning
	if keyboard is None:
		logger.warning('keyboard module not available; hotkey disabled')
		hotkey_warning = True
		return False
	try:
		keyboard.add_hotkey(hotkey_str, lambda: ipc_queue.put('SHOW'))
		hotkey_ok = True
		logger.info('Registered hotkey: %s', hotkey_str)
		return True
	except Exception:
		logger.exception('Failed to register hotkey')
		hotkey_warning = True
		try_notify('Hotkey registration failed')
		return False

def try_notify(msg, title='Simpler'):
	try:
		# try win10toast
		from win10toast import ToastNotifier
		t = ToastNotifier()
		t.show_toast(title, msg, duration=5, threaded=True)
		return
	except Exception:
		pass
	try:
		from plyer import notification
		notification.notify(title=title, message=msg)
		return
	except Exception:
		pass
	try:
		messagebox.showwarning(title, msg)
	except Exception:
		pass

# === System Tray ===
tray_icon = None
tray_thread = None

def make_icon_image(color='#0078d4', warn=False):
	size = (32, 32)
	img = Image.new('RGBA', size, (0,0,0,0))
	draw = ImageDraw.Draw(img)
	r = 14
	cx, cy = 16, 16
	draw.ellipse((cx-r, cy-r, cx+r, cy+r), fill=color)
	if warn:
		draw.ellipse((24-6, 6-2, 30-2, 12+2), fill='#ff0000')
	return img

def open_scripts_dir(scripts_dir):
	path = join_base(scripts_dir)
	if not os.path.exists(path):
		os.makedirs(path, exist_ok=True)
	try:
		subprocess.Popen(['explorer', path])
	except Exception:
		logger.exception('Failed to open scripts dir')

def create_tray(icon_title, gui, config):
	global tray_icon, tray_thread
	if pystray is None or Image is None:
		logger.warning('pystray or PIL not available; tray disabled')
		return

	def on_show(icon, item):
		ipc_queue.put('SHOW')

	def toggle_autostart(icon, item):
		if check_autostart():
			disable_autostart()
		else:
			enable_autostart()
		# regenerate menu
		icon.menu = make_menu()

	def on_open_scripts(icon, item):
		open_scripts_dir(config.get('scripts_dir'))

	def on_quit(icon, item):
		try:
			icon.stop()
		except Exception:
			pass
		try:
			gui.root.after(0, gui.root.quit)
		except Exception:
			pass

	def make_menu():
		return pystray.Menu(
			pystray.MenuItem('Show Panel', on_show),
			pystray.MenuItem('Run at Startup', toggle_autostart, checked=lambda item: check_autostart()),
			pystray.MenuItem('Open Scripts Folder', on_open_scripts),
			pystray.Menu.SEPARATOR,
			pystray.MenuItem('Quit', on_quit)
		)

	image = make_icon_image('#0078d4', warn=hotkey_warning)
	tray_icon = pystray.Icon("Simpler", image, icon_title, menu=make_menu())

	def run_icon():
		try:
			tray_icon.run()
		except Exception:
			logger.exception('Tray icon failed')

	tray_thread = threading.Thread(target=run_icon, daemon=True)
	tray_thread.start()

# === Startup ===
RUN_KEY = r"Software\Microsoft\Windows\CurrentVersion\Run"
RUN_NAME = 'Simpler'

def enable_autostart():
	try:
		key = winreg.OpenKey(winreg.HKEY_CURRENT_USER, RUN_KEY, 0, winreg.KEY_SET_VALUE)
		exe = sys.executable
		winreg.SetValueEx(key, RUN_NAME, 0, winreg.REG_SZ, exe)
		winreg.CloseKey(key)
		logger.info('Enabled autostart: %s', exe)
		return True
	except Exception:
		logger.exception('Failed to enable autostart')
		return False

def disable_autostart():
	try:
		key = winreg.OpenKey(winreg.HKEY_CURRENT_USER, RUN_KEY, 0, winreg.KEY_SET_VALUE)
		try:
			winreg.DeleteValue(key, RUN_NAME)
		except FileNotFoundError:
			pass
		winreg.CloseKey(key)
		logger.info('Disabled autostart')
		return True
	except Exception:
		logger.exception('Failed to disable autostart')
		return False

def check_autostart():
	try:
		key = winreg.OpenKey(winreg.HKEY_CURRENT_USER, RUN_KEY, 0, winreg.KEY_READ)
		val, _ = winreg.QueryValueEx(key, RUN_NAME)
		winreg.CloseKey(key)
		return bool(val)
	except Exception:
		return False

# === Entry Point ===
def main():
	config = load_config()
	# reconfigure logging to desired file
	try:
		log_path = join_base(config.get('log_file', DEFAULT_CONFIG['log_file']))
		for h in list(logger.handlers):
			logger.removeHandler(h)
		rh = logging.handlers.RotatingFileHandler(log_path, maxBytes=1_000_000, backupCount=2, encoding='utf-8')
		rh.setFormatter(logging.Formatter('%(asctime)s %(levelname)s %(name)s: %(message)s'))
		logger.addHandler(rh)
	except Exception:
		logger.exception('Failed to configure log file')

	# single instance
	bound = start_ipc_server()
	if not bound:
		# another instance exists -> send SHOW and exit
		sent = send_ipc_show()
		if sent:
			print('Another instance sent SHOW; exiting')
			return
		# if send failed, try to continue

	# create tkinter root
	root = tk.Tk()
	root.withdraw()

	gui = SimplerGUI(root, config)

	# register hotkey
	try:
		register_hotkey(config.get('hotkey', DEFAULT_CONFIG['hotkey']), lambda: ipc_queue.put('SHOW'))
	except Exception:
		logger.exception('Hotkey registration error')

	# mouse middle listener
	if config.get('mouse_middle_key') and pynput_mouse is not None:
		def on_click(x, y, button, pressed):
			try:
				if button == pynput_mouse.Button.middle and pressed:
					ipc_queue.put('SHOW')
			except Exception:
				pass
		try:
			ml = pynput_mouse.Listener(on_click=on_click)
			ml.daemon = True
			ml.start()
		except Exception:
			logger.exception('Failed to start mouse listener')

	# create tray
	create_tray('Simpler', gui, config)

	# mainloop: process ipc_queue via gui polling; start tkinter mainloop
	try:
		root.mainloop()
	except KeyboardInterrupt:
		pass

if __name__ == '__main__':
	try:
		main()
	except Exception:
		logger.exception('Unhandled exception in main')

# === Dependencies ===
# Required pip packages:
# keyboard, pynput, pystray, Pillow, pywin32

















