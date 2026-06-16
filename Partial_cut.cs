using System;
using System.Linq;
using cAlgo.API;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class PartialCloseManager : Robot
    {
        // UI Controls
        private StackPanel _mainPanel;
        private TextBox _positionIdInput;
        private TextBox _percentageInput;
        private TextBox _targetPriceInput;
        private Button _loadButton;
        private Button _activateButton;
        private TextBlock _statusText;
        private TextBlock _positionInfoText;

        // Runtime variables
        private Position _selectedPosition;
        private double _closePercentage = 80;
        private double _targetPrice;
        private bool _autoCloseEnabled = false;
        private bool _hasExecutedClose = false;
        private DateTime _lastExecutionTime = DateTime.MinValue;

        protected override void OnStart()
        {
            CreateControlPanel();
            Print("Partial Close Manager started - Using ModifyPosition for true partial close");
        }

        protected override void OnTick()
        {
            if (_autoCloseEnabled && !_hasExecutedClose)
            {
                // Refresh position reference every tick
                if (!RefreshPosition())
                {
                    Print("Position not found - may have been closed externally");
                    Deactivate();
                    return;
                }

                CheckAndExecutePartialClose();
            }
        }

        protected override void OnStop()
        {
            _autoCloseEnabled = false;
        }

        private void CreateControlPanel()
        {
            _mainPanel = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(10, 10, 0, 0),
                Width = 320,
                BackgroundColor = Color.FromHex("#1e293b")
            };

            var contentStack = new StackPanel { Margin = new Thickness(15, 15, 15, 15) };

            var title = new TextBlock
            {
                Text = "Partial Close Manager v3",
                FontSize = 16,
                FontWeight = FontWeight.Bold,
                ForegroundColor = Color.FromHex("#60a5fa"),
                Margin = new Thickness(0, 0, 0, 10)
            };
            contentStack.AddChild(title);

            var infoText = new TextBlock
            {
                Text = "Uses ModifyPosition for true partial close",
                ForegroundColor = Color.FromHex("#22c55e"),
                FontSize = 10,
                Margin = new Thickness(0, 0, 0, 15),
                TextWrapping = TextWrapping.Wrap
            };
            contentStack.AddChild(infoText);

            var idLabel = new TextBlock
            {
                Text = "Position ID:",
                ForegroundColor = Color.FromHex("#94a3b8"),
                Margin = new Thickness(0, 0, 0, 5)
            };
            contentStack.AddChild(idLabel);

            _positionIdInput = new TextBox
            {
                Text = "",
                Height = 35,
                BackgroundColor = Color.FromHex("#0f172a"),
                ForegroundColor = Color.White,
                BorderColor = Color.FromHex("#475569"),
                Margin = new Thickness(0, 0, 0, 5)
            };
            contentStack.AddChild(_positionIdInput);

            _loadButton = new Button
            {
                Text = "LOAD POSITION",
                Height = 35,
                BackgroundColor = Color.FromHex("#3b82f6"),
                ForegroundColor = Color.White,
                Margin = new Thickness(0, 0, 0, 15)
            };
            _loadButton.Click += LoadPosition;
            contentStack.AddChild(_loadButton);

            _positionInfoText = new TextBlock
            {
                Text = "No position loaded",
                ForegroundColor = Color.FromHex("#64748b"),
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 15),
                TextWrapping = TextWrapping.Wrap
            };
            contentStack.AddChild(_positionInfoText);

            contentStack.AddChild(CreateSeparator());

            var pctLabel = new TextBlock
            {
                Text = "Close % (e.g., 80):",
                ForegroundColor = Color.FromHex("#94a3b8"),
                Margin = new Thickness(0, 10, 0, 5)
            };
            contentStack.AddChild(pctLabel);

            _percentageInput = new TextBox
            {
                Text = "80",
                Height = 35,
                BackgroundColor = Color.FromHex("#0f172a"),
                ForegroundColor = Color.White,
                BorderColor = Color.FromHex("#475569"),
                Margin = new Thickness(0, 0, 0, 10)
            };
            contentStack.AddChild(_percentageInput);

            var priceLabel = new TextBlock
            {
                Text = "Target Price:",
                ForegroundColor = Color.FromHex("#94a3b8"),
                Margin = new Thickness(0, 0, 0, 5)
            };
            contentStack.AddChild(priceLabel);

            _targetPriceInput = new TextBox
            {
                Text = "",
                Height = 35,
                BackgroundColor = Color.FromHex("#0f172a"),
                ForegroundColor = Color.White,
                BorderColor = Color.FromHex("#475569"),
                Margin = new Thickness(0, 0, 0, 5)
            };
            contentStack.AddChild(_targetPriceInput);

            var quickButtons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 15)
            };

            var beButton = new Button
            {
                Text = "B/E",
                Width = 70,
                Height = 30,
                Margin = new Thickness(0, 0, 5, 0),
                BackgroundColor = Color.FromHex("#334155"),
                ForegroundColor = Color.FromHex("#fbbf24")
            };
            beButton.Click += SetBreakEven;
            quickButtons.AddChild(beButton);

            var currentButton = new Button
            {
                Text = "Current",
                Width = 70,
                Height = 30,
                BackgroundColor = Color.FromHex("#334155"),
                ForegroundColor = Color.White
            };
            currentButton.Click += SetCurrentPrice;
            quickButtons.AddChild(currentButton);

            contentStack.AddChild(quickButtons);
            contentStack.AddChild(CreateSeparator());

            _statusText = new TextBlock
            {
                Text = "Status: INACTIVE",
                ForegroundColor = Color.FromHex("#ef4444"),
                FontWeight = FontWeight.Bold,
                Margin = new Thickness(0, 10, 0, 10),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            contentStack.AddChild(_statusText);

            _activateButton = new Button
            {
                Text = "ACTIVATE",
                Height = 45,
                BackgroundColor = Color.FromHex("#22c55e"),
                ForegroundColor = Color.White,
                FontWeight = FontWeight.Bold,
                FontSize = 14
            };
            _activateButton.Click += ToggleActivate;
            contentStack.AddChild(_activateButton);

            var manualButton = new Button
            {
                Text = "CLOSE NOW (Manual)",
                Height = 35,
                BackgroundColor = Color.FromHex("#6366f1"),
                ForegroundColor = Color.White,
                Margin = new Thickness(0, 10, 0, 0)
            };
            manualButton.Click += ManualClose;
            contentStack.AddChild(manualButton);

            _mainPanel.AddChild(contentStack);
            Chart.AddControl(_mainPanel);
        }

        private Border CreateSeparator()
        {
            return new Border
            {
                Height = 1,
                BackgroundColor = Color.FromHex("#334155"),
                Margin = new Thickness(0, 5, 0, 5)
            };
        }

        private void LoadPosition(ButtonClickEventArgs e)
        {
            int positionId;
            if (!int.TryParse(_positionIdInput.Text, out positionId))
            {
                MessageBox.Show("Please enter a valid Position ID number");
                return;
            }

            _selectedPosition = null;
            foreach (var pos in Positions)
            {
                if (pos.Id == positionId)
                {
                    _selectedPosition = pos;
                    break;
                }
            }

            if (_selectedPosition == null)
            {
                MessageBox.Show("Position ID " + positionId + " not found!\n\nCheck the Positions tab for the correct ID.");
                _positionInfoText.Text = "Position not found";
                _positionInfoText.ForegroundColor = Color.FromHex("#ef4444");
                return;
            }

            _positionInfoText.Text = string.Format(
                "Symbol: {0}\nType: {1}\nVolume: {2:F2}\nEntry: {3:F5}\nID: {4}",
                _selectedPosition.SymbolName,
                _selectedPosition.TradeType,
                _selectedPosition.VolumeInUnits,
                _selectedPosition.EntryPrice,
                _selectedPosition.Id
            );
            _positionInfoText.ForegroundColor = Color.FromHex("#22c55e");

            Print(string.Format("Loaded position {0} | Volume: {1:F2} | Symbol: {2}", 
                _selectedPosition.Id, _selectedPosition.VolumeInUnits, _selectedPosition.SymbolName));
        }

        private bool RefreshPosition()
        {
            if (_selectedPosition == null) return false;

            // Try to find by ID
            foreach (var pos in Positions)
            {
                if (pos.Id == _selectedPosition.Id)
                {
                    _selectedPosition = pos;
                    return true;
                }
            }

            return false;
        }

        private void SetBreakEven(ButtonClickEventArgs e)
        {
            if (_selectedPosition == null)
            {
                MessageBox.Show("Load a position first");
                return;
            }
            _targetPriceInput.Text = _selectedPosition.EntryPrice.ToString("F" + _selectedPosition.Symbol.Digits);
        }

        private void SetCurrentPrice(ButtonClickEventArgs e)
        {
            if (_selectedPosition == null)
            {
                MessageBox.Show("Load a position first");
                return;
            }
            double currentPrice = _selectedPosition.TradeType == TradeType.Buy 
                ? _selectedPosition.Symbol.Bid 
                : _selectedPosition.Symbol.Ask;
            _targetPriceInput.Text = currentPrice.ToString("F" + _selectedPosition.Symbol.Digits);
        }

        private void ToggleActivate(ButtonClickEventArgs e)
        {
            if (_selectedPosition == null)
            {
                MessageBox.Show("Please load a position first");
                return;
            }

            if (!double.TryParse(_percentageInput.Text, out _closePercentage) || _closePercentage <= 0 || _closePercentage > 100)
            {
                MessageBox.Show("Please enter valid close percentage (1-100)");
                return;
            }

            if (!double.TryParse(_targetPriceInput.Text, out _targetPrice) || _targetPrice <= 0)
            {
                MessageBox.Show("Please enter valid target price");
                return;
            }

            _autoCloseEnabled = !_autoCloseEnabled;

            if (_autoCloseEnabled)
            {
                _hasExecutedClose = false;
                _activateButton.Text = "DEACTIVATE";
                _activateButton.BackgroundColor = Color.FromHex("#ef4444");
                _statusText.Text = "ACTIVE - Monitoring...";
                _statusText.ForegroundColor = Color.FromHex("#22c55e");

                Print(string.Format(
                    "ACTIVATED: Position {0}, Close {1}% at {2}",
                    _selectedPosition.Id,
                    _closePercentage,
                    _targetPrice
                ));
            }
            else
            {
                Deactivate();
            }
        }

        private void ManualClose(ButtonClickEventArgs e)
        {
            if (_selectedPosition == null)
            {
                MessageBox.Show("No position loaded");
                return;
            }

            if (!double.TryParse(_percentageInput.Text, out _closePercentage))
            {
                MessageBox.Show("Please enter valid close percentage");
                return;
            }

            RefreshPosition();
            ExecutePartialClose();
        }

        private void CheckAndExecutePartialClose()
        {
            // Prevent multiple executions within 5 seconds
            if ((DateTime.Now - _lastExecutionTime).TotalSeconds < 5) return;

            double currentPrice = _selectedPosition.TradeType == TradeType.Buy 
                ? _selectedPosition.Symbol.Bid 
                : _selectedPosition.Symbol.Ask;

            bool conditionMet = false;

            if (_selectedPosition.TradeType == TradeType.Buy)
            {
                conditionMet = (_targetPrice >= _selectedPosition.EntryPrice && currentPrice >= _targetPrice) ||
                               (_targetPrice <= _selectedPosition.EntryPrice && currentPrice <= _targetPrice);
            }
            else
            {
                conditionMet = (_targetPrice <= _selectedPosition.EntryPrice && currentPrice <= _targetPrice) ||
                               (_targetPrice >= _selectedPosition.EntryPrice && currentPrice >= _targetPrice);
            }

            if (conditionMet)
            {
                Print("Target reached! Current: " + currentPrice + " | Target: " + _targetPrice);
                ExecutePartialClose();
            }
        }

        private void ExecutePartialClose()
        {
            try
            {
                double currentVolume = _selectedPosition.VolumeInUnits;
                
                // Calculate remaining volume after closing X%
                double remainingPercentage = (100 - _closePercentage) / 100.0;
                double newVolume = currentVolume * remainingPercentage;
                
                // Round to symbol's volume step
                double volumeStep = _selectedPosition.Symbol.VolumeInUnitsStep;
                newVolume = Math.Floor(newVolume / volumeStep) * volumeStep;
                
                // Ensure minimum volume
                double minVolume = _selectedPosition.Symbol.VolumeInUnitsMin;
                if (newVolume < minVolume)
                {
                    MessageBox.Show("Remaining volume would be below minimum. Closing entire position.");
                    ClosePosition(_selectedPosition);
                    Deactivate();
                    return;
                }

                double volumeToClose = currentVolume - newVolume;

                Print(string.Format("EXECUTING: Current={0:F2}, New={1:F2} (keeping {2}%), Closing={3:F2}", 
                    currentVolume, newVolume, 100 - _closePercentage, volumeToClose));

                // Use ModifyPosition to resize - this keeps the same position object!
                var result = ModifyPosition(_selectedPosition, newVolume);

                if (result.IsSuccessful)
                {
                    _hasExecutedClose = true;
                    _lastExecutionTime = DateTime.Now;
                    
                    // Refresh to get updated position
                    RefreshPosition();
                    
                    string message = string.Format(
                        "Partial Close Successful!\n\nOriginal: {0:F2} units\nClosed: {1:F2} units ({2}%)\nRemaining: {3:F2} units\nPosition ID: {4} (unchanged)",
                        currentVolume,
                        volumeToClose,
                        _closePercentage,
                        newVolume,
                        _selectedPosition.Id
                    );
                    
                    Print("SUCCESS! Position modified. Same ID: " + _selectedPosition.Id);
                    MessageBox.Show(message);
                    Deactivate();
                }
                else
                {
                    Print("ModifyPosition failed: " + result.Error);
                    
                    // Fallback to ClosePosition if Modify fails
                    Print("Attempting fallback with ClosePosition...");
                    var closeResult = ClosePosition(_selectedPosition, volumeToClose);
                    
                    if (closeResult.IsSuccessful)
                    {
                        _hasExecutedClose = true;
                        _lastExecutionTime = DateTime.Now;
                        MessageBox.Show(string.Format(
                            "Partial Close Completed (via ClosePosition)\n\nClosed: {0:F2} units ({1}%)\nNote: Position ID may have changed",
                            volumeToClose,
                            _closePercentage
                        ));
                        Deactivate();
                    }
                    else
                    {
                        MessageBox.Show("Both methods failed. Error: " + closeResult.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                Print("Error: " + ex.Message);
                MessageBox.Show("Error: " + ex.Message);
            }
        }

        private void Deactivate()
        {
            _autoCloseEnabled = false;
            _hasExecutedClose = false;
            _activateButton.Text = "ACTIVATE";
            _activateButton.BackgroundColor = Color.FromHex("#22c55e");
            _statusText.Text = "Status: INACTIVE";
            _statusText.ForegroundColor = Color.FromHex("#ef4444");
            Print("DEACTIVATED");
        }
    }
}