//+------------------------------------------------------------------+
//|  Tokyo Bridge EA v3.8                                             |
//|  Undetectable: Magic=0, Comment="", Random delay                  |
//|  Auto-detects broker filling mode                                 |
//+------------------------------------------------------------------+
#property copyright ""
#property version   "3.80"
#property strict

input string   Symbol_Silver     = "XAGUSD.x";
input string   Symbol_Gold       = "XAUUSD.x";
input double   Risk_Silver       = 500.0;
input double   Risk_Gold         = 500.0;
input double   Contract_Silver   = 5000.0;
input double   Contract_Gold     = 100.0;
input double   MinLot            = 0.01;
input double   LotStep           = 0.01;

string lastTradeID_Silver = "";
string lastTradeID_Gold   = "";
string status_Silver      = "Waiting...";
string status_Gold        = "Waiting...";
string lastSignal_Silver  = "none";
string lastSignal_Gold    = "none";

int OnInit()
  {
   MathSrand((int)TimeLocal());
   EventSetTimer(1);
   Print("Tokyo Bridge EA v3.8 started.");
   Print("Files folder: ", TerminalInfoString(TERMINAL_DATA_PATH), "\\MQL5\\Files\\");
   DrawPanel();
   return(INIT_SUCCEEDED);
  }

void OnDeinit(const int reason)
  {
   EventKillTimer();
   Comment("");
  }

void OnTimer()
  {
   ProcessSymbol("silver", Symbol_Silver, Risk_Silver, Contract_Silver);
   ProcessSymbol("gold",   Symbol_Gold,   Risk_Gold,   Contract_Gold);
   DrawPanel();
  }

//+------------------------------------------------------------------+
//| Auto-detect broker filling mode                                   |
//+------------------------------------------------------------------+
ENUM_ORDER_TYPE_FILLING GetFillingMode(string symbol)
  {
   long filling = SymbolInfoInteger(symbol, SYMBOL_FILLING_MODE);
   if((filling & SYMBOL_FILLING_IOC) != 0) return ORDER_FILLING_IOC;
   if((filling & SYMBOL_FILLING_FOK) != 0) return ORDER_FILLING_FOK;
   return ORDER_FILLING_RETURN;
  }

void ProcessSymbol(string prefix, string symbol, double riskUSD, double contractSize)
  {
   string filename = prefix + "_bridge.json";
   int handle = FileOpen(filename, FILE_READ|FILE_TXT|FILE_ANSI);
   if(handle == INVALID_HANDLE) return;

   string content = "";
   while(!FileIsEnding(handle))
      content += FileReadString(handle);
   FileClose(handle);

   if(StringLen(content) < 10) return;

   string trade_id  = ParseJSON(content, "trade_id");
   string direction = ParseJSON(content, "direction");
   string entry_str = ParseJSON(content, "entry");
   string sl_str    = ParseJSON(content, "sl");
   string tp_str    = ParseJSON(content, "tp");

   if(trade_id == "") return;

   string lastID = (prefix == "silver") ? lastTradeID_Silver : lastTradeID_Gold;
   if(trade_id == lastID) return;

   if(prefix == "silver") lastTradeID_Silver = trade_id;
   else                   lastTradeID_Gold   = trade_id;

   // Debug direction chars
   string charCodes = "";
   for(int c=0; c<StringLen(direction); c++)
      charCodes += IntegerToString(StringGetCharacter(direction,c)) + " ";
   Print("[", prefix, "] Direction chars: ", charCodes);

   // Detect BUY/SELL by first character
   bool isBuy = false;
   for(int c=0; c<StringLen(direction); c++)
     {
      ushort ch = StringGetCharacter(direction, c);
      if(ch == 'B' || ch == 'b') { isBuy = true;  break; }
      if(ch == 'S' || ch == 's') { isBuy = false; break; }
     }

   Print("[", prefix, "] isBuy=", isBuy, " → ", (isBuy ? "BUY" : "SELL"));

   if(prefix == "silver") { status_Silver = "DETECTED..."; lastSignal_Silver = (isBuy?"BUY":"SELL") + " " + trade_id; }
   else                   { status_Gold   = "DETECTED..."; lastSignal_Gold   = (isBuy?"BUY":"SELL") + " " + trade_id; }
   DrawPanel();

   double sl = StringToDouble(sl_str);
   double tp = StringToDouble(tp_str);
   double en = StringToDouble(entry_str);

   if(sl <= 0 || tp <= 0)
     {
      Print("[", prefix, "] Invalid SL/TP, skip.");
      FileDelete(filename);
      return;
     }

   // Stealth delay 1-3 seconds
   Sleep(MathRand() % 2000 + 1000);

   double slDist = MathAbs(en - sl);
   double lots   = CalcLots(slDist, riskUSD, contractSize);

   bool success = PlaceTrade(isBuy, symbol, lots, sl, tp, prefix);

   if(success)
     {
      if(prefix == "silver") status_Silver = "EXECUTED OK";
      else                   status_Gold   = "EXECUTED OK";
     }
   else
     {
      if(prefix == "silver") status_Silver = "FAILED ✗";
      else                   status_Gold   = "FAILED ✗";
     }

   FileDelete(filename);
   DrawPanel();
  }

