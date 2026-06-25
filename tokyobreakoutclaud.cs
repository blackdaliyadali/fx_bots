using System;
using cAlgo.API;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class TokyoBreakoutBot : Robot
    {
        [Parameter("Range Start Time (HH:mm)", DefaultValue = "04:00")]
        public string RangeStartTimeStr { get; set; }

        [Parameter("GMT Offset (hours)", DefaultValue = 4, MinValue = -12, MaxValue = 14)]
        public int GmtOffset { get; set; }

        [Parameter("Breakout Min Distance", DefaultValue = 0.05, MinValue = 0.001)]
        public double BreakoutMinDistance { get; set; }

        [Parameter("Entry Buffer", DefaultValue = 0.070, MinValue = 0.001)]
        public double EntryBuffer { get; set; }

        [Parameter("SL Buffer", DefaultValue = 0.020, MinValue = 0.001)]
        public double SLBuffer { get; set; }

        [Parameter("TP Distance", DefaultValue = 1.000, MinValue = 0.01)]
        public double TPDistance { get; set; }

        [Parameter("Risk Amount USD", DefaultValue = 4900, MinValue = 1)]
        public double RiskAmountUSD { get; set; }

        [Parameter("Contract Size", DefaultValue = 500, MinValue = 1)]
        public double ContractSize { get; set; }

        [Parameter("Enable Midnight Close", DefaultValue = true)]
        public bool EnableMidnightClose { get; set; }

        [Parameter("Midnight Close Time (HH:mm)", DefaultValue = "00:00")]
        public string MidnightCloseTimeStr { get; set; }

        // ── Constants ─────────────────────────────────────────────────────────
        private const string Label   = "tokyo breakout";
        private const double MinLots = 0.01;
        private const double LotStep = 0.01;

        // ── Config ────────────────────────────────────────────────────────────
        private TimeSpan _sessionStart;   // e.g. 04:00
        private TimeSpan _sessionEnd;     // sessionStart + 1h = 05:00
        private TimeSpan _midnightTime;
        private bool     _configOk;

        // ── Daily state ───────────────────────────────────────────────────────
        private DateTime _today = DateTime.MinValue;
        private bool     _tradeDone;
        private bool     _tradeOpened;

        // ── Range ─────────────────────────────────────────────────────────────
        private double _rH, _rL;
        private bool   _rangeReady;   // true after first session bar processed
        private bool   _rangeFrozen;  // true after breakout detected

        // ── Breakout ──────────────────────────────────────────────────────────
        private bool      _bDetected;
        private TradeType _bDir;
        private double    _bClose;
        private double    _bCH, _bCL;
        private double    _entry, _sl, _tp, _lots;

        // First breakout candle (stored permanently — used for opposite SL)
        private bool      _firstBreakoutSet;
        private TradeType _firstBreakoutDir;
        private double    _firstBCH, _firstBCL;

        private const string Panel = "TBPanel";

        // ── OnStart ───────────────────────────────────────────────────────────
        protected override void OnStart()
        {
            _configOk = true;

            if (!TryParseTime(RangeStartTimeStr, out _sessionStart))
            { Print("ERROR: Bad RangeStartTime"); _configOk = false; }
            else
            { _sessionEnd = _sessionStart.Add(TimeSpan.FromHours(1)); }

            if (!TryParseTime(MidnightCloseTimeStr, out _midnightTime))
            { Print("ERROR: Bad MidnightCloseTime"); _configOk = false; }

            if (_configOk)
            {
                Print($"Bot OK. Session={_sessionStart}-{_sessionEnd} GMT+{GmtOffset}");
                ReconstructToday(); // rebuild today's state from existing bars
            }

            RefreshPanel();
        }

        // ── Reconstruct today on live attach ──────────────────────────────────
        private void ReconstructToday()
        {
            var now      = Server.Time.AddHours(GmtOffset);
            _today       = now.Date;
            Print($"Reconstructing today: {_today:yyyy-MM-dd}");

            // Collect today's bars first (oldest to newest)
            var todayBars = new System.Collections.Generic.List<Bar>();
            for (int i = 0; i < Bars.Count - 1; i++) // exclude current forming bar
            {
                var b     = Bars[i];
                var bLoc  = b.OpenTime.AddHours(GmtOffset);
                if (bLoc.Date == _today) todayBars.Add(b);
            }
            Print($"Found {todayBars.Count} bars for today.");

            foreach (var b in todayBars)
            {
                var bLoc   = b.OpenTime.AddHours(GmtOffset);
                var bDate  = bLoc.Date;
                var bTime  = bLoc.TimeOfDay;

                // Only process today's session bars
                if (bDate != _today) continue;
                if (bTime < _sessionStart) continue;

                // Phase 1: first hour — build initial range
                if (bTime < _sessionEnd)
                {
                    if (!_rangeReady)
                    {
                        _rH = b.High; _rL = b.Low;
                        _rangeReady = true;
                    }
                    else
                    {
                        if (b.High > _rH) _rH = b.High;
                        if (b.Low  < _rL) _rL = b.Low;
                    }
                    continue;
                }

                // Phase 2: after first hour
                if (!_rangeReady) continue;

                if (!_bDetected)
                {
                    if (b.Close > _rH + BreakoutMinDistance)
                    { _rangeFrozen = true; SetBreakout(TradeType.Buy, b); }
                    else if (b.Close < _rL - BreakoutMinDistance)
                    { _rangeFrozen = true; SetBreakout(TradeType.Sell, b); }
                    else
                    {
                        if (b.High > _rH) _rH = b.High;
                        if (b.Low  < _rL) _rL = b.Low;
                    }
                }
                else
                {
                    if (_bDir == TradeType.Buy  && b.Close < _rL - BreakoutMinDistance)
                    { ClearBreakout(); SetBreakout(TradeType.Sell, b); }
                    else if (_bDir == TradeType.Sell && b.Close > _rH + BreakoutMinDistance)
                    { ClearBreakout(); SetBreakout(TradeType.Buy, b); }
                }
            }

            // Check if position already open
            if (HasPos())
            {
                _tradeOpened = true;
                Print("Existing position found — managing it.");
            }

            Print($"Reconstruct done: rangeReady={_rangeReady} rH={_rH:F3} rL={_rL:F3} breakout={_bDetected}");
            DrawLines();
        }

        // ── OnBar ─────────────────────────────────────────────────────────────
        protected override void OnBar()
        {
            if (!_configOk) return;

            var closed     = Bars[Bars.Count - 2];
            var closedLoc  = closed.OpenTime.AddHours(GmtOffset);
            var closedDate = closedLoc.Date;
            var closedTime = closedLoc.TimeOfDay;

            var running    = Bars[Bars.Count - 1];
            var runningDate = running.OpenTime.AddHours(GmtOffset).Date;

            // ── New day from running candle ───────────────────────────────────
            if (runningDate > _today)
            {
                _today        = runningDate;
                _tradeDone    = false;
                _tradeOpened      = false;
                _rangeReady       = false;
                _firstBreakoutSet = false;
                _firstBCH = _firstBCL = 0;
                _rangeFrozen  = false;
                _rH = _rL     = 0;
                ClearBreakout();
                ClearLines();
                Print($"=== NEW DAY: {_today:yyyy-MM-dd} ===");
                RefreshPanel();
            }

            if (_tradeDone) return;

            // Only process today's closed candles
            if (closedDate != _today) return;

            // Only process candles at or after session start
            if (closedTime < _sessionStart) return;

            Print($"[{closedDate:MM-dd} {closedTime:hh\\:mm}] H={closed.High:F3} L={closed.Low:F3} C={closed.Close:F3}");

            // ── PHASE 1: Build initial range (04:00 to 05:00) ────────────────
            // All candles that OPENED in the first hour go into initial range
            if (closedTime < _sessionEnd)
            {
                if (!_rangeReady)
                {
                    _rH = closed.High;
                    _rL = closed.Low;
                    _rangeReady = true;
                    Print($"  Range INIT: H={_rH:F3} L={_rL:F3}");
                }
                else
                {
                    if (closed.High > _rH) _rH = closed.High;
                    if (closed.Low  < _rL) _rL = closed.Low;
                    Print($"  Range build (1h): H={_rH:F3} L={_rL:F3}");
                }
                DrawLines();
                RefreshPanel();
                return; // never check breakout inside first hour
            }

            // ── PHASE 2: After 05:00 — check breakout OR extend range ─────────
            if (!_rangeReady) return; // safety: no range built yet

            if (!_bDetected)
            {
                // Check breakout on this closed candle FIRST (before extending)
                if (closed.Close > _rH + BreakoutMinDistance)
                {
                    Print($"  Range FROZEN: H={_rH:F3} L={_rL:F3}");
                    _rangeFrozen = true;
                    SetBreakout(TradeType.Buy, closed);
                }
                else if (closed.Close < _rL - BreakoutMinDistance)
                {
                    Print($"  Range FROZEN: H={_rH:F3} L={_rL:F3}");
                    _rangeFrozen = true;
                    SetBreakout(TradeType.Sell, closed);
                }
                else
                {
                    // No breakout → extend range with wicks (only if not frozen)
                    if (!_rangeFrozen)
                    {
                        if (closed.High > _rH) _rH = closed.High;
                        if (closed.Low  < _rL) _rL = closed.Low;
                        Print($"  Range ext: H={_rH:F3} L={_rL:F3}");
                        DrawLines();
                    }
                }
            }
            else
            {
                // Opposite breakout check (range already frozen)
                if (_bDir == TradeType.Buy  && closed.Close < _rL - BreakoutMinDistance)
                {
                    Print("  Opposite SELL");
                    ClearBreakout();
                    SetBreakout(TradeType.Sell, closed);
                }
                else if (_bDir == TradeType.Sell && closed.Close > _rH + BreakoutMinDistance)
                {
                    Print("  Opposite BUY");
                    ClearBreakout();
                    SetBreakout(TradeType.Buy, closed);
                }
            }

            RefreshPanel();
        }

        // ── OnTick ────────────────────────────────────────────────────────────
        protected override void OnTick()
        {
            if (!_configOk || _tradeDone) return;

            // Midnight close
            if (EnableMidnightClose)
            {
                var t = Server.Time.AddHours(GmtOffset).TimeOfDay;
                if (t >= _midnightTime && t < _midnightTime.Add(TimeSpan.FromMinutes(1)))
                {
                    CloseAll("Midnight");
                    Done("Midnight close");
                    return;
                }
            }

            // SL/TP detection — position was opened but now gone
            if (_tradeOpened && !HasPos())
            {
                Done("SL/TP hit");
                return;
            }

            // Already in position
            if (HasPos()) return;

            // No breakout yet
            if (!_bDetected) return;

            // Check entry trigger
            bool triggered = _bDir == TradeType.Buy
                ? Symbol.Ask >= _entry
                : Symbol.Bid <= _entry;

            if (triggered)
            {
                Print($"Entry triggered! Ask={Symbol.Ask:F3} Bid={Symbol.Bid:F3} Trigger={_entry:F3}");
                Enter();
            }
        }

        // ── Breakout ──────────────────────────────────────────────────────────
        private void SetBreakout(TradeType dir, Bar b)
        {
            _bDetected = true;
            _bDir      = dir;
            _bCH       = b.High;
            _bCL       = b.Low;
            _bClose    = b.Close;

            // Store FIRST breakout candle permanently
            // First breakout candle extreme wick = new range boundary = SL reference
            if (!_firstBreakoutSet)
            {
                _firstBreakoutSet = true;
                _firstBreakoutDir = dir;
                _firstBCH         = b.High;
                _firstBCL         = b.Low;
                Print($"  First breakout stored: {dir} H={_firstBCH:F3} L={_firstBCL:F3}");

            }
            // Only update displayed range boundary on DIRECTION SWITCH
            // Normal first breakout: range stays as original 04:00-05:00 range
            if (_firstBreakoutSet && (_firstBreakoutDir != dir))
            {
                // Range extended — show new boundary
                if (dir == TradeType.Buy)  _rL = _firstBCL; // sell candle low = new range low
                else                       _rH = _firstBCH; // buy candle high = new range high
                Print($"  Range extended to: H={_rH:F3} L={_rL:F3}");
            }

            // Entry = breakout candle WICK + buffer (always)
            // SL logic:
            //   NO switch (first breakout) → SL = original range boundary + buffer
            //   SWITCH (opposite breakout) → SL = first breakout candle extreme + buffer
            //                                     (because range extended to include it)
            bool switched = _firstBreakoutSet && (_firstBreakoutDir != dir);

            if (dir == TradeType.Buy)
            {
                _entry = b.High + EntryBuffer;
                _sl    = switched
                         ? _firstBCL - SLBuffer   // switched: range extended down to first SELL candle low
                         : _rL       - SLBuffer;  // normal:   original range low
                _tp    = _entry + TPDistance;
            }
            else
            {
                _entry = b.Low  - EntryBuffer;
                _sl    = switched
                         ? _firstBCH + SLBuffer   // switched: range extended up to first BUY candle high
                         : _rH       + SLBuffer;  // normal:   original range high
                _tp    = _entry - TPDistance;
            }

            _lots = CalcLots(Math.Abs(_entry - _sl));
            Print($"  BREAKOUT {dir}: Entry={_entry:F3} SL={_sl:F3} TP={_tp:F3} Lots={_lots:F2}");
            DrawLines();
        }

        private void ClearBreakout()
        {
            _bDetected = false;
            _bCH = _bCL = _bClose = _entry = _sl = _tp = _lots = 0;
            // NOTE: _firstBreakoutSet intentionally NOT reset here
            // First breakout candle stays stored for SL reference on opposite trades
        }

        // ── Entry ─────────────────────────────────────────────────────────────
        private void Enter()
        {
            if (HasPos() || _tradeDone) return;
            if (_lots < MinLots || _entry <= 0 || _sl <= 0 || _tp <= 0)
            {
                Print("Enter: invalid params, skip");
                return;
            }

            var r = ExecuteMarketOrder(_bDir, SymbolName, _lots, Label, null, null);
            if (r.IsSuccessful)
            {
                r.Position.ModifyStopLossPrice(_sl);
                r.Position.ModifyTakeProfitPrice(_tp);
                _tradeOpened = true;
                Print($"TRADE OPEN: {_bDir} {_lots}L @ {r.Position.EntryPrice:F3} SL={_sl:F3} TP={_tp:F3}");
                RefreshPanel();
            }
            else
            {
                Print($"ORDER FAIL: {r.Error}");
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private bool HasPos()
        {
            foreach (var p in Positions)
                if (p.Label == Label && p.SymbolName == SymbolName) return true;
            return false;
        }

        private void CloseAll(string why)
        {
            foreach (var p in Positions)
                if (p.Label == Label && p.SymbolName == SymbolName)
                { ClosePosition(p); Print($"Closed: {why}"); }
        }

        private void Done(string why)
        {
            _tradeDone = true;
            Print($"Daily done: {why}");
            RefreshPanel();
        }

        private double CalcLots(double slDist)
        {
            if (slDist <= 0)
            {
                Print("SL dist=0, using min volume");
                return Symbol.VolumeInUnitsMin;
            }

            // Calculate raw lots then convert to broker volume units
            double rawLots    = RiskAmountUSD / (slDist * ContractSize);
            double rawUnits   = rawLots * Symbol.LotSize;

            // Floor to broker step
            double step       = Symbol.VolumeInUnitsStep;
            double flooredUnits = Math.Floor(rawUnits / step) * step;

            // Enforce broker minimum
            if (flooredUnits < Symbol.VolumeInUnitsMin)
            {
                Print($"Volume {flooredUnits} below min {Symbol.VolumeInUnitsMin}, using min.");
                flooredUnits = Symbol.VolumeInUnitsMin;
            }

            Print($"Lots calc: slDist={slDist:F3} rawLots={rawLots:F4} units={flooredUnits}");
            return flooredUnits;
        }

        private bool TryParseTime(string s, out TimeSpan t)
        {
            t = TimeSpan.Zero;
            if (string.IsNullOrWhiteSpace(s)) return false;
            var p = s.Trim().Split(':');
            if (p.Length != 2) return false;
            if (!int.TryParse(p[0], out int h) || !int.TryParse(p[1], out int m)) return false;
            if (h < 0 || h > 23 || m < 0 || m > 59) return false;
            t = new TimeSpan(h, m, 0);
            return true;
        }

        // ── Chart Lines ───────────────────────────────────────────────────────
        private void DrawLines()
        {
            Chart.RemoveObject("L_RH");
            Chart.RemoveObject("L_RL");
            Chart.RemoveObject("L_EN");
            Chart.RemoveObject("L_SL");
            Chart.RemoveObject("L_TP");

            if (_rangeReady)
            {
                Chart.DrawHorizontalLine("L_RH", _rH, Color.Cyan,   1, LineStyle.Dots);
                Chart.DrawHorizontalLine("L_RL", _rL, Color.Cyan,   1, LineStyle.Dots);
            }
            if (_bDetected)
            {
                Chart.DrawHorizontalLine("L_EN", _entry, Color.Yellow, 2, LineStyle.Solid);
                Chart.DrawHorizontalLine("L_SL", _sl,    Color.Red,    1, LineStyle.Dots);
                Chart.DrawHorizontalLine("L_TP", _tp,    Color.Lime,   1, LineStyle.Dots);
            }
        }

        private void ClearLines()
        {
            Chart.RemoveObject("L_RH");
            Chart.RemoveObject("L_RL");
            Chart.RemoveObject("L_EN");
            Chart.RemoveObject("L_SL");
            Chart.RemoveObject("L_TP");
        }

        // ── Panel ─────────────────────────────────────────────────────────────
        private void RefreshPanel()
        {
            Chart.RemoveObject(Panel);

            string state =
                !_configOk    ? "CONFIG ERROR"                               :
                _tradeDone    ? "DAILY COMPLETED"                            :
                !_rangeReady  ? $"WAITING SESSION ({_sessionStart})"         :
                !_bDetected   ? (_rangeFrozen ? "RANGE FROZEN - NO ENTRY YET"
                                              : $"BUILDING RANGE ({_sessionStart}-{_sessionEnd})")  :
                                $"WAITING ENTRY [{_bDir.ToString().ToUpper()}]";

            Color col =
                !_configOk  ? Color.Red            :
                _tradeDone  ? Color.CornflowerBlue :
                _bDetected  ? Color.Yellow         :
                !_rangeReady? Color.Orange         :
                              Color.WhiteSmoke;

            string rH   = _rangeReady  ? _rH.ToString("F3")    : "---";
            string rL   = _rangeReady  ? _rL.ToString("F3")    : "---";
            string dir  = _bDetected   ? _bDir.ToString().ToUpper() : "---";
            string bcH  = _bCH > 0    ? _bCH.ToString("F3")   : "---";
            string bcL  = _bCL > 0    ? _bCL.ToString("F3")   : "---";
            string bCls = _bClose > 0 ? _bClose.ToString("F3") : "---";
            string ent  = _entry > 0  ? _entry.ToString("F3")  : "---";
            string slS  = _sl > 0     ? _sl.ToString("F3")     : "---";
            string tpS  = _tp > 0     ? _tp.ToString("F3")     : "---";
            double lotsDisplay = _lots > 0 && Symbol.LotSize > 0
                             ? _lots / Symbol.LotSize : _lots;
            string lots = _lots > 0 ? lotsDisplay.ToString("F2") : "---";
            string day  = _today == DateTime.MinValue ? "Detecting..." : _today.ToString("yyyy-MM-dd");
            string tz   = GmtOffset >= 0 ? $"UTC+{GmtOffset}" : $"UTC{GmtOffset}";

            Chart.DrawStaticText(Panel,
                $"─── TOKYO BREAKOUT BOT ───────\n" +
                $"STATE:         {state}\n"          +
                $"Trading Day:   {day}\n"            +
                $"─────────────────────────────\n"  +
                $"Range High:    {rH}\n"             +
                $"Range Low:     {rL}\n"             +
                $"Direction:     {dir}\n"            +
                $"BC High:       {bcH}\n"            +
                $"BC Low:        {bcL}\n"            +
                $"BC Close:      {bCls}\n"           +
                $"Entry Trigger: {ent}\n"            +
                $"Stop Loss:     {slS}\n"            +
                $"Take Profit:   {tpS}\n"            +
                $"─────────────────────────────\n"  +
                $"Risk (USD):    {RiskAmountUSD:F0}\n" +
                $"Contract Size: {ContractSize:F0}\n"  +
                $"Lot Size:      {lots}\n"           +
                $"─────────────────────────────\n"  +
                $"Timezone:      {tz}\n"             +
                $"Day Status:    {(_tradeDone ? "COMPLETED" : "ACTIVE")}",
                VerticalAlignment.Top, HorizontalAlignment.Left, col);
        }

        protected override void OnStop()
        {
            Chart.RemoveObject(Panel);
            ClearLines();
            Print("Bot stopped.");
        }
    }
}
