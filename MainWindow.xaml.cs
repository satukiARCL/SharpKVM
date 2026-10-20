using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SharpKVM.Config;
using SharpKVM.Input;
using SharpKVM.Native;
using SharpKVM.Network;
using SharpKVM.Protocol;
using SharpKVM.Utils;

namespace SharpKVM;

public partial class MainWindow : Window
{
    private readonly DisplayLayoutConfig _config;
    private readonly List<MonitorInfo> _monitors;
    private bool _isDragging = false;
    private Point _dragStartOffset;
    private double _displayScale = 0.1;
    private readonly List<Rect> _localMonitorRects = new();

    private Rect _localVirtualBoundsCanvas; // Canvas bounding box for all local monitors

    private InputHook? _inputHook;
    private KvmNetworkClient? _client;
    private KvmNetworkServer? _server;
    private CancellationTokenSource? _cts;

    public MainWindow()
    {
        Logger.Info("Initializing MainWindow...");
        InitializeComponent();

        _config = DisplayLayoutConfig.Load();
        Logger.Info($"Loaded config: RemoteHost={_config.RemoteHost}, Port={_config.Port}");

        try
        {
            _monitors = MonitorInfo.GetAllMonitors();
            Logger.Info($"Detected {_monitors.Count} monitors.");
            foreach (var m in _monitors)
            {
                Logger.Info($" - Monitor '{m.Name}': Bounds=({m.Left},{m.Top},{m.Width}x{m.Height}), Primary={m.IsPrimary}");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to enumerate monitors, falling back to default.", ex);
            _monitors = new List<MonitorInfo>();
        }

        Loaded += MainWindow_Loaded;
        Closing += (s, e) =>
        {
            StopAll();
            _config.Save();
        };
        Logger.Info("MainWindow constructor completed.");
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        RenderLocalMonitors();
        ApplyConfigToUI();

        // アプリ起動時に自動的に受信待機を開始する (接続先PCでの押し忘れ防止)
        StartReceiverBtn_Click(this, new RoutedEventArgs());
    }

    private void RenderLocalMonitors()
    {
        try
        {
            Logger.Info("Rendering local monitors on canvas...");
            LocalMonitorsCanvas.Children.Clear();
            _localMonitorRects.Clear();

            if (_monitors.Count == 0) return;

            SubheaderText.Text = $"マルチディスプレイ検出: {_monitors.Count} 台のモニターがアクティブです";

            int minX = _monitors.Min(m => m.Left);
            int minY = _monitors.Min(m => m.Top);
            int maxX = _monitors.Max(m => m.Right);
            int maxY = _monitors.Max(m => m.Bottom);

            int totalW = Math.Max(1, maxX - minX);
            int totalH = Math.Max(1, maxY - minY);

            double canvasW = DisplayCanvas.ActualWidth > 0 ? DisplayCanvas.ActualWidth : 800.0;
            double canvasH = DisplayCanvas.ActualHeight > 0 ? DisplayCanvas.ActualHeight : 420.0;

            // 左右・上下に相手カード（幅 200〜260px）を置くための十分な余白を確保するため、自PCモニター群の最大幅を 280px に設定
            double maxCanvasW = 280.0;
            double maxCanvasH = 180.0;
            _displayScale = Math.Min(maxCanvasW / totalW, maxCanvasH / totalH);
            double scale = _displayScale;

            double clusterW = totalW * scale;
            double clusterH = totalH * scale;

            // 自PCモニター群をキャンバスの中央に配置
            double baseLeft = Math.Max(20.0, (canvasW - clusterW) / 2.0);
            double baseTop = Math.Max(20.0, (canvasH - clusterH) / 2.0);

            _localVirtualBoundsCanvas = new Rect(baseLeft, baseTop, clusterW, clusterH);

            int index = 1;
            foreach (var mon in _monitors)
            {
                double w = Math.Max(70, mon.Width * scale);
                double h = Math.Max(50, mon.Height * scale);
                double x = baseLeft + (mon.Left - minX) * scale;
                double y = baseTop + (mon.Top - minY) * scale;

                var border = new Border
                {
                    Width = w,
                    Height = h,
                    Background = mon.IsPrimary ? new SolidColorBrush(Color.FromRgb(30, 58, 138)) : new SolidColorBrush(Color.FromRgb(30, 41, 59)),
                    BorderBrush = mon.IsPrimary ? new SolidColorBrush(Color.FromRgb(59, 130, 246)) : new SolidColorBrush(Color.FromRgb(96, 165, 250)),
                    BorderThickness = new Thickness(2),
                    CornerRadius = new CornerRadius(6)
                };

                var stack = new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };

                string title = mon.IsPrimary ? $"🖥 ディスプレイ {index} (メイン)" : $"🖥 ディスプレイ {index}";
                stack.Children.Add(new TextBlock
                {
                    Text = title,
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center
                });

                stack.Children.Add(new TextBlock
                {
                    Text = $"{mon.Width} × {mon.Height}",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromRgb(147, 197, 253)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 2, 0, 0)
                });

                border.Child = stack;

                Canvas.SetLeft(border, x);
                Canvas.SetTop(border, y);

                LocalMonitorsCanvas.Children.Add(border);
                _localMonitorRects.Add(new Rect(x, y, w, h));
                index++;
            }

            // 相手カードの初期サイズを同一スケールで算出
            RemoteMonitor.Width = Math.Max(80.0, 1920 * _displayScale);
            RemoteMonitor.Height = Math.Max(50.0, 1080 * _displayScale);

            Logger.Info("Local monitors rendered successfully.");
        }
        catch (Exception ex)
        {
            Logger.Error("Error rendering local monitors.", ex);
        }
    }

    private void ApplyConfigToUI()
    {
        RemoteHostInput.Text = _config.RemoteHost;
        RemoteHostLabel.Text = _config.RemoteHost;
        PortInput.Text = _config.Port.ToString();

        double canvasW = DisplayCanvas.ActualWidth > 0 ? DisplayCanvas.ActualWidth : 800.0;
        double remoteW = RemoteMonitor.Width > 0 ? RemoteMonitor.Width : 160.0;
        double remoteH = RemoteMonitor.Height > 0 ? RemoteMonitor.Height : 100.0;

        // 既存設定が自PCモニターと被っているか、画面外の場合は自動で綺麗に再配置
        bool isOverlapping = false;
        var remoteRect = new Rect(_config.RemoteCanvasX, _config.RemoteCanvasY, remoteW, remoteH);
        foreach (var lRect in _localMonitorRects)
        {
            if (remoteRect.IntersectsWith(lRect))
            {
                isOverlapping = true;
                break;
            }
        }

        if (isOverlapping || _config.RemoteCanvasX < 10 || _config.RemoteCanvasX > canvasW - 50)
        {
            ApplyLayoutDirectionToCanvas(_config.CurrentEdge);
        }
        else
        {
            Canvas.SetLeft(RemoteMonitor, _config.RemoteCanvasX);
            Canvas.SetTop(RemoteMonitor, _config.RemoteCanvasY);
            UpdateRemoteVirtualBounds(_config.RemoteCanvasX, _config.RemoteCanvasY, remoteW, remoteH);
            UpdateEdgeDirectionIndicator();
        }
    }

    private void RemoteMonitor_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
        {
            _isDragging = true;
            _dragStartOffset = e.GetPosition(RemoteMonitor);
            RemoteMonitor.CaptureMouse();
        }
    }

    private void DisplayCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isDragging)
        {
            Point canvasPos = e.GetPosition(DisplayCanvas);
            double newX = canvasPos.X - _dragStartOffset.X;
            double newY = canvasPos.Y - _dragStartOffset.Y;

            newX = Math.Clamp(newX, 10, DisplayCanvas.ActualWidth - RemoteMonitor.ActualWidth - 10);
            newY = Math.Clamp(newY, 10, DisplayCanvas.ActualHeight - RemoteMonitor.ActualHeight - 10);

            // Snap against the whole local virtual desktop cluster
            double localX = _localVirtualBoundsCanvas.X;
            double localY = _localVirtualBoundsCanvas.Y;
            double localW = _localVirtualBoundsCanvas.Width;
            double localH = _localVirtualBoundsCanvas.Height;
            double remoteW = RemoteMonitor.ActualWidth;
            double remoteH = RemoteMonitor.ActualHeight;

            // 1. Snapping to adjacent edges of local monitor cards
            const double SnapDistance = 35.0;
            const double Spacing = 15.0;

            foreach (var lRect in _localMonitorRects)
            {
                // Snap Right of monitor
                if (Math.Abs(newX - (lRect.Right + Spacing)) < SnapDistance && Math.Abs(newY - lRect.Top) < SnapDistance * 1.5)
                {
                    newX = lRect.Right + Spacing;
                    newY = lRect.Top;
                }
                // Snap Left of monitor
                else if (Math.Abs(newX - (lRect.Left - remoteW - Spacing)) < SnapDistance && Math.Abs(newY - lRect.Top) < SnapDistance * 1.5)
                {
                    newX = lRect.Left - remoteW - Spacing;
                    newY = lRect.Top;
                }
                // Snap Top of monitor
                else if (Math.Abs(newY - (lRect.Top - remoteH - Spacing)) < SnapDistance && Math.Abs(newX - lRect.Left) < SnapDistance * 1.5)
                {
                    newY = lRect.Top - remoteH - Spacing;
                    newX = lRect.Left;
                }
                // Snap Bottom of monitor
                else if (Math.Abs(newY - (lRect.Bottom + Spacing)) < SnapDistance && Math.Abs(newX - lRect.Left) < SnapDistance * 1.5)
                {
                    newY = lRect.Bottom + Spacing;
                    newX = lRect.Left;
                }
            }

            // 2. Prevent overlapping with any local monitor card (Collision Resolution)
            foreach (var lRect in _localMonitorRects)
            {
                var curRect = new Rect(newX, newY, remoteW, remoteH);
                if (curRect.IntersectsWith(lRect))
                {
                    double overlapX = Math.Min(curRect.Right, lRect.Right) - Math.Max(curRect.Left, lRect.Left);
                    double overlapY = Math.Min(curRect.Bottom, lRect.Bottom) - Math.Max(curRect.Top, lRect.Top);

                    if (overlapX < overlapY)
                    {
                        if (curRect.X < lRect.X) newX = lRect.Left - remoteW - 10;
                        else newX = lRect.Right + 10;
                    }
                    else
                    {
                        if (curRect.Y < lRect.Y) newY = lRect.Top - remoteH - 10;
                        else newY = lRect.Bottom + 10;
                    }
                }
            }

            newX = Math.Clamp(newX, 10, DisplayCanvas.ActualWidth - remoteW - 10);
            newY = Math.Clamp(newY, 10, DisplayCanvas.ActualHeight - remoteH - 10);

            Canvas.SetLeft(RemoteMonitor, newX);
            Canvas.SetTop(RemoteMonitor, newY);

            _config.RemoteCanvasX = newX;
            _config.RemoteCanvasY = newY;

            // Recalculate relative to the centroid of local virtual bounds
            double centerLocalX = localX + localW / 2;
            double centerLocalY = localY + localH / 2;
            double centerRemoteX = newX + remoteW / 2;
            double centerRemoteY = newY + remoteH / 2;

            double dx = centerRemoteX - centerLocalX;
            double dy = centerRemoteY - centerLocalY;

            if (Math.Abs(dx) >= Math.Abs(dy))
            {
                _config.CurrentEdge = dx >= 0 ? EdgeDirection.Right : EdgeDirection.Left;
            }
            else
            {
                _config.CurrentEdge = dy >= 0 ? EdgeDirection.Bottom : EdgeDirection.Top;
            }

            if (_inputHook != null)
            {
                _inputHook.ActiveEdge = _config.CurrentEdge;
            }

            UpdateRemoteVirtualBounds(newX, newY, remoteW, remoteH);
            UpdateEdgeDirectionIndicator();
        }
    }

    private void DisplayCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            RemoteMonitor.ReleaseMouseCapture();
            UpdateRemoteVirtualBounds(Canvas.GetLeft(RemoteMonitor), Canvas.GetTop(RemoteMonitor), RemoteMonitor.ActualWidth, RemoteMonitor.ActualHeight);
            _config.Save();
            SendLayoutSyncPacket();
        }
    }

    private void SendLayoutSyncPacket()
    {
        if (_client != null && _client.IsConnected)
        {
            _client.QueuePacket(new KvmPacket
            {
                Type = PacketType.SyncLayoutConfig,
                Param1 = (int)_config.CurrentEdge,
                Param2 = (int)_config.RemoteVirtualMinY,
                Param3 = (int)_config.RemoteVirtualMinX
            });
            Logger.Info($"[Layout Sync] Sent master layout direction {_config.CurrentEdge} to receiver.");
        }
    }

    private void ApplyLayoutDirectionToCanvas(EdgeDirection edge)
    {
        try
        {
            Logger.Info($"[ApplyLayout] Start applying edge: {edge}");

            if (_localMonitorRects.Count == 0)
            {
                RenderLocalMonitors();
            }

            if (_localMonitorRects.Count == 0)
            {
                Logger.Warn("[ApplyLayout] No local monitors available.");
                return;
            }

            double canvasW = DisplayCanvas.ActualWidth > 0 ? DisplayCanvas.ActualWidth : 800.0;
            double canvasH = DisplayCanvas.ActualHeight > 0 ? DisplayCanvas.ActualHeight : 420.0;

            double remoteW = RemoteMonitor.Width > 0 ? RemoteMonitor.Width : 160.0;
            double remoteH = RemoteMonitor.Height > 0 ? RemoteMonitor.Height : 100.0;
            const double Spacing = 20.0;

            double localLeft = _localVirtualBoundsCanvas.Left;
            double localRight = _localVirtualBoundsCanvas.Right;
            double localTop = _localVirtualBoundsCanvas.Top;
            double localBottom = _localVirtualBoundsCanvas.Bottom;

            double newX = localRight + Spacing;
            double newY = localTop + (_localVirtualBoundsCanvas.Height - remoteH) / 2.0;

            switch (edge)
            {
                case EdgeDirection.Left:
                    newX = localLeft - remoteW - Spacing;
                    newY = localTop + (_localVirtualBoundsCanvas.Height - remoteH) / 2.0;
                    break;
                case EdgeDirection.Right:
                    newX = localRight + Spacing;
                    newY = localTop + (_localVirtualBoundsCanvas.Height - remoteH) / 2.0;
                    break;
                case EdgeDirection.Top:
                    newX = localLeft + (_localVirtualBoundsCanvas.Width - remoteW) / 2.0;
                    newY = localTop - remoteH - Spacing;
                    break;
                case EdgeDirection.Bottom:
                    newX = localLeft + (_localVirtualBoundsCanvas.Width - remoteW) / 2.0;
                    newY = localBottom + Spacing;
                    break;
            }

            // 衝突回避: 自PCモニターカードと重ならないように外側へ押し出す
            foreach (var lRect in _localMonitorRects)
            {
                var curRect = new Rect(newX, newY, remoteW, remoteH);
                if (curRect.IntersectsWith(lRect))
                {
                    if (edge == EdgeDirection.Left) newX = lRect.Left - remoteW - Spacing;
                    else if (edge == EdgeDirection.Right) newX = lRect.Right + Spacing;
                    else if (edge == EdgeDirection.Top) newY = lRect.Top - remoteH - Spacing;
                    else if (edge == EdgeDirection.Bottom) newY = lRect.Bottom + Spacing;
                }
            }

            double minX = 10.0;
            double maxX = Math.Max(minX, canvasW - remoteW - 10.0);
            double minY = 10.0;
            double maxY = Math.Max(minY, canvasH - remoteH - 10.0);

            newX = Math.Clamp(newX, minX, maxX);
            newY = Math.Clamp(newY, minY, maxY);

            Canvas.SetLeft(RemoteMonitor, newX);
            Canvas.SetTop(RemoteMonitor, newY);

            _config.RemoteCanvasX = newX;
            _config.RemoteCanvasY = newY;
            _config.CurrentEdge = edge;
            _config.Save();

            UpdateRemoteVirtualBounds(newX, newY, remoteW, remoteH);
            UpdateEdgeDirectionIndicator();
            Logger.Info($"[ApplyLayout] Successfully moved RemoteMonitor to {edge} at ({newX:F0},{newY:F0})");
        }
        catch (Exception ex)
        {
            Logger.Error("[ApplyLayout] Error placing RemoteMonitor", ex);
        }
    }

    private void UpdateRemoteVirtualBounds(double canvasX, double canvasY, double canvasW, double canvasH)
    {
        if (_monitors.Count == 0 || _displayScale <= 0) return;

        int minX = _monitors.Min(m => m.Left);
        int minY = _monitors.Min(m => m.Top);
        double baseLeft = 120.0;
        double baseTop = 80.0;

        double virtMinX = minX + (canvasX - baseLeft) / _displayScale;
        double virtMinY = minY + (canvasY - baseTop) / _displayScale;
        double virtW = canvasW / _displayScale;
        double virtH = canvasH / _displayScale;

        _config.RemoteVirtualMinX = virtMinX;
        _config.RemoteVirtualMaxX = virtMinX + virtW;
        _config.RemoteVirtualMinY = virtMinY;
        _config.RemoteVirtualMaxY = virtMinY + virtH;

        if (_inputHook != null)
        {
            _inputHook.RemoteVirtualMinX = _config.RemoteVirtualMinX;
            _inputHook.RemoteVirtualMaxX = _config.RemoteVirtualMaxX;
            _inputHook.RemoteVirtualMinY = _config.RemoteVirtualMinY;
            _inputHook.RemoteVirtualMaxY = _config.RemoteVirtualMaxY;
        }

        Logger.Info($"[Layout] Active Interval: X=[{virtMinX:F0}..{virtMinX + virtW:F0}], Y=[{virtMinY:F0}..{virtMinY + virtH:F0}]");
    }

    private void DisplayCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        DisplayCanvas.Focus();
    }

    private void UpdateEdgeDirectionIndicator()
    {
        string dirText = _config.CurrentEdge switch
        {
            EdgeDirection.Right => "配置方向: 右 (Right) ➔ 自PCの最も右端から相手へ",
            EdgeDirection.Left => "配置方向: 左 (Left) ➔ 自PCの最も左端から相手へ",
            EdgeDirection.Top => "配置方向: 上 (Top) ➔ 自PCの最も上端から相手へ",
            EdgeDirection.Bottom => "配置方向: 下 (Bottom) ➔ 自PCの最も下端から相手へ",
            _ => "配置方向: 未定"
        };

        EdgeIndicatorText.Text = dirText;
    }

    private void RemoteHostInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (RemoteHostLabel == null || _config == null) return;
        string host = RemoteHostInput.Text.Trim();
        RemoteHostLabel.Text = string.IsNullOrEmpty(host) ? "127.0.0.1" : host;
        _config.RemoteHost = host;
    }

    private void SetStatus(string text, Color dotColor, Color badgeBg, Color badgeBorder)
    {
        Dispatcher.Invoke(() =>
        {
            StatusText.Text = text;
            StatusDot.Fill = new SolidColorBrush(dotColor);
            StatusBadge.Background = new SolidColorBrush(badgeBg);
            StatusBadge.BorderBrush = new SolidColorBrush(badgeBorder);
        });
    }

    private async void StartSenderBtn_Click(object sender, RoutedEventArgs e)
    {
        StopAll();
        _cts = new CancellationTokenSource();

        string host = string.IsNullOrWhiteSpace(RemoteHostInput.Text) ? "127.0.0.1" : RemoteHostInput.Text.Trim();
        if (!int.TryParse(PortInput.Text.Trim(), out int port)) port = 24850;

        _config.RemoteHost = host;
        _config.Port = port;
        _config.Save();

        SetStatus("接続試行中...", Color.FromRgb(245, 158, 11), Color.FromRgb(39, 39, 42), Color.FromRgb(63, 63, 70));
        StartSenderBtn.IsEnabled = false;
        StartReceiverBtn.IsEnabled = false;
        StopBtn.IsEnabled = true;

        _client = new KvmNetworkClient(host, port);
        _client.OnPacketReceived = packet =>
        {
            if (packet.Type == PacketType.HandshakeDisplayInfo)
            {
                int remoteW = packet.Param1;
                int remoteH = packet.Param2;
                int monitorCount = packet.Param3;
                Logger.Info($"[Handshake Received] Remote Display: {remoteW}x{remoteH} ({monitorCount} monitors)");

                if (_inputHook != null)
                {
                    _inputHook.RemoteScreenWidth = remoteW;
                    _inputHook.RemoteScreenHeight = remoteH;
                }

                Dispatcher.Invoke(() =>
                {
                    RemoteResolutionText.Text = $"{remoteW} × {remoteH} ({monitorCount}画面)";

                    // Apply the exact same scale as local monitors
                    double newWidth = Math.Max(80.0, remoteW * _displayScale);
                    double newHeight = Math.Max(50.0, remoteH * _displayScale);
                    RemoteMonitor.Width = newWidth;
                    RemoteMonitor.Height = newHeight;

                    UpdateRemoteVirtualBounds(Canvas.GetLeft(RemoteMonitor), Canvas.GetTop(RemoteMonitor), newWidth, newHeight);
                    Logger.Info($"Updated RemoteMonitor card size to {newWidth:F0}x{newHeight:F0} (Scale: {_displayScale:F4})");
                    SendLayoutSyncPacket();
                });
            }
        };

        try
        {
            await _client.ConnectAsync(_cts.Token);
            SetStatus("接続中 (自PC操作中)", Color.FromRgb(59, 130, 246), Color.FromRgb(30, 58, 138), Color.FromRgb(59, 130, 246));
            SendLayoutSyncPacket();

            // 接続直後と少し置いたタイミングの両方で送信してレイアウト同期の確実性を高める
            _ = Task.Run(async () =>
            {
                await Task.Delay(300);
                SendLayoutSyncPacket();
                await Task.Delay(700);
                SendLayoutSyncPacket();
            });

            _inputHook = new InputHook
            {
                ActiveEdge = _config.CurrentEdge,
                RemoteVirtualMinX = _config.RemoteVirtualMinX,
                RemoteVirtualMaxX = _config.RemoteVirtualMaxX,
                RemoteVirtualMinY = _config.RemoteVirtualMinY,
                RemoteVirtualMaxY = _config.RemoteVirtualMaxY,
                OnPacketGenerated = packet => _client.QueuePacket(packet),
                OnRemoteControlStateChanged = isRemote =>
                {
                    if (isRemote)
                    {
                        SetStatus("相手 PC 操作中 (ScrollLockで復帰)", Color.FromRgb(16, 185, 129), Color.FromRgb(6, 78, 59), Color.FromRgb(16, 185, 129));
                    }
                    else
                    {
                        SetStatus("接続中 (自PC操作中)", Color.FromRgb(59, 130, 246), Color.FromRgb(30, 58, 138), Color.FromRgb(59, 130, 246));
                    }
                }
            };
            _inputHook.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"接続エラー: {ex.Message}", "SharpKVM 接続失敗", MessageBoxButton.OK, MessageBoxImage.Warning);
            StopAll();
        }
    }

    private void StartReceiverBtn_Click(object sender, RoutedEventArgs e)
    {
        StopAll();
        _cts = new CancellationTokenSource();

        if (!int.TryParse(PortInput.Text.Trim(), out int port)) port = 24850;
        _config.Port = port;
        _config.Save();

        StartSenderBtn.IsEnabled = false;
        StartReceiverBtn.IsEnabled = false;
        StopBtn.IsEnabled = true;

        _server = new KvmNetworkServer(port);
        _server.OnError = errMsg =>
        {
            Dispatcher.Invoke(() =>
            {
                SetStatus($"エラー: {errMsg}", Color.FromRgb(239, 68, 68), Color.FromRgb(127, 29, 29), Color.FromRgb(239, 68, 68));
                StartSenderBtn.IsEnabled = true;
                StartReceiverBtn.IsEnabled = true;
                StopBtn.IsEnabled = false;
            });
        };
        _server.OnClientConnected = clientEp =>
        {
            Dispatcher.Invoke(() =>
            {
                SetStatus($"接続中 (接続元: {clientEp})", Color.FromRgb(59, 130, 246), Color.FromRgb(30, 58, 138), Color.FromRgb(59, 130, 246));
                Logger.Info($"[Receiver] Client connected: {clientEp}");
            });
        };
        _server.OnClientDisconnected = () =>
        {
            Dispatcher.Invoke(() =>
            {
                SetStatus($"受信待機中 (ポート {port})", Color.FromRgb(16, 185, 129), Color.FromRgb(6, 78, 59), Color.FromRgb(16, 185, 129));
                Logger.Info($"[Receiver] Client disconnected. Returned to listening on port {port}.");
            });
        };

        _server.OnPacketReceived = packet =>
        {
            switch (packet.Type)
            {
                case PacketType.MouseMove:
                    // Relative delta movement: Add dx and dy to current cursor position
                    if (Win32.GetCursorPos(out var pt))
                    {
                        InputSimulator.MoveMouse(pt.x + packet.Param1, pt.y + packet.Param2);
                    }
                    break;
                case PacketType.MouseDown:
                    InputSimulator.MouseButtonEvent((SharpKVM.Protocol.MouseButton)packet.Param1, true);
                    break;
                case PacketType.MouseUp:
                    InputSimulator.MouseButtonEvent((SharpKVM.Protocol.MouseButton)packet.Param1, false);
                    break;
                case PacketType.MouseWheel:
                    InputSimulator.MouseWheel(packet.Param1);
                    break;
                case PacketType.KeyDown:
                    InputSimulator.KeyEvent((ushort)packet.Param1, (ushort)packet.Param2, true, (uint)packet.Param3);
                    break;
                case PacketType.KeyUp:
                    InputSimulator.KeyEvent((ushort)packet.Param1, (ushort)packet.Param2, false, (uint)packet.Param3);
                    break;
                case PacketType.SwitchScreen:
                    InputSimulator.MoveMouse(packet.Param1, packet.Param2);
                    break;
                case PacketType.SyncLayoutConfig:
                    var senderEdge = (EdgeDirection)packet.Param1;
                    var inverseEdge = senderEdge switch
                    {
                        EdgeDirection.Right => EdgeDirection.Left,
                        EdgeDirection.Left => EdgeDirection.Right,
                        EdgeDirection.Top => EdgeDirection.Bottom,
                        EdgeDirection.Bottom => EdgeDirection.Top,
                        _ => EdgeDirection.Left
                    };
                    Logger.Info($"[Layout Sync] Received master layout from sender: {senderEdge} -> applying inverse {inverseEdge}");

                    _config.CurrentEdge = inverseEdge;
                    if (_inputHook != null)
                    {
                        _inputHook.ActiveEdge = inverseEdge;
                    }

                    Dispatcher.Invoke(() =>
                    {
                        ApplyLayoutDirectionToCanvas(inverseEdge);
                        UpdateEdgeDirectionIndicator();
                    });
                    break;
            }
        };

        _ = _server.StartAsync(_cts.Token);
        SetStatus($"受信待機中 (ポート {port})", Color.FromRgb(16, 185, 129), Color.FromRgb(6, 78, 59), Color.FromRgb(16, 185, 129));
    }

    private void StopBtn_Click(object sender, RoutedEventArgs e)
    {
        StopAll();
    }

    private void StopAll()
    {
        _cts?.Cancel();
        _inputHook?.Dispose();
        _inputHook = null;

        _client?.Disconnect();
        _client = null;

        _server?.Stop();
        _server = null;

        StartSenderBtn.IsEnabled = true;
        StartReceiverBtn.IsEnabled = true;
        StopBtn.IsEnabled = false;

        RemoteResolutionText.Text = "未同期 (接続時に検出)";
        SetStatus("停止中", Color.FromRgb(113, 113, 122), Color.FromRgb(39, 39, 42), Color.FromRgb(63, 63, 70));
    }
}
