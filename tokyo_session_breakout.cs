// ╔══════════════════════════════════════════════════════════════════╗
// ║   ☕ Gold Tokyo Breakout Bot                                     ║
// ║   Symbol    : XAUUSD                                            ║
// ║   Timeframe : M15                                               ║
// ║   Logic     : Trade breakout of Tokyo session range             ║
// ║   Entry     : Candle CLOSE above/below Tokyo High/Low           ║
// ║   SL        : Opposite side of Tokyo range                      ║
// ║   TP        : Configurable R:R (default 1:1)                    ║
// ║   v2        : Late attach reconstruction added                  ║
// ╚══════════════════════════════════════════════════════════════════╝

using System;
using cAlgo.API;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class GoldTokyoBreakout : Robot
    {
        // ── SESSION SETTINGS ────────────────────────────────────────────
        [Parameter("GMT Offset (your broker server)", Group = "⏰ Session", DefaultValue = 4, MinValue = -12, MaxValue = 14)]
        public int GmtOffset { get; set; }

        [Parameter("Tokyo Session Start Hour (local)", Group = "⏰ Session", DefaultValue = 4, MinValue = 0, MaxValue = 23)]
        public int TokyoStartHour { get; set; }

        [Parameter("Tokyo Session Start Minute", Group = "⏰ Session", DefaultValue = 0, MinValue = 0, MaxValue = 59)]
        public int TokyoStartMin { get; set; }

        [Parameter("Tokyo Session End Hour (local)", Group = "⏰ Session", DefaultValue = 13, MinValue = 0, MaxValue = 23)]
        public int TokyoEndHour { get; set; }

        [Parameter("Tokyo Session End Minute", Group = "⏰ Session", DefaultValue = 0, MinValue = 0, MaxValue = 59)]
        public int TokyoEndMin { get; set; }

        [Parameter("EOD Close Hour — close trade if still open (local)", Group = "⏰ Session", DefaultValue = 22, MinValue = 14, MaxValue = 23)]
        public int EodHour { get; set; }

        // ── TRADE SETTINGS ──────────────────────────────────────────────
        [Parameter("Risk Amount ($)", Group = "💰 Trade", DefaultValue = 10.0, MinValue = 1.0)]
        public double RiskAmount { get; set; }

        [Parameter("Risk Reward (1 = 1:1, 1.5 = 1:1.5)", Group = "💰 Trade", DefaultValue = 1.0, MinValue = 0.5, MaxValue = 10.0)]
        public double RiskReward { get; set; }

        [Parameter("Partial Close at 50% TP? (cut 50% lot)", Group = "💰 Trade", DefaultValue = true)]
        public bool UsePartialClose { get; set; }

        [Parameter("Break Even after Partial Close?", Group = "💰 Trade", DefaultValue = true)]
        public bool UseBreakEven { get; set; }

        // ── DISPLAY SETTINGS ────────────────────────────────────────────
        [Parameter("Dashboard on Right side? (false = Left)", Group = "🖥 Display", DefaultValue = false)]
        public bool DashRight { get; set; }

        // ═══════════════════════════════════════════════════════════════
        // PRIVATE STATE
        // ═══════════════════════════════════════════════════════════════
        private const string BOT = "Gold_Tokyo_Breakout";
        private const string PFX = "GTB_";

        private double   _tokyoHigh         = double.MinValue;
        private double   _tokyoLow          = double.MaxValue;
        private bool     _sessionRangeReady = false;
        private bool     _tradeEnteredToday = false;
        private string   _botStatus         = "STARTING UP";
        private string   _entryDirection    = "---";
        private double   _lastLotSize       = 0;
        private double   _tradeEntry        = 0;
        private double   _tradeSL           = 0;
        private double   _tradeTP           = 0;
        private double   _halfTpLevel       = 0;
        private bool     _partialCloseDone  = false;
        private bool     _breakEvenDone     = false;
        private DateTime _currentDay        = DateTime.MinValue;
        private DateTime _lastDashUpdate    = DateTime.MinValue;

        // ═══════════════════════════════════════════════════════════════
        // ON START
        // ═══════════════════════════════════════════════════════════════
        protected override void OnStart()
        {
            TLog("═══════════════════════════════════");
            TLog($"☕ Gold Tokyo Breakout v2 — STARTED");
            TLog($"Symbol     : {SymbolName}");
            TLog($"GMT Offset : +{GmtOffset}");
            TLog($"Session    : {TokyoStartHour:00}:{TokyoStartMin:00} → {TokyoEndHour:00}:{TokyoEndMin:00} (local)");
            TLog($"Risk       : ${RiskAmount}  R:R 1:{RiskReward}");
            TLog("═══════════════════════════════════");

            if (TimeFrame != TimeFrame.Minute15)
                TLog("⚠️ WARNING: Bot designed for M15.");

            Positions.Closed += OnPositionClosed;
            ResetDaily();

            // ── RECONSTRUCT TODAY'S STATE FROM HISTORY ─────────────────
            ReconstructToday();

            UpdateDashboard();
        }

        // ═══════════════════════════════════════════════════════════════
        // RECONSTRUCT TODAY — scan historical bars on startup
        // Allows late attach at any time during the day
        // ═══════════════════════════════════════════════════════════════
        private void ReconstructToday()
        {
            var localNow  = ToLocal(Server.Time);
            _currentDay   = localNow.Date;

            TLog($"🔄 Reconstructing today: {_currentDay:yyyy-MM-dd}");

            var sessionStart = new TimeSpan(TokyoStartHour, TokyoStartMin, 0);
            var sessionEnd   = new TimeSpan(TokyoEndHour,   TokyoEndMin,   0);

            double rH = double.MinValue;
            double rL = double.MaxValue;
            bool   rangeBuilt = false;

            // Check if position already open
            foreach (var pos in Positions)
            {
                if (pos.Label == BOT && pos.SymbolName == SymbolName)
                {
                    _tradeEnteredToday = true;
                    _tradeEntry        = pos.EntryPrice;
                    _tradeSL           = pos.StopLoss  ?? 0;
                    _tradeTP           = pos.TakeProfit ?? 0;
                    _entryDirection    = pos.TradeType == TradeType.Buy ? "▲ BUY" : "▼ SELL";
                    _lastLotSize       = Symbol.VolumeInUnitsToQuantity(pos.VolumeInUnits);
                    _botStatus         = $"🔴 IN TRADE — {_entryDirection} (recovered)";
                    TLog($"✅ Existing position found: {_entryDirection} @ {_tradeEntry:F2}");
                    return;
                }
            }

            // Scan today's bars oldest → newest
            int totalBars = Bars.Count;
            for (int i = totalBars - 1; i >= 1; i--)
            {
                var barTime  = ToLocal(Bars.OpenTimes[i]);
                var barDate  = barTime.Date;
                var barTimeOfDay = barTime.TimeOfDay;

                // Stop scanning if we go before today
                if (barDate < _currentDay) break;
                if (barDate != _currentDay) continue;

                // Only process session bars
                if (barTimeOfDay < sessionStart) continue;

                // Build range during session
                if (barTimeOfDay >= sessionStart && barTimeOfDay < sessionEnd)
                {
                    double bH = Bars.HighPrices[i];
                    double bL = Bars.LowPrices[i];
                    if (bH > rH) rH = bH;
                    if (bL < rL) rL = bL;
                    rangeBuilt = true;
                }

                // After session — check for breakout on closed bars
                if (barTimeOfDay >= sessionEnd && rangeBuilt)
                {
                    if (!_sessionRangeReady)
                    {
                        _tokyoHigh         = rH;
                        _tokyoLow          = rL;
                        _sessionRangeReady = true;
                        TLog($"📊 Range reconstructed: Hi={_tokyoHigh:F2} Lo={_tokyoLow:F2}");
                    }

                    // Check if breakout already happened
                    double barClose = Bars.ClosePrices[i];
                    if (!_tradeEnteredToday)
                    {
                        if (barClose > _tokyoHigh)
                        {
                            TLog($"▲ Historical BUY breakout detected at {barTime:HH:mm}");
                            // Don't enter — it already happened, mark as entered to avoid duplicate
                            _tradeEnteredToday = true;
                            _botStatus = "⏳ BREAKOUT OCCURRED — no position (missed or closed)";
                        }
                        else if (barClose < _tokyoLow)
                        {
                            TLog($"▼ Historical SELL breakout detected at {barTime:HH:mm}");
                            _tradeEnteredToday = true;
                            _botStatus = "⏳ BREAKOUT OCCURRED — no position (missed or closed)";
                        }
                    }
                }
            }

            // Apply reconstructed range if session bars found but session not ended yet
            if (rangeBuilt && !_sessionRangeReady)
            {
                _tokyoHigh = rH;
                _tokyoLow  = rL;
                var localTimeNow = ToLocal(Server.Time).TimeOfDay;

                if (localTimeNow < sessionEnd)
                {
                    // Still inside session — range still building
                    _botStatus = $"BUILDING RANGE  Hi:{_tokyoHigh:F2}  Lo:{_tokyoLow:F2}";
                    TLog($"📊 Range building: Hi={_tokyoHigh:F2} Lo={_tokyoLow:F2} (session still active)");
                }
                else
                {
                    // Session ended but range not locked yet
                    _sessionRangeReady = true;
                    _botStatus = "⏳ WAITING FOR BREAKOUT";
                    TLog($"📊 Range locked (post-session): Hi={_tokyoHigh:F2} Lo={_tokyoLow:F2}");
                }
            }

            if (!rangeBuilt)
            {
                _botStatus = "⏳ WAITING FOR SESSION";
                TLog("No session bars found for today yet.");
            }

            DrawTokyoLines();
            TLog($"✅ Reconstruction complete. Status: {_botStatus}");
        }

        // ═══════════════════════════════════════════════════════════════
        // ON BAR — main logic runs on candle close
        // ═══════════════════════════════════════════════════════════════
        protected override void OnBar()
        {
            DateTime localTime = ToLocal(Server.Time);

            // ── STEP 0: New day reset ──────────────────────────────────
            if (IsNewDay(localTime))
            {
                TLog($"── 📅 NEW DAY: {localTime:yyyy-MM-dd} ──");
                ResetDaily();
                _currentDay = localTime.Date;
            }

            // ── STEP 1: Build Tokyo range ──────────────────────────────
            if (IsInSession(localTime))
            {
                double barHigh = Bars.HighPrices.Last(1);
                double barLow  = Bars.LowPrices.Last(1);

                if (barHigh > _tokyoHigh) _tokyoHigh = barHigh;
                if (barLow  < _tokyoLow)  _tokyoLow  = barLow;

                _botStatus = $"BUILDING RANGE  Hi:{_tokyoHigh:F2}  Lo:{_tokyoLow:F2}";
                DrawTokyoLines();
                UpdateDashboard();
                return;
            }

            // ── STEP 2: Session just ended → lock range ────────────────
            if (!_sessionRangeReady
                && _tokyoHigh != double.MinValue
                && _tokyoLow  != double.MaxValue)
            {
                double rangePips = (_tokyoHigh - _tokyoLow) / Symbol.PipSize;
                TLog($"📊 Tokyo range locked | Hi={_tokyoHigh:F2} Lo={_tokyoLow:F2} | {rangePips:F0} pips");
                _sessionRangeReady = true;
                _botStatus         = "⏳ WAITING FOR BREAKOUT";
                DrawTokyoLines();
                UpdateDashboard();
                return;
            }

            // ── STEP 3: EOD — close any open trade ────────────────────
            if (localTime.Hour >= EodHour && localTime.Minute == 0)
            {
                bool closedSomething = CloseMyPositions("EOD time exit");
                if (closedSomething)
                    TLog($"🕐 EOD close triggered at {localTime:HH:mm}");
                _botStatus = "🌙 DAY COMPLETE — waiting for new day";
                UpdateDashboard();
                return;
            }

            // ── STEP 4: Breakout detection ─────────────────────────────
            if (_sessionRangeReady && !_tradeEnteredToday)
            {
                double prevClose = Bars.ClosePrices.Last(1);

                if (prevClose > _tokyoHigh)
                {
                    TLog($"▲ BREAKOUT UP | close {prevClose:F2} > Hi {_tokyoHigh:F2}");
                    EnterTrade(TradeType.Buy);
                }
                else if (prevClose < _tokyoLow)
                {
                    TLog($"▼ BREAKOUT DOWN | close {prevClose:F2} < Lo {_tokyoLow:F2}");
                    EnterTrade(TradeType.Sell);
                }
                else
                {
                    _botStatus = $"⏳ WAITING FOR BREAKOUT  [{_tokyoHigh:F2} / {_tokyoLow:F2}]";
                }
            }

            UpdateDashboard();
        }

        // ═══════════════════════════════════════════════════════════════
        // ON TICK
        // ═══════════════════════════════════════════════════════════════
        protected override void OnTick()
        {
            if (_tradeEnteredToday && _halfTpLevel > 0 && !_partialCloseDone)
            {
                bool halfHit = false;
                if (_entryDirection == "▲ BUY"  && Symbol.Bid >= _halfTpLevel) halfHit = true;
                if (_entryDirection == "▼ SELL" && Symbol.Ask <= _halfTpLevel) halfHit = true;

                if (halfHit)
                {
                    TLog($"📍 50% TP hit — partial close");

                    for (int i = Positions.Count - 1; i >= 0; i--)
                    {
                        var pos = Positions[i];
                        if (pos.Label != BOT || pos.SymbolName != SymbolName) continue;

                        double halfVol = Math.Round(pos.VolumeInUnits / 2.0
                                         / Symbol.VolumeInUnitsStep) * Symbol.VolumeInUnitsStep;
                        halfVol = Math.Max(halfVol, Symbol.VolumeInUnitsMin);

                        if (halfVol >= pos.VolumeInUnits)
                        {
                            ClosePosition(pos);
                            TLog("⚠️ Lot too small to halve — closed full position");
                        }
                        else
                        {
                            ClosePosition(pos, halfVol);
                            TLog($"✂️ Partial close: {halfVol} units");

                            if (UseBreakEven && !_breakEvenDone)
                            {
                                ModifyPosition(pos, _tradeEntry, pos.TakeProfit, ProtectionType.Absolute);
                                _breakEvenDone = true;
                                _tradeSL       = _tradeEntry;
                                TLog($"🔒 Break Even: SL → {_tradeEntry:F2}");
                                DrawSLTPLines(_tradeSL, _tradeTP, pos.TradeType);
                            }
                        }

                        _partialCloseDone = true;
                        _botStatus = _breakEvenDone ? "♻️ PARTIAL + BE SET" : "♻️ PARTIAL CLOSED";
                        UpdateDashboard();
                        break;
                    }
                }
            }

            if ((Server.Time - _lastDashUpdate).TotalSeconds >= 2)
            {
                UpdateDashboard();
                _lastDashUpdate = Server.Time;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // ENTER TRADE
        // ═══════════════════════════════════════════════════════════════
        private void EnterTrade(TradeType direction)
        {
            double entry, sl, tp, slDistance;

            if (direction == TradeType.Buy)
            {
                entry      = Symbol.Ask;
                sl         = _tokyoLow;
                slDistance = entry - sl;
                tp         = entry + (slDistance * RiskReward);
            }
            else
            {
                entry      = Symbol.Bid;
                sl         = _tokyoHigh;
                slDistance = sl - entry;
                tp         = entry - (slDistance * RiskReward);
            }

            if (slDistance <= 0)
            {
                TLog($"❌ ABORT: SL distance ≤ 0");
                _botStatus = "❌ ERROR: SL distance invalid";
                UpdateDashboard();
                return;
            }

            double lots        = CalcLotSize(slDistance);
            long   volumeUnits = (long)Symbol.QuantityToVolumeInUnits(lots);

            TLog($"📤 {direction} | Entry≈{entry:F2} SL={sl:F2} TP={tp:F2} Lots={lots:F2}");

            var result = ExecuteMarketOrder(direction, SymbolName, volumeUnits, BOT);

            if (result.IsSuccessful)
            {
                ModifyPosition(result.Position, sl, tp, ProtectionType.Absolute);
                _tradeEnteredToday = true;
                _entryDirection    = direction == TradeType.Buy ? "▲ BUY" : "▼ SELL";
                _lastLotSize       = lots;
                _tradeEntry        = result.Position.EntryPrice;
                _tradeSL           = sl;
                _tradeTP           = tp;
                _partialCloseDone  = false;
                _breakEvenDone     = false;

                double tpDist = Math.Abs(tp - _tradeEntry);
                _halfTpLevel  = direction == TradeType.Buy
                                ? _tradeEntry + (tpDist * 0.5)
                                : _tradeEntry - (tpDist * 0.5);

                _botStatus = $"🔴 IN TRADE — {_entryDirection}";
                DrawSLTPLines(sl, tp, direction);
                TLog($"✅ Filled | Entry={result.Position.EntryPrice:F2} SL={sl:F2} TP={tp:F2}");
            }
            else
            {
                _botStatus = $"❌ ORDER FAILED: {result.Error}";
                TLog($"❌ Failed: {result.Error}");
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // LOT SIZE
        // ═══════════════════════════════════════════════════════════════
        private double CalcLotSize(double slDistancePrice)
        {
            double contractSize = Symbol.LotSize > 0 ? Symbol.LotSize : 100;
            double riskPerLot   = slDistancePrice * contractSize;

            if (riskPerLot <= 0) return Symbol.VolumeInUnitsToQuantity(Symbol.VolumeInUnitsMin);

            double lots     = RiskAmount / riskPerLot;
            double minLots  = Symbol.VolumeInUnitsToQuantity(Symbol.VolumeInUnitsMin);
            double maxLots  = Symbol.VolumeInUnitsToQuantity(Symbol.VolumeInUnitsMax);
            double stepLots = Symbol.VolumeInUnitsToQuantity(Symbol.VolumeInUnitsStep);

            lots = Math.Max(lots, minLots);
            lots = Math.Min(lots, maxLots);
            if (stepLots > 0) lots = Math.Round(lots / stepLots) * stepLots;

            TLog($"📐 Lots: ${RiskAmount} / ({slDistancePrice:F2} × {contractSize}) = {lots:F2}");
            return lots;
        }

        // ═══════════════════════════════════════════════════════════════
        // HELPERS
        // ═══════════════════════════════════════════════════════════════
        private bool CloseMyPositions(string reason)
        {
            bool closed = false;
            for (int i = Positions.Count - 1; i >= 0; i--)
            {
                var pos = Positions[i];
                if (pos.Label == BOT && pos.SymbolName == SymbolName)
                {
                    ClosePosition(pos);
                    TLog($"🔒 Closed {pos.Id} ({reason})");
                    closed = true;
                }
            }
            return closed;
        }

        private void OnPositionClosed(PositionClosedEventArgs args)
        {
            var pos = args.Position;
            if (pos.Label != BOT || pos.SymbolName != SymbolName) return;
            double pnl = pos.NetProfit;
            _botStatus = pnl >= 0 ? $"✅ WIN +${pnl:F2}" : $"❌ LOSS -${Math.Abs(pnl):F2}";
            TLog($"📋 Closed | {_botStatus} | Reason: {args.Reason}");
            RemoveObject(PFX + "SL"); RemoveObject(PFX + "TP");
            RemoveObject(PFX + "SLL"); RemoveObject(PFX + "TPL");
            UpdateDashboard();
        }

        private void ResetDaily()
        {
            _tokyoHigh         = double.MinValue;
            _tokyoLow          = double.MaxValue;
            _sessionRangeReady = false;
            _tradeEnteredToday = false;
            _entryDirection    = "---";
            _lastLotSize       = 0;
            _tradeEntry        = 0;
            _tradeSL           = 0;
            _tradeTP           = 0;
            _halfTpLevel       = 0;
            _partialCloseDone  = false;
            _breakEvenDone     = false;
            _botStatus         = "⏳ WAITING FOR SESSION";

            RemoveObject(PFX + "TH");  RemoveObject(PFX + "TL");
            RemoveObject(PFX + "THL"); RemoveObject(PFX + "TLL");
            RemoveObject(PFX + "SL");  RemoveObject(PFX + "TP");
            RemoveObject(PFX + "SLL"); RemoveObject(PFX + "TPL");
            TLog("🔄 Daily reset");
        }

        private void DrawTokyoLines()
        {
            if (_tokyoHigh == double.MinValue || _tokyoLow == double.MaxValue) return;
            Chart.DrawHorizontalLine(PFX + "TH",  _tokyoHigh, Color.Gold, 2, LineStyle.Lines);
            Chart.DrawHorizontalLine(PFX + "TL",  _tokyoLow,  Color.Gold, 2, LineStyle.Lines);
            Chart.DrawText(PFX + "THL", $"  📌 Tokyo High: {_tokyoHigh:F2}", Bars.Count - 1, _tokyoHigh, Color.Gold);
            Chart.DrawText(PFX + "TLL", $"  📌 Tokyo Low:  {_tokyoLow:F2}",  Bars.Count - 1, _tokyoLow,  Color.Gold);
        }

        private void DrawSLTPLines(double sl, double tp, TradeType type)
        {
            Chart.DrawHorizontalLine(PFX + "SL",  sl, Color.OrangeRed,  3, LineStyle.Solid);
            Chart.DrawHorizontalLine(PFX + "TP",  tp, Color.LimeGreen,  3, LineStyle.Solid);
            Chart.DrawText(PFX + "SLL", $"  ⛔ SL: {sl:F2}", Bars.Count - 1, sl, Color.OrangeRed);
            Chart.DrawText(PFX + "TPL", $"  🎯 TP: {tp:F2}", Bars.Count - 1, tp, Color.LimeGreen);
        }

        private void RemoveObject(string name)
        {
            try { Chart.RemoveObject(name); } catch { }
        }

        private void UpdateDashboard()
        {
            DateTime local = ToLocal(Server.Time);
            double openPnl = 0;
            foreach (var pos in Positions)
                if (pos.Label == BOT && pos.SymbolName == SymbolName)
                    openPnl += pos.NetProfit;

            double rangePips = (_tokyoHigh != double.MinValue && _tokyoLow != double.MaxValue)
                             ? (_tokyoHigh - _tokyoLow) / Symbol.PipSize : 0;

            string hiStr   = _tokyoHigh != double.MinValue ? _tokyoHigh.ToString("F2") : "---";
            string loStr   = _tokyoLow  != double.MaxValue ? _tokyoLow.ToString("F2")  : "---";
            string lotsStr = _lastLotSize > 0 ? _lastLotSize.ToString("F2") : "---";
            string entryStr= _tradeEntry > 0  ? _tradeEntry.ToString("F2")  : "---";
            string slStr   = _tradeSL > 0     ? _tradeSL.ToString("F2")     : "---";
            string tpStr   = _tradeTP > 0     ? _tradeTP.ToString("F2")     : "---";
            string halfStr = _halfTpLevel > 0 ? _halfTpLevel.ToString("F2") : "---";
            string pnlStr  = openPnl != 0     ? $"${openPnl:+0.00;-0.00}"  : "$0.00";
            string pcStr   = !UsePartialClose ? "OFF" : _partialCloseDone ? "✅ DONE" : "⏳ WAITING";
            string beStr   = !UseBreakEven    ? "OFF" : _breakEvenDone    ? "✅ DONE" : "⏳ WAITING";

            string text =
                $"☕  Gold Tokyo Breakout  ·  XAUUSD M15\n"    +
                $"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n"         +
                $"  Symbol   : {SymbolName,-10}  TF : M15\n"   +
                $"  Date     : {local:ddd dd-MMM-yyyy}\n"       +
                $"  Time     : {local:HH:mm:ss}  (GMT+{GmtOffset})\n" +
                $"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n"         +
                $"  Session  : {TokyoStartHour:00}:{TokyoStartMin:00} → {TokyoEndHour:00}:{TokyoEndMin:00}\n" +
                $"  Tokyo Hi : {hiStr,-12}  Lo : {loStr}\n"    +
                $"  Range    : {rangePips:F0} pips\n"           +
                $"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n"         +
                $"  STATUS   : {_botStatus}\n"                  +
                $"  Direction: {_entryDirection}\n"             +
                $"  Entry    : {entryStr}\n"                    +
                $"  SL       : {slStr}\n"                       +
                $"  TP       : {tpStr}\n"                       +
                $"  50% TP   : {halfStr}\n"                     +
                $"  Lot Size : {lotsStr}\n"                     +
                $"  R:R      : 1 : {RiskReward}\n"             +
                $"  Risk $   : ${RiskAmount:F2}\n"              +
                $"  Open P&L : {pnlStr}\n"                      +
                $"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n"         +
                $"  Partial  : {pcStr}\n"                       +
                $"  BreakEven: {beStr}\n"                       +
                $"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━";

            Chart.DrawStaticText(PFX + "DASH", text,
                VerticalAlignment.Top,
                DashRight ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                Color.White);
        }

        private DateTime ToLocal(DateTime utcTime) => utcTime.AddHours(GmtOffset);
        private bool IsNewDay(DateTime t) => t.Date != _currentDay;
        private bool IsInSession(DateTime t)
        {
            var ts    = t.TimeOfDay;
            var start = new TimeSpan(TokyoStartHour, TokyoStartMin, 0);
            var end   = new TimeSpan(TokyoEndHour,   TokyoEndMin,   0);
            return ts >= start && ts < end;
        }
        private void TLog(string msg) => Print($"[Gold_Tokyo] {msg}");

        protected override void OnStop()
        {
            CloseMyPositions("Bot stopped");
            TLog("☕ Stopped.");
        }
    }
}