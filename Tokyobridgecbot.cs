using System;
using System.IO;
using cAlgo.API;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    /// <summary>
    /// Tokyo Bridge cBot v2
    /// Monitors multiple symbols and writes to multiple account folders.
    /// Drag onto ANY chart — monitors all configured symbols.
    /// Zero interference with main trading bots.
    /// </summary>
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.FullAccess)]
    public class TokyoBridgeBot : Robot
    {
        // ── Symbol Settings ───────────────────────────────────────────────────
        [Parameter("═══ SYMBOLS ═══", DefaultValue = "")]
        public string _S0 { get; set; }

        [Parameter("Symbol 1 (e.g. XAGUSD.x)", DefaultValue = "XAGUSD.x")]
        public string Symbol1 { get; set; }

        [Parameter("Symbol 2 (blank = disabled)", DefaultValue = "XAUUSD.x")]
        public string Symbol2 { get; set; }

        [Parameter("Symbol 3 (blank = disabled)", DefaultValue = "")]
        public string Symbol3 { get; set; }

        // ── Account File Paths ────────────────────────────────────────────────
        [Parameter("═══ ACCOUNT FILE PATHS ═══", DefaultValue = "")]
        public string _S1 { get; set; }

        [Parameter("Account 1 Folder", DefaultValue = @"C:\Users\User\OneDrive\Desktop\FX\bridge\account1")]
        public string Account1Folder { get; set; }

        [Parameter("Account 2 Folder (blank = disabled)", DefaultValue = @"C:\Users\User\OneDrive\Desktop\FX\bridge\account2")]
        public string Account2Folder { get; set; }

        [Parameter("Account 3 Folder (blank = disabled)", DefaultValue = "")]
        public string Account3Folder { get; set; }

        // ── State ─────────────────────────────────────────────────────────────
        private string _lastSilverTradeId = "";
        private string _lastGoldTradeId   = "";
        private string _lastSym3TradeId   = "";

        private const string PanelName = "BridgePanel";

        protected override void OnStart()
        {
            // Create account folders if they don't exist
            CreateFolder(Account1Folder);
            CreateFolder(Account2Folder);
            CreateFolder(Account3Folder);

            Print($"Bridge cBot v2 started.");
            Print($"Monitoring: {Symbol1} | {Symbol2} | {Symbol3}");
            Print($"Account1: {Account1Folder}");
            Print($"Account2: {Account2Folder}");
            Print($"Account3: {Account3Folder}");

            UpdatePanel("Monitoring...");
        }

        protected override void OnTick()
        {
            // Check each configured symbol
            if (!string.IsNullOrWhiteSpace(Symbol1))
                CheckSymbol(Symbol1, ref _lastSilverTradeId, "silver");

            if (!string.IsNullOrWhiteSpace(Symbol2))
                CheckSymbol(Symbol2, ref _lastGoldTradeId, "gold");

            if (!string.IsNullOrWhiteSpace(Symbol3))
                CheckSymbol(Symbol3, ref _lastSym3TradeId, "sym3");
        }

        private void CheckSymbol(string symbolName, ref string lastTradeId, string filePrefix)
        {
            foreach (var pos in Positions)
            {
                if (!pos.SymbolName.Trim().Equals(symbolName.Trim(),
                    StringComparison.OrdinalIgnoreCase))
                    continue;

                string tradeId = pos.Id.ToString();
                if (tradeId == lastTradeId) return; // already processed

                lastTradeId = tradeId;
                WriteToAllAccounts(pos, filePrefix);
                UpdatePanel($"{filePrefix.ToUpper()} trade written");
                return;
            }
        }

        private void WriteToAllAccounts(Position pos, string filePrefix)
        {
            string json = BuildJson(pos);

            WriteFile(Account1Folder, filePrefix, json);
            WriteFile(Account2Folder, filePrefix, json);
            WriteFile(Account3Folder, filePrefix, json);

            Print($"Written {filePrefix} trade to all account folders.");
        }

        private void WriteFile(string folder, string prefix, string json)
        {
            if (string.IsNullOrWhiteSpace(folder)) return;

            try
            {
                string filePath = Path.Combine(folder, $"{prefix}_bridge.json");
                System.IO.File.WriteAllText(filePath, json);
                Print($"Written: {filePath}");
            }
            catch (Exception ex)
            {
                Print($"Write error [{folder}]: {ex.Message}");
            }
        }

        private string BuildJson(Position pos)
        {
            string direction = pos.TradeType == TradeType.Buy ? "BUY" : "SELL";
            double entry     = pos.EntryPrice;
            double sl        = pos.StopLoss ?? 0;
            double tp        = pos.TakeProfit ?? 0;
            string timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
            string tradeId   = pos.Id.ToString();

            return $@"{{
  ""trade_id"":  ""{tradeId}"",
  ""direction"": ""{direction}"",
  ""entry"":     {entry:F3},
  ""sl"":        {sl:F3},
  ""tp"":        {tp:F3},
  ""symbol"":    ""{pos.SymbolName}"",
  ""timestamp"": ""{timestamp}""
}}";
        }

        private void CreateFolder(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder)) return;
            try { Directory.CreateDirectory(folder); }
            catch (Exception ex) { Print($"Folder error [{folder}]: {ex.Message}"); }
        }

        private void UpdatePanel(string status)
        {
            Chart.RemoveObject(PanelName);
            string sym1 = string.IsNullOrWhiteSpace(Symbol1) ? "---" : Symbol1;
            string sym2 = string.IsNullOrWhiteSpace(Symbol2) ? "---" : Symbol2;
            string sym3 = string.IsNullOrWhiteSpace(Symbol3) ? "---" : Symbol3;

            Chart.DrawStaticText(PanelName,
                $"─── TOKYO BRIDGE v2 ──────────\n" +
                $"Status:  {status}\n"               +
                $"Symbol1: {sym1}\n"                 +
                $"Symbol2: {sym2}\n"                 +
                $"Symbol3: {sym3}\n"                 +
                $"Acc1: {ShortPath(Account1Folder)}\n" +
                $"Acc2: {ShortPath(Account2Folder)}\n" +
                $"Acc3: {ShortPath(Account3Folder)}",
                VerticalAlignment.Bottom,
                HorizontalAlignment.Left,
                Color.LightBlue);
        }

        private string ShortPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "disabled";
            return path.Length > 30 ? "..." + path.Substring(path.Length - 27) : path;
        }

        protected override void OnStop()
        {
            Chart.RemoveObject(PanelName);
            Print("Bridge cBot stopped.");
        }
    }
}