bool PlaceTrade(bool isBuy, string symbol, double lots, double sl, double tp, string prefix)
  {
   // ── Auto-detect filling mode for this broker ──
   ENUM_ORDER_TYPE_FILLING fillMode = GetFillingMode(symbol);
   Print("[", prefix, "] Filling mode: ", EnumToString(fillMode));

   // ── STEP 1: Market order NO SL/TP ──
   MqlTradeRequest request;
   MqlTradeResult  result;
   ZeroMemory(request);
   ZeroMemory(result);

   request.action       = TRADE_ACTION_DEAL;
   request.symbol       = symbol;
   request.volume       = lots;
   request.sl           = 0;
   request.tp           = 0;
   request.magic        = 0;
   request.comment      = "";
   request.deviation    = 20;
   request.type_filling = fillMode;  // ← auto-detected per broker

   if(isBuy)
     {
      request.type  = ORDER_TYPE_BUY;
      request.price = SymbolInfoDouble(symbol, SYMBOL_ASK);
      Print("[", prefix, "] Placing BUY @ ", request.price);
     }
   else
     {
      request.type  = ORDER_TYPE_SELL;
      request.price = SymbolInfoDouble(symbol, SYMBOL_BID);
      Print("[", prefix, "] Placing SELL @ ", request.price);
     }

   bool sent = false;
   if(OrderSend(request, result)) sent = true;

   if(!sent || result.retcode != TRADE_RETCODE_DONE)
     {
      Print("[", prefix, "] Order FAILED. Code=", result.retcode, " ", result.comment);

      // Retry with RETURN filling if first attempt failed
      if(fillMode != ORDER_FILLING_RETURN)
        {
         Print("[", prefix, "] Retrying with ORDER_FILLING_RETURN...");
         request.type_filling = ORDER_FILLING_RETURN;
         ZeroMemory(result);
         sent = false;
         if(OrderSend(request, result)) sent = true;
         if(sent && result.retcode == TRADE_RETCODE_DONE)
            Print("[", prefix, "] Retry OK with RETURN filling!");
         else
           {
            Print("[", prefix, "] Retry FAILED. Code=", result.retcode);
            return false;
           }
        }
      else
         return false;
     }

   Print("[", prefix, "] Order placed! Ticket=", result.order, " Lots=", lots);

   // ── STEP 2: Set SL/TP on new position ──
   Sleep(2000);
   datetime orderTime = TimeCurrent();

   for(int i = PositionsTotal()-1; i >= 0; i--)
     {
      ulong posTicket = PositionGetTicket(i);
      if(!PositionSelectByTicket(posTicket)) continue;
      if(PositionGetString(POSITION_SYMBOL) != symbol) continue;

      datetime posTime = (datetime)PositionGetInteger(POSITION_TIME);
      if(orderTime - posTime > 10) continue;

      long posType = PositionGetInteger(POSITION_TYPE);
      if(isBuy  && posType != POSITION_TYPE_BUY)  continue;
      if(!isBuy && posType != POSITION_TYPE_SELL) continue;

      Print("[", prefix, "] Matched position ", posTicket);

      MqlTradeRequest modReq;
      MqlTradeResult  modRes;
      ZeroMemory(modReq);
      ZeroMemory(modRes);

      modReq.action   = TRADE_ACTION_SLTP;
      modReq.symbol   = symbol;
      modReq.position = posTicket;
      modReq.sl       = sl;
      modReq.tp       = tp;
      modReq.magic    = 0;
      modReq.comment  = "";

      bool modSent = false;
      if(OrderSend(modReq, modRes)) modSent = true;

      if(modSent && modRes.retcode == TRADE_RETCODE_DONE)
         Print("[", prefix, "] SL/TP set! SL=", sl, " TP=", tp);
      else
         Print("[", prefix, "] SL/TP FAILED. Code=", modRes.retcode, " ", modRes.comment);

      break;
     }

   return true;
  }

double CalcLots(double slDist, double riskUSD, double contractSize)
  {
   if(slDist <= 0) return MinLot;
   double raw  = riskUSD / (slDist * contractSize);
   double lots = MathFloor(raw / LotStep) * LotStep;
   lots = NormalizeDouble(lots, 2);
   return MathMax(lots, MinLot);
  }

string ParseJSON(string json, string key)
  {
   string search = "\"" + key + "\"";
   int pos = StringFind(json, search);
   if(pos < 0) return "";
   pos = StringFind(json, ":", pos);
   if(pos < 0) return "";
   pos++;
   while(pos < StringLen(json) && StringGetCharacter(json, pos) == ' ') pos++;
   string value = "";
   if(StringGetCharacter(json, pos) == '"')
     {
      pos++;
      while(pos < StringLen(json))
        {
         ushort c = StringGetCharacter(json, pos);
         if(c == '"') break;
         value += ShortToString(c);
         pos++;
        }
     }
   else
     {
      while(pos < StringLen(json))
        {
         ushort c = StringGetCharacter(json, pos);
         if(c == ',' || c == '}' || c == '\n' || c == '\r') break;
         value += ShortToString(c);
         pos++;
        }
      StringTrimRight(value);
      StringTrimLeft(value);
     }
   return value;
  }

void DrawPanel()
  {
   string today = TimeToString(TimeLocal(), TIME_DATE);
   string panel = "";
   panel += "╔══════════════════════════════╗\n";
   panel += "║    TOKYO BRIDGE EA v3.8      ║\n";
   panel += "║  Date: " + today + "        ║\n";
   panel += "╠══════════════════════════════╣\n";
   panel += "║  SILVER: " + Symbol_Silver + "     ║\n";
   panel += "║  Status: " + status_Silver + "      ║\n";
   panel += "║  Last  : " + lastSignal_Silver + "  ║\n";
   panel += "╠══════════════════════════════╣\n";
   panel += "║  GOLD  : " + Symbol_Gold + "     ║\n";
   panel += "║  Status: " + status_Gold + "      ║\n";
   panel += "║  Last  : " + lastSignal_Gold + "  ║\n";
   panel += "╚══════════════════════════════╝\n";
   Comment(panel);
  }

void OnTick() {}
//+------------------------------------------------------------------+
