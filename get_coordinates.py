"""
MT5 Field Coordinate Finder v2
================================
Captures all MT5 New Order window coordinates including Symbol field.

HOW TO USE:
1. Open MT5 on external monitor → maximize fullscreen
2. Open XAGUSD chart
3. Press F9 → New Order window opens
4. Run this script
5. Hover mouse over each field when prompted → wait 3 seconds
6. Screenshot final results
"""

import pyautogui
import time

fields = [
    "SYMBOL dropdown field",
    "VOLUME / LOT field",
    "STOP LOSS field",
    "TAKE PROFIT field",
    "SELL BY MARKET button",
    "BUY BY MARKET button",
]

print("=" * 55)
print("  MT5 Coordinate Finder v2")
print("=" * 55)
print("  Make sure:")
print("  1. MT5 is MAXIMIZED FULLSCREEN on external monitor")
print("  2. F9 New Order window is OPEN")
print("  3. Don't move MT5 window during capture")
print("=" * 55)
print()
input("Press Enter when ready to start...")
print()

results = []

for i, field in enumerate(fields):
    print(f"[{i+1}/{len(fields)}] Hover mouse over: {field}")
    for countdown in range(3, 0, -1):
        print(f"  Capturing in {countdown}...", end='\r')
        time.sleep(1)
    x, y = pyautogui.position()
    print(f"  Captured: X={x}, Y={y}          ")
    results.append((field, x, y))
    print()
    time.sleep(0.5)

print()
print("=" * 55)
print("  ALL COORDINATES CAPTURED")
print("=" * 55)
for field, x, y in results:
    print(f"  {field}:")
    print(f"    X={x}, Y={y}")
    print()
print("=" * 55)
print()
print("  COPY FOR SCRIPT:")
print("=" * 55)
names = ["COORD_SYMBOL", "COORD_VOL", "COORD_SL", "COORD_TP", "COORD_SELL", "COORD_BUY"]
for (field, x, y), name in zip(results, names):
    print(f"  {name} = ({x}, {y})")
print("=" * 55)
print()
time.sleep(1)
print("\n>>> TAKE SCREENSHOT NOW <<<")
print(">>> Press Enter only AFTER screenshot <<<")
input("")