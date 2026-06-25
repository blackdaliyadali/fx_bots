"""
Fundedelite Bridge v1
======================
- Monitors JSON files from cBot
- Uses pyautogui to click MT5 on external monitor
- MT5 must be MAXIMIZED FULLSCREEN on external monitor always
- Supports XAGUSD and XAUUSD
"""

import time
import json
import os
import math
import shutil
import pyautogui
import pygetwindow as gw
import pyperclip
import logging
from datetime import datetime

# ═══════════════════════════════════════════════════════════════
# CONFIG
# ═══════════════════════════════════════════════════════════════

ACCOUNT_FOLDER   = r"D:\bridge\account1\fundedelite"
MT5_WINDOW_TITLE = "186200"       # Fundedelite account number in MT5 title bar

MT5_RISK_USD      = 250.0
MT5_CONTRACT_SIZE = 5000.0
MT5_MIN_LOT       = 0.01
MT5_LOT_STEP      = 0.01

SYMBOLS = {
    "silver": "XAGUSD",
    "gold":   "XAUUSD",
}

# ── COORDINATES (External Monitor - Fundedelite MT5 Fullscreen) ──
COORD_SYMBOL = (850, 342)   # Symbol dropdown
COORD_VOL    = (824, 386)   # Volume/Lot field
COORD_SL     = (827, 416)   # Stop Loss field
COORD_TP     = (830, 444)   # Take Profit field
COORD_SELL   = (805, 680)   # Sell by Market button
COORD_BUY    = (996, 682)   # Buy by Market button

# Symbol names in MT5 for each instrument
SYMBOL_NAMES = {
    "silver": "XAGUSD",
    "gold":   "XAUUSD",
}

# ═══════════════════════════════════════════════════════════════

CHECK_INTERVAL = 1.0

pyautogui.FAILSAFE = False
pyautogui.PAUSE    = 0.25

# ── LOGGING ──
log_file = os.path.join(ACCOUNT_FOLDER, "fundedelite_log.txt")
os.makedirs(ACCOUNT_FOLDER, exist_ok=True)

logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s | %(levelname)s | %(message)s',
    handlers=[
        logging.FileHandler(log_file, encoding='utf-8'),
        logging.StreamHandler()
    ]
)
log = logging.getLogger(__name__)

# ── STATE ──
last_trade_ids = {prefix: None for prefix in SYMBOLS}

# ── HELPERS ──

def get_bridge_file(prefix):
    return os.path.join(ACCOUNT_FOLDER, f"{prefix}_bridge.json")

def read_json(filepath):
    try:
        if not os.path.exists(filepath):
            return None
        with open(filepath, 'r') as f:
            return json.load(f)
    except Exception as e:
        log.error(f"Read error {filepath}: {e}")
        return None

def archive_file(filepath, prefix):
    try:
        processed = os.path.join(ACCOUNT_FOLDER, "processed")
        os.makedirs(processed, exist_ok=True)
        ts   = datetime.now().strftime("%Y%m%d_%H%M%S")
        dest = os.path.join(processed, f"{prefix}_{ts}.json")
        shutil.move(filepath, dest)
        log.info(f"Archived: {dest}")
    except Exception as e:
        log.error(f"Archive error: {e}")

def calc_lots(sl_distance):
    if sl_distance <= 0:
        return MT5_MIN_LOT
    raw  = MT5_RISK_USD / (sl_distance * MT5_CONTRACT_SIZE)
    lots = math.floor(raw / MT5_LOT_STEP) * MT5_LOT_STEP
    lots = round(lots, 2)
    return max(lots, MT5_MIN_LOT)

def click_field(coord):
    """Click field and select all existing text."""
    pyautogui.click(coord[0], coord[1])
    time.sleep(0.3)
    pyautogui.hotkey('ctrl', 'a')
    time.sleep(0.15)

def focus_mt5():
    """Bring MT5 window to foreground."""
    import win32gui
    import win32con
    wins = [w for w in gw.getAllWindows()
            if MT5_WINDOW_TITLE.lower() in w.title.lower()]
    if not wins:
        log.error(f"MT5 not found: {MT5_WINDOW_TITLE}")
        return False
    try:
        hwnd = wins[0]._hWnd
        win32gui.ShowWindow(hwnd, win32con.SW_MAXIMIZE)
        win32gui.SetForegroundWindow(hwnd)
        time.sleep(1.0)
        log.info(f"MT5 focused: {wins[0].title}")
        return True
    except Exception as e:
        log.error(f"Focus error: {e}")
        return False

def set_symbol(symbol_name):
    """Click symbol field and type correct symbol."""
    pyautogui.click(COORD_SYMBOL[0], COORD_SYMBOL[1])
    time.sleep(0.5)
    pyautogui.hotkey('ctrl', 'a')
    time.sleep(0.2)
    pyperclip.copy(symbol_name)
    pyautogui.hotkey('ctrl', 'v')
    time.sleep(0.5)
    pyautogui.press('enter')
    time.sleep(0.5)
    log.info(f"Symbol set: {symbol_name}")

