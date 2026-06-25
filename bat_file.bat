@echo off
title Fundedelite Bridge
cd /d "D:\bridge\account3\fundedelite"
:start
python fundedelite_bridge.py
timeout /t 5
goto start