def close_order_window():
    """Close any open order window."""
    wins = [w for w in gw.getAllWindows()
            if "order" in w.title.lower()]
    if wins:
        pyautogui.press('escape')
        time.sleep(0.5)

def execute_trade(direction, lots, sl, tp, symbol_name, prefix):
    log.info(f"Executing {symbol_name} {direction} {lots}L SL={sl} TP={tp}")

    if not focus_mt5():
        return False

    close_order_window()
    time.sleep(0.5)

    # Open order window
    pyautogui.press('f9')
    time.sleep(2.5)

    # Set correct symbol
    set_symbol(SYMBOL_NAMES[prefix])
    time.sleep(0.5)

    # Volume
    click_field(COORD_VOL)
    pyperclip.copy(str(lots))
    pyautogui.hotkey('ctrl', 'v')
    log.info(f"Volume: {lots}")
    time.sleep(0.3)

    # Stop Loss
    click_field(COORD_SL)
    pyperclip.copy(str(round(sl, 3)))
    pyautogui.hotkey('ctrl', 'v')
    log.info(f"SL: {sl}")
    time.sleep(0.3)

    # Take Profit
    click_field(COORD_TP)
    pyperclip.copy(str(round(tp, 3)))
    pyautogui.hotkey('ctrl', 'v')
    log.info(f"TP: {tp}")
    time.sleep(0.5)

    # Buy or Sell
    if direction.upper() == 'SELL':
        pyautogui.click(COORD_SELL[0], COORD_SELL[1])
        log.info("Clicked SELL")
    else:
        pyautogui.click(COORD_BUY[0], COORD_BUY[1])
        log.info("Clicked BUY")

    time.sleep(2.0)

    # Verify order window closed
    still_open = any("order" in w.title.lower() for w in gw.getAllWindows())
    if not still_open:
        log.info(f"{symbol_name} {direction} executed OK!")
        return True
    else:
        log.warning("Order window still open - retrying")
        if direction.upper() == 'SELL':
            pyautogui.click(COORD_SELL[0], COORD_SELL[1])
        else:
            pyautogui.click(COORD_BUY[0], COORD_BUY[1])
        time.sleep(1.5)
        still_open2 = any("order" in w.title.lower() for w in gw.getAllWindows())
        if not still_open2:
            log.info("Retry worked!")
            return True
        pyautogui.press('escape')
        return False

def process_symbol(prefix, symbol_name):
    global last_trade_ids

    filepath = get_bridge_file(prefix)
    data     = read_json(filepath)
    if not data or not data.get('trade_id'):
        return

    trade_id  = str(data.get('trade_id', ''))
    direction = data.get('direction', '').upper()
    entry     = float(data.get('entry', 0))
    sl        = float(data.get('sl', 0))
    tp        = float(data.get('tp', 0))
    timestamp = data.get('timestamp', '')

    if trade_id == last_trade_ids[prefix]:
        return

    last_trade_ids[prefix] = trade_id
    log.info(f"[{symbol_name}] New trade {trade_id}: {direction} Entry={entry} SL={sl} TP={tp}")

    if not direction or sl <= 0 or tp <= 0:
        log.warning(f"[{symbol_name}] Invalid data, skipping.")
        archive_file(filepath, prefix)
        return

    sl_dist = abs(entry - sl)
    lots    = calc_lots(sl_dist)
    log.info(f"[{symbol_name}] Lots: {lots} (SL dist={sl_dist:.3f})")

    success = execute_trade(direction, lots, sl, tp, symbol_name, prefix)

    if success:
        log.info(f"[{symbol_name}] Trade {trade_id} done!")
    else:
        log.error(f"[{symbol_name}] Trade {trade_id} may have failed")

    archive_file(filepath, prefix)

# ── MAIN LOOP ──

def main():
    log.info("=" * 55)
    log.info("Fundedelite Bridge v1 Started")
    log.info(f"Account folder: {ACCOUNT_FOLDER}")
    log.info(f"MT5 window:     {MT5_WINDOW_TITLE}")
    log.info(f"Risk: ${MT5_RISK_USD} | Contract: {MT5_CONTRACT_SIZE}")
    log.info(f"Monitoring: {list(SYMBOLS.values())}")
    log.info(f"External monitor MT5 must be FULLSCREEN")
    log.info("=" * 55)

    while True:
        try:
            for prefix, symbol_name in SYMBOLS.items():
                process_symbol(prefix, symbol_name)
        except KeyboardInterrupt:
            log.info("Stopped by user.")
            break
        except Exception as e:
            log.error(f"Loop error: {e}")

        time.sleep(CHECK_INTERVAL)

if __name__ == "__main__":
    main()