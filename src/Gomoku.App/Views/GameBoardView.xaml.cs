using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Gomoku.App;
using Gomoku.Core.Domain.Models;
using Gomoku.Core.Game;

namespace Gomoku.App.Views;

/// <summary>棋盘对局视图：网格绘制、点击落子、回合指示、悔棋与重开。</summary>
public partial class GameBoardView : UserControl
{
    private const double CanvasSize = 570;
    private const double CellSize = CanvasSize / (Board.Size - 1);
    private const double StoneRadius = 14;

    private readonly GameController _controller;
    private string _boardStyle;
    // 棋盘材质：木质 wood / 石制 stone，与风格独立，视频模式下棋盘仍显示该材质
    private string _boardMaterial;
    private readonly List<Ellipse> _winRings = new();
    private MainWindow? _window;
    private readonly List<Grid> _winCells = new();
    private bool _delayEnd;
    private System.Windows.Threading.DispatcherTimer? _endTimer;
    private const double TurnSeconds = 30;
    private double _remaining;
    private System.Windows.Threading.DispatcherTimer? _turnTimer;
    private Line? _winLine;
    private readonly List<Particle> _particles = new();
    private bool _particleHook;

    private readonly Dictionary<Position, Grid> _stoneCache = new();
    private string _appliedBoardStyle = string.Empty;
    private Position? _lastMovePosition;
    private Position? _lastAnimatedMove;
    private Ellipse? _lastMoveRing;
    private bool _aiBusy;
    private System.Threading.CancellationTokenSource? _aiCts;
    private bool _shifting;
    private System.Windows.Threading.DispatcherTimer? _shiftTimer;
    private DateTime _shiftStart;
    private double _shiftFromRow, _shiftFromCol, _shiftToRow, _shiftToCol;

    // 动态视频背景
    private readonly string[] _videoPaths = new[]
    {
        System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Videos", "video1.mp4"),
        System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Videos", "video2.mp4"),
        System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Videos", "video3.mp4"),
        System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Videos", "video4.mp4")
    };
    private int _currentVideoIndex = -1;
    private MediaElement _frontVideo = null!;
    private MediaElement _backVideo = null!;
    private bool _isVideoStyle;
    private bool _videoCrossing;
    private System.Windows.Threading.DispatcherTimer? _videoCheckTimer;
    // 交叉淡化时长：旧视频淡出与新视频淡入重叠进行的时长
    private const double VideoCrossFadeMs = 2000;
    // 提前量：视频剩余时间小于该值时开始预载下一段并启动交叉淡化
    private const double VideoPreloadLeadMs = 2500;

    private sealed class Particle
    {
        public Ellipse Shape = null!;
        public double X, Y, Vx, Vy, Life, MaxLife;
    }

    public GameBoardView(GameController controller, string boardMaterial = "wood", bool videoBackground = false)
    {
        InitializeComponent();
        _controller = controller;
        _boardStyle = videoBackground ? "video" : boardMaterial;
        _boardMaterial = boardMaterial;
        Loaded += (_, _) =>
        {
            SubscribeFullScreen();
            ApplyPlayerBox();
            InitializeVideoBackground(); // 动态视频背景（仅在 videoBackground 时播放）
            KickOffIfAiTurn(); // 玩家执白时 AI 执黑先行
        };
        Unloaded += (_, _) =>
        {
            UnsubscribeFullScreen();
            StopVideoBackground();
        };
        Refresh();
    }

    /// <summary>进入对局后若轮到 AI（如玩家执白、AI 执黑先手），立即触发 AI 落子。</summary>
    private void KickOffIfAiTurn()
    {
        if (_controller.Mode == GameMode.VsAi && _controller.CurrentPlayer != _controller.HumanColor)
        {
            TryAiTurn();
        }
    }

    private void SubscribeFullScreen()
    {
        if (Window.GetWindow(this) is MainWindow w)
        {
            _window = w;
            w.FullScreenChanged += OnFullScreenChanged;
            OnFullScreenChanged(w, EventArgs.Empty);
        }
    }

    private void UnsubscribeFullScreen()
    {
        if (_window is not null)
        {
            _window.FullScreenChanged -= OnFullScreenChanged;
            _window = null;
        }
    }

    private void OnFullScreenChanged(object? sender, EventArgs e)
    {
        bool fs = _window?.IsFullScreen ?? false;
        FullScreenButton.Content = fs ? "还原" : "全屏";
        BlackBoxImg.Visibility = fs ? Visibility.Visible : Visibility.Collapsed;

        // 人机模式：右下角始终显示玩家所选颜色的棋盒（全屏时替换掉原有的白色装饰罐，避免颜色误导）
        bool showPlayerBox = _controller.Mode == GameMode.VsAi;
        PlayerBoxPanel.Visibility = showPlayerBox ? Visibility.Visible : Visibility.Collapsed;
        WhiteBoxImg.Visibility = fs && !showPlayerBox ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>按玩家所选颜色初始化右下角棋盒：棋罐与罐口棋子颜色一致（仅人机模式；可见性由全屏状态控制）。</summary>
    private void ApplyPlayerBox()
    {
        Stone human = _controller.HumanColor == 0 ? Stone.Black : Stone.White;
        PlayerBoxImg.Source = new BitmapImage(new Uri(
            human == Stone.Black
                ? "pack://application:,,,/Assets/black_box.png"
                : "pack://application:,,,/Assets/white_box.png", UriKind.Absolute));

        Brush stoneBrush = CreateStoneBrush(human);
        var shadow = CreateStoneShadow(human);
        foreach (var ellipse in new[] { PlayerBoxStone1, PlayerBoxStone2, PlayerBoxStone3 })
        {
            ellipse.Fill = stoneBrush;
            ellipse.Effect = shadow;
            ellipse.Stroke = null;
        }
    }

    private static BitmapImage? _woodImage;
    private static BitmapImage? _stoneImage;

    /// <summary>从程序资源加载位图（pack URI），并限制解码尺寸，避免加载超大图卡顿。</summary>
    private static BitmapImage LoadBoardImage(string uri)
    {
        return new BitmapImage(new Uri(uri, UriKind.Absolute)) { DecodePixelWidth = 1024 };
    }

    /// <summary>根据所选棋盘材质设置背景：木质/石制图片（视频模式下棋盘仍保持材质，视频只作窗口背景）。</summary>
    private void ApplyBoardStyle()
    {
        _woodImage ??= LoadBoardImage("pack://application:,,,/Assets/board_wood.jpg");
        _stoneImage ??= LoadBoardImage("pack://application:,,,/Assets/board_stone.jpg");
        BoardCanvas.Background = new ImageBrush(_boardMaterial == "stone" ? _stoneImage : _woodImage) { Stretch = Stretch.Fill };
    }

    /// <summary>棋盘风格切换：木质 → 石制 → 动态视频 → 循环（材质随风格记忆，视频模式沿用上一材质）。</summary>
    /// <summary>启用动态视频背景：双播放器初始状态并开始循环（视频铺满窗口，棋盘保持材质）。</summary>
    private void InitializeVideoBackground()
    {
        if (_boardStyle != "video")
        {
            return;
        }
        if (!System.IO.File.Exists(_videoPaths[0]))
        {
            // 视频资源缺失时回退到木质棋盘，避免黑屏
            _boardStyle = "wood";
            _boardMaterial = "wood";
            ApplyBoardStyle();
            return;
        }

        _isVideoStyle = true;
        VideoBackgroundLayer.Visibility = Visibility.Visible;
        ApplyBoardStyle(); // 棋盘区域保持木质/石质材质，视频在棋盘后面
        _frontVideo = VideoPlayerA;
        _backVideo = VideoPlayerB;
        _frontVideo.Opacity = 1.0;
        _backVideo.Opacity = 0.0;
        _currentVideoIndex = -1;
        _videoCrossing = false;
        PlayNextOn(_frontVideo);
        StartVideoCheckTimer();
    }

    /// <summary>启动播放进度检查定时器，用于提前触发段间交叉淡化。</summary>
    private void StartVideoCheckTimer()
    {
        if (_videoCheckTimer is null)
        {
            _videoCheckTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
        }
        _videoCheckTimer.Tick -= VideoCheckTick; // 防止重复挂载
        _videoCheckTimer.Tick += VideoCheckTick;
        _videoCheckTimer.Start();
    }

    /// <summary>周期性检查当前视频剩余时间，接近结尾时提前启动交叉淡化。</summary>
    private void VideoCheckTick(object? sender, EventArgs e)
    {
        if (!_isVideoStyle || _videoCrossing)
        {
            return;
        }
        if (!_frontVideo.NaturalDuration.HasTimeSpan)
        {
            return;
        }
        var remain = _frontVideo.NaturalDuration.TimeSpan - _frontVideo.Position;
        if (remain <= TimeSpan.FromMilliseconds(VideoPreloadLeadMs))
        {
            BeginVideoCrossFade();
        }
    }

    /// <summary>提前预载下一段视频，与当前段在重叠期内做 2 秒交叉淡入淡出。</summary>
    private void BeginVideoCrossFade()
    {
        _videoCrossing = true;
        var newFront = _backVideo;
        var oldFront = _frontVideo;

        _currentVideoIndex = (_currentVideoIndex + 1) % _videoPaths.Length;
        newFront.Source = new Uri(_videoPaths[_currentVideoIndex], UriKind.Absolute);
        newFront.Play();

        var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(VideoCrossFadeMs))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(VideoCrossFadeMs))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        fadeIn.Completed += (_, _) =>
        {
            oldFront.Stop();
            _frontVideo = newFront;
            _backVideo = oldFront;
            _videoCrossing = false;
        };
        oldFront.BeginAnimation(OpacityProperty, fadeOut);
        newFront.BeginAnimation(OpacityProperty, fadeIn);
    }

    /// <summary>停止并隐藏动态视频背景，释放媒体资源。</summary>
    private void StopVideoBackground()
    {
        _isVideoStyle = false;
        _videoCrossing = false;
        _videoCheckTimer?.Stop();
        VideoBackgroundLayer.Visibility = Visibility.Collapsed;
        VideoPlayerA.Stop();
        VideoPlayerB.Stop();
        VideoPlayerA.Source = null;
        VideoPlayerB.Source = null;
    }

    /// <summary>在指定播放器上加载并播放下一个视频。</summary>
    private void PlayNextOn(MediaElement player)
    {
        _currentVideoIndex = (_currentVideoIndex + 1) % _videoPaths.Length;
        player.Source = new Uri(_videoPaths[_currentVideoIndex], UriKind.Absolute);
        player.Play();
    }

    /// <summary>单个视频播放结束兜底：正常情况下已由 VideoCheckTick 提前交叉淡化，此方法仅作异常兜底。</summary>
    private void VideoPlayer_MediaEnded(object sender, RoutedEventArgs e)
    {
        if (sender is not MediaElement ended || !_isVideoStyle || _videoCrossing)
        {
            return;
        }

        var newFront = ReferenceEquals(ended, _frontVideo) ? _backVideo : _frontVideo;
        var oldFront = ReferenceEquals(ended, _frontVideo) ? _frontVideo : _backVideo;

        _currentVideoIndex = (_currentVideoIndex + 1) % _videoPaths.Length;
        newFront.Source = new Uri(_videoPaths[_currentVideoIndex], UriKind.Absolute);
        newFront.Play();

        newFront.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(500)));
        oldFront.BeginAnimation(OpacityProperty,
            new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(500)));
        oldFront.Stop();

        _frontVideo = newFront;
        _backVideo = oldFront;
    }

    /// <summary>绘制网格线与九星位；仅由 SyncBoard 在需要完整重建时调用。</summary>
    private void DrawGridLines()
    {
        var gridBrush = _boardMaterial == "stone"
            ? new SolidColorBrush(Color.FromRgb(0x4A, 0x4A, 0x4A))
            : new SolidColorBrush(Color.FromRgb(0x3B, 0x24, 0x10));
        gridBrush.Freeze();

        for (int i = 0; i < Board.Size; i++)
        {
            double offset = i * CellSize;
            BoardCanvas.Children.Add(new Line
            {
                X1 = 0, Y1 = offset, X2 = CanvasSize - 1, Y2 = offset,
                Stroke = gridBrush, StrokeThickness = 1.6
            });
            BoardCanvas.Children.Add(new Line
            {
                X1 = offset, Y1 = 0, X2 = offset, Y2 = CanvasSize - 1,
                Stroke = gridBrush, StrokeThickness = 1.6
            });
        }

        int[] stars = { 3, 9, 15 };
        foreach (int r in stars)
        {
            foreach (int c in stars)
            {
                var star = new Ellipse
                {
                    Width = 5,
                    Height = 5,
                    Fill = gridBrush
                };
                Canvas.SetLeft(star, c * CellSize - 2.5);
                Canvas.SetTop(star, r * CellSize - 2.5);
                BoardCanvas.Children.Add(star);
            }
        }
    }

    /// <summary>按控制器状态增量更新棋盘与提示，避免每步重绘整盘。</summary>
    private void Refresh()
    {
        SyncBoard();
        UpdateLastMoveMarker();
        RebuildWinRings();
        AnimateLastMove();
        UpdateStatusAndTimer();
    }

    /// <summary>同步棋盘棋子缓存：仅新增/删除变化的棋子，网格线只在换肤时重建。</summary>
    private void SyncBoard()
    {
        bool fullRebuild = _appliedBoardStyle != _boardStyle;
        if (fullRebuild)
        {
            BoardCanvas.Children.Clear();
            ClearParticles();
            _stoneCache.Clear();
            _winRings.Clear();
            _winCells.Clear();
            _winLine = null;
            _lastMoveRing = null;
            _lastMovePosition = null;
            _lastAnimatedMove = null;
            ApplyBoardStyle();
            DrawGridLines();
            _appliedBoardStyle = _boardStyle;
        }

        var currentPositions = new HashSet<Position>();
        Board board = _controller.Board;
        for (int row = board.ViewRow; row < board.ViewRow + Board.Size; row++)
        {
            for (int col = board.ViewCol; col < board.ViewCol + Board.Size; col++)
            {
                var pos = new Position(row, col);
                if (board.GetCell(pos) != Stone.Empty)
                {
                    currentPositions.Add(pos);
                }
            }
        }

        var cachedPositions = new List<Position>(_stoneCache.Keys);
        foreach (var pos in cachedPositions)
        {
            if (!currentPositions.Contains(pos))
            {
                var cell = _stoneCache[pos];
                BoardCanvas.Children.Remove(cell);
                _stoneCache.Remove(pos);
                if (_lastMovePosition == pos)
                {
                    _lastMovePosition = null;
                    _lastMoveRing = null;
                }
            }
        }

        foreach (var pos in currentPositions)
        {
            Stone stone = board.GetCell(pos);
            if (!_stoneCache.ContainsKey(pos))
            {
                // 画布坐标为窗口相对坐标：世界坐标减去窗口左上角偏移
                var cell = (Grid)DrawStone(pos.Col - board.ViewCol, pos.Row - board.ViewRow, stone);
                _stoneCache[pos] = cell;
            }
        }
    }

    /// <summary>移动红色最后一手标记到最新落子。</summary>
    private void UpdateLastMoveMarker()
    {
        Position? newLast = _controller.Board.History.Count > 0
            ? _controller.Board.History[^1].Position
            : null;

        if (newLast == _lastMovePosition)
        {
            return;
        }

        if (_lastMoveRing is not null && _lastMoveRing.Parent is Grid oldCell)
        {
            oldCell.Children.Remove(_lastMoveRing);
        }

        _lastMovePosition = newLast;
        _lastMoveRing = null;

        if (newLast is null || !_stoneCache.TryGetValue(newLast.Value, out var cell))
        {
            return;
        }

        var ring = new Ellipse
        {
            Stroke = new SolidColorBrush(Color.FromArgb(200, 0xE0, 0x4B, 0x3A)),
            StrokeThickness = 2,
            Margin = new Thickness(-3),
            IsHitTestVisible = false
        };
        // 红环放在高光之下、棋子之上
        cell.Children.Insert(cell.Children.Count - 1, ring);
        _lastMoveRing = ring;
    }

    /// <summary>根据获胜五连重建金色描边。</summary>
    private void RebuildWinRings()
    {
        foreach (var cell in _stoneCache.Values)
        {
            for (int i = cell.Children.Count - 1; i >= 0; i--)
            {
                if (cell.Children[i] is FrameworkElement fe && fe.Tag?.ToString() == "win")
                {
                    cell.Children.RemoveAt(i);
                }
            }
        }
        _winRings.Clear();
        _winCells.Clear();

        if (_controller.WinningLine is null)
        {
            return;
        }

        foreach (var pos in _controller.WinningLine)
        {
            if (!_stoneCache.TryGetValue(pos, out var cell))
            {
                continue;
            }

            var ring = new Ellipse
            {
                Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0xC0, 0x2A)),
                StrokeThickness = 3,
                Opacity = 0,
                IsHitTestVisible = false,
                Tag = "win"
            };
            cell.Children.Insert(Math.Max(0, cell.Children.Count - 1), ring);
            _winRings.Add(ring);
            _winCells.Add(cell);
        }
    }

    /// <summary>仅在新最后一手变化时播放落子动画。</summary>
    private void AnimateLastMove()
    {
        Position? newLast = _controller.Board.History.Count > 0
            ? _controller.Board.History[^1].Position
            : null;

        if (newLast == _lastAnimatedMove)
        {
            return;
        }

        _lastAnimatedMove = newLast;
        if (newLast is not null && _stoneCache.TryGetValue(newLast.Value, out var cell))
        {
            AnimateStoneAppear(cell);
        }
    }

    /// <summary>刷新回合/状态文字、结算遮罩与倒计时。</summary>
    private void UpdateStatusAndTimer()
    {
        if (_controller.State == GameSessionState.Finished)
        {
            TurnText.Text = ModeName + " · 对局结束";
            if (_controller.IsResigned)
            {
                string resigner = _controller.ResignedBy == 0 ? "黑方" : "白方";
                string winner = _controller.Winner == Stone.Black ? "黑方" : "白方";
                StatusText.Text = $"{resigner}认输，{winner}获胜！";
            }
            else
            {
                StatusText.Text = _controller.Winner switch
                {
                    Stone.Black => "黑方获胜！",
                    Stone.White => "白方获胜！",
                    _ => "平局！"
                };
            }

            StopTurnTimer();
            EndOverlay.Visibility = Visibility.Visible;
            if (!_delayEnd)
            {
                PlayEndAnimation(_controller.Winner);
            }
        }
        else
        {
            _endTimer?.Stop();
            DimMask.BeginAnimation(OpacityProperty, null);
            EndCard.BeginAnimation(OpacityProperty, null);
            DimMask.Opacity = 0;
            EndCard.Opacity = 0;
            EndOverlay.Visibility = Visibility.Collapsed;
            _delayEnd = false;
            ClearParticles();

            if (_controller.Mode == GameMode.VsAi && _controller.CurrentPlayer != _controller.HumanColor)
            {
                TurnText.Text = "人机对战 · AI 思考中…";
                StatusText.Text = "AI 正在思考，请稍候";
                StopTurnTimer(); // AI 回合不走人类倒计时（AI 在后台思考）
            }
            else if (_controller.Mode == GameMode.Online)
            {
                TurnText.Text = "联机对战 · 当前回合：" + (_controller.CurrentPlayer == 0 ? "黑棋" : "白棋");
                StatusText.Text = "联机同步将在 M4 阶段接入，当前可临时本地试玩";
            }
            else
            {
                TurnText.Text = ModeName + " · 当前回合：" + (_controller.CurrentPlayer == 0 ? "黑棋" : "白棋");
                StatusText.Text = string.Empty;
            }

            if (_controller.State == GameSessionState.Playing)
            {
                StartTurnTimer();
            }
            else
            {
                StopTurnTimer();
            }
        }
    }

    /// <summary>播放胜利结算动画：五颗子沿斜线亮起、金色流光贯穿、连线爆闪。</summary>
    private void PlayEndAnimation(Stone winner)
    {
        string img = winner switch
        {
            Stone.Black => "pack://application:,,,/Assets/result_black.png",
            Stone.White => "pack://application:,,,/Assets/result_white.png",
            _ => "pack://application:,,,/Assets/result_draw.png"
        };
        EndCard.Source = new BitmapImage(new Uri(img, UriKind.Absolute));

        // 先停掉上一轮残留定时器和动画
        _endTimer?.Stop();
        DimMask.BeginAnimation(OpacityProperty, null);
        EndCard.BeginAnimation(OpacityProperty, null);
        DimMask.Opacity = 0;
        EndCard.Opacity = 0;
        foreach (var r in _winRings)
        {
            r.BeginAnimation(OpacityProperty, null);
            r.Opacity = 0;
        }

        // 第一步：压暗全局棋盘
        var dim = new DoubleAnimation(0, 0.55, TimeSpan.FromMilliseconds(300))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        DimMask.BeginAnimation(OpacityProperty, dim);

        bool hasWin = _winRings.Count >= 5;

        // 第二步：五颗棋子逐个亮起：金环 + 放大 + 光晕 + 粒子爆开
        for (int i = 0; i < _winRings.Count; i++)
        {
            var ring = _winRings[i];
            var delay = TimeSpan.FromMilliseconds(i * 220 + 250);

            var glow = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(350))
            {
                BeginTime = delay,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            ring.BeginAnimation(OpacityProperty, glow);

            if (i < _winCells.Count)
            {
                var cell = _winCells[i];
                cell.RenderTransform = new ScaleTransform(1, 1);
                var scale = (ScaleTransform)cell.RenderTransform;
                var growX = new DoubleAnimation(1, 1.3, TimeSpan.FromMilliseconds(350))
                {
                    BeginTime = delay,
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                var growY = new DoubleAnimation(1, 1.3, TimeSpan.FromMilliseconds(350))
                {
                    BeginTime = delay,
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, growX);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, growY);

                if (hasWin)
                {
                    // 光晕圈：金色径向渐变，放大并淡出
                    AddGlowToCell(cell, delay);
                    // 粒子爆开：延迟到该子亮起时
                    double cx = Canvas.GetLeft(cell) + StoneRadius;
                    double cy = Canvas.GetTop(cell) + StoneRadius;
                    var pt = new System.Windows.Threading.DispatcherTimer { Interval = delay };
                    pt.Tick += (_, _) => { pt.Stop(); EmitBurst(cx, cy, 14); };
                    pt.Start();
                }
            }
        }

        // 第三步：五颗亮完后：金线爆闪 + 贴图浮入
        int ringMs = _winRings.Count * 220 + 750;
        _endTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ringMs) };
        _endTimer.Tick += (_, _) =>
        {
            _endTimer.Stop();
            if (hasWin)
            {
                FlashWinLine();
            }
            var story = (Storyboard)EndOverlay.Resources["EndStory"];
            story.Begin();
        };
        _endTimer.Start();
    }

    /// <summary>金色光晕圈：随棋子亮起时出现，放大并淡出，增强立体感。</summary>
    private void AddGlowToCell(Grid cell, TimeSpan delay)
    {
        var glow = new Ellipse
        {
            Width = StoneRadius * 5,
            Height = StoneRadius * 5,
            Fill = new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(190, 0xFF, 0xD7, 0x74), 0.0),
                    new GradientStop(Color.FromArgb(0, 0xFF, 0xD7, 0x74), 1.0)
                }
            },
            IsHitTestVisible = false,
            Opacity = 0,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(0.5, 0.5)
        };
        cell.Children.Add(glow);

        var sb = new Storyboard();
        var fadeIn = new DoubleAnimation(0, 0.9, TimeSpan.FromMilliseconds(300)) { BeginTime = delay };
        var fadeOut = new DoubleAnimation(0.9, 0, TimeSpan.FromMilliseconds(650)) { BeginTime = delay + TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(fadeIn, glow);
        Storyboard.SetTargetProperty(fadeIn, new PropertyPath(OpacityProperty));
        Storyboard.SetTarget(fadeOut, glow);
        Storyboard.SetTargetProperty(fadeOut, new PropertyPath(OpacityProperty));
        var grow = new DoubleAnimation(0.5, 1.6, TimeSpan.FromMilliseconds(950)) { BeginTime = delay };
        Storyboard.SetTarget(grow, (ScaleTransform)glow.RenderTransform);
        Storyboard.SetTargetProperty(grow, new PropertyPath(ScaleTransform.ScaleXProperty));
        var growY = new DoubleAnimation(0.5, 1.6, TimeSpan.FromMilliseconds(950)) { BeginTime = delay };
        Storyboard.SetTarget(growY, (ScaleTransform)glow.RenderTransform);
        Storyboard.SetTargetProperty(growY, new PropertyPath(ScaleTransform.ScaleYProperty));
        sb.Children.Add(fadeIn);
        sb.Children.Add(fadeOut);
        sb.Children.Add(grow);
        sb.Children.Add(growY);
        sb.Begin();
    }

    /// <summary>从 (cx,cy) 发射一圈金色粒子，向四周飞散并渐隐。</summary>
    private void EmitBurst(double cx, double cy, int count)
    {
        var rnd = new Random();
        for (int i = 0; i < count; i++)
        {
            double angle = rnd.NextDouble() * Math.PI * 2;
            double speed = 1.2 + rnd.NextDouble() * 2.2;
            var el = new Ellipse
            {
                Width = 3.5 + rnd.NextDouble() * 3,
                Height = 3.5 + rnd.NextDouble() * 3,
                Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x74)),
                IsHitTestVisible = false
            };
            Canvas.SetLeft(el, cx);
            Canvas.SetTop(el, cy);
            BoardCanvas.Children.Add(el);
            _particles.Add(new Particle
            {
                Shape = el,
                X = cx,
                Y = cy,
                Vx = Math.Cos(angle) * speed,
                Vy = Math.Sin(angle) * speed,
                Life = 26 + rnd.NextDouble() * 14,
                MaxLife = 40
            });
        }

        if (!_particleHook)
        {
            _particleHook = true;
            CompositionTarget.Rendering += OnParticleRendering;
        }
    }

    private void OnParticleRendering(object? sender, EventArgs e)
    {
        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            p.X += p.Vx;
            p.Y += p.Vy;
            p.Vy += 0.03;
            p.Life -= 1;
            Canvas.SetLeft(p.Shape, p.X);
            Canvas.SetTop(p.Shape, p.Y);
            p.Shape.Opacity = Math.Max(0, p.Life / p.MaxLife);
            if (p.Life <= 0)
            {
                BoardCanvas.Children.Remove(p.Shape);
                _particles.RemoveAt(i);
            }
        }

        if (_particles.Count == 0)
        {
            CompositionTarget.Rendering -= OnParticleRendering;
            _particleHook = false;
        }
    }

    private void ClearParticles()
    {
        foreach (var p in _particles)
        {
            BoardCanvas.Children.Remove(p.Shape);
        }

        _particles.Clear();
        if (_particleHook)
        {
            CompositionTarget.Rendering -= OnParticleRendering;
            _particleHook = false;
        }
    }

    /// <summary>五连金线爆闪：沿真实五连方向拉一条金色流光线。</summary>
    private void FlashWinLine()
    {
        if (_controller.WinningLine is null || _controller.WinningLine.Count < 2)
        {
            return;
        }

        var line = new Line
        {
            Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x74)),
            StrokeThickness = 1,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Opacity = 0,
            IsHitTestVisible = false
        };
        line.Effect = new DropShadowEffect
        {
            BlurRadius = 12,
            Color = Color.FromRgb(0xFF, 0xD7, 0x74),
            ShadowDepth = 0,
            Opacity = 0.9
        };
        var first = _controller.WinningLine[0];
        var last = _controller.WinningLine[^1];
        line.X1 = first.Col * CellSize;
        line.Y1 = first.Row * CellSize;
        line.X2 = last.Col * CellSize;
        line.Y2 = last.Row * CellSize;
        BoardCanvas.Children.Add(line);
        _winLine = line;

        line.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 0.9, TimeSpan.FromMilliseconds(220)));
        line.BeginAnimation(Line.StrokeThicknessProperty,
            new DoubleAnimation(1, 4.5, TimeSpan.FromMilliseconds(220))
            {
                AutoReverse = true
            });
    }

        /// <summary>当前模式的显示名。</summary>
    private string ModeName => _controller.Mode switch
    {
        GameMode.LocalTwoPlayer => "本地双人",
        GameMode.VsAi => string.IsNullOrEmpty(_controller.DifficultyLabel)
            ? "人机对战"
            : $"人机对战 · {_controller.DifficultyLabel}",
        GameMode.Online => "联机对战",
        _ => "对局"
    };

    /// <summary>
    /// 绘制立体棋子：球面渐变 + 内部高光 + 投影，整体作为一个棋子单元
    /// （高光在棋子内部，不会在棋盘上形成独立的小白点）。
    /// </summary>
    /// <summary>开始当前回合的 30 秒倒计时；仅对局进行中生效。</summary>
    private void StartTurnTimer()
    {
        _turnTimer?.Stop();
        _remaining = TurnSeconds;
        UpdateTimerText();
        _turnTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _turnTimer.Tick += (_, _) =>
        {
            _remaining -= 1;
            UpdateTimerText();
            if (_remaining <= 0)
            {
                _turnTimer.Stop();
                OnTimeUp();
            }
        };
        _turnTimer.Start();
    }

    /// <summary>停止倒计时并清空显示。</summary>
    private void StopTurnTimer()
    {
        _turnTimer?.Stop();
        _turnTimer = null;
        TimerText.Text = "";
    }

    /// <summary>刷新倒计时文字；剩余 5 秒内变红提醒。</summary>
    private void UpdateTimerText()
    {
        string side = _controller.CurrentPlayer == 0 ? "黑方" : "白方";
        TimerText.Text = $"{side} {Math.Ceiling(_remaining)}s";
        TimerText.Foreground = _remaining <= 5
            ? new SolidColorBrush(Colors.Red)
            : new SolidColorBrush(Color.FromRgb(0x4A, 0x37, 0x28));
    }

    /// <summary>当前回合方思考超时：自动为其落一子，对局继续。</summary>
    private void OnTimeUp()
    {
        if (_controller.AutoPlaceForTimeout())
        {
            Refresh();
            if (_controller.State == GameSessionState.Playing)
            {
                if (_controller.IsViewFull)
                {
                    ShowBoardFullPanel();
                    return;
                }

                StatusText.Text = "超时，已自动落子";
            }

            TryAiTurn();
        }
        else
        {
            Refresh();
        }
    }

    private FrameworkElement DrawStone(int col, int row, Stone stone)
    {
        var cell = new Grid
        {
            Width = StoneRadius * 2,
            Height = StoneRadius * 2,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(1, 1)
        };

        // 静态半透明阴影（不用 DropShadowEffect，避免每盘全量重建时的 GPU 开销）
        var shadow = new Ellipse
        {
            Fill = new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)),
            Margin = new Thickness(StoneRadius * 0.15, StoneRadius * 0.35, 0, 0),
            IsHitTestVisible = false
        };
        cell.Children.Add(shadow);

        var stoneEllipse = new Ellipse
        {
            Fill = CreateStoneBrush(stone)
        };
        cell.Children.Add(stoneEllipse);

        // 球面反光：位于棋子内部左上角，颜色更淡、尺寸更小
        var highlight = new Ellipse
        {
            Width = StoneRadius * 0.85,
            Height = StoneRadius * 0.45,
            Fill = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)),
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(StoneRadius * 0.25, StoneRadius * 0.2, 0, 0)
        };
        cell.Children.Add(highlight);

        Canvas.SetLeft(cell, col * CellSize - StoneRadius);
        Canvas.SetTop(cell, row * CellSize - StoneRadius);
        BoardCanvas.Children.Add(cell);
        return cell;
    }

    /// <summary>球面径向渐变：光从左上照入，中心高光、边缘渐暗，形成立体棋子。</summary>
    private static Brush CreateStoneBrush(Stone stone)
    {
        var brush = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.35, 0.3),
            Center = new Point(0.5, 0.5),
            RadiusX = 0.55,
            RadiusY = 0.55
        };

        if (stone == Stone.Black)
        {
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x66, 0x66, 0x66), 0.0));
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x28, 0x28, 0x28), 0.45));
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x00, 0x00, 0x00), 1.0));
        }
        else
        {
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0xFF, 0xFF), 0.0));
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(0xE6, 0xE6, 0xE6), 0.55));
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(0xB0, 0xB0, 0xB0), 1.0));
        }

        brush.Freeze();
        return brush;
    }

    /// <summary>棋子投影：黑子深、白子浅，增强悬浮感。</summary>
    private static DropShadowEffect CreateStoneShadow(Stone stone)
    {
        return new DropShadowEffect
        {
            BlurRadius = 4,
            ShadowDepth = 2,
            Direction = 270,
            Opacity = stone == Stone.Black ? 0.55 : 0.3,
            Color = Color.FromRgb(0x2A, 0x1F, 0x10)
        };
    }

    /// <summary>落子动画：棋子几乎就在交叉点出现，只做轻微下坠、缩放与淡入，立体感由投影保留。</summary>
    private static void AnimateStoneAppear(FrameworkElement stone)
    {
        stone.RenderTransformOrigin = new Point(0.5, 0.5);
        var scale = new ScaleTransform(0.88, 0.88);
        var move = new TranslateTransform(0, -7);
        var group = new TransformGroup();
        group.Children.Add(scale);
        group.Children.Add(move);
        stone.RenderTransform = group;

        var storyboard = new Storyboard();

        // 三个动作统一 220ms、同时开始，用 CubicEase EaseOut 让起手快、收尾缓，曲线更顺。
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        // 1) 轻微下坠：仅 7px
        var fall = new DoubleAnimation(-7, 0, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = ease
        };
        Storyboard.SetTarget(fall, move);
        Storyboard.SetTargetProperty(fall, new PropertyPath(TranslateTransform.YProperty));
        storyboard.Children.Add(fall);

        // 2) 轻微缩放展开（横向/纵向同步）
        var growX = new DoubleAnimation(0.88, 1.0, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = ease
        };
        Storyboard.SetTarget(growX, scale);
        Storyboard.SetTargetProperty(growX, new PropertyPath(ScaleTransform.ScaleXProperty));
        storyboard.Children.Add(growX);

        var growY = new DoubleAnimation(0.88, 1.0, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = ease
        };
        Storyboard.SetTarget(growY, scale);
        Storyboard.SetTargetProperty(growY, new PropertyPath(ScaleTransform.ScaleYProperty));
        storyboard.Children.Add(growY);

        // 3) 淡入（从 0.4 起，不做全透明，避免"凭空冒出来"感）
        var fade = new DoubleAnimation(0.5, 1.0, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = ease
        };
        Storyboard.SetTarget(fade, stone);
        Storyboard.SetTargetProperty(fade, new PropertyPath(UIElement.OpacityProperty));
        storyboard.Children.Add(fade);

        // 动画结束后彻底清空变换与透明度，棋子恢复纯 26x26 正常大小，
        // 避免 HoldEnd 残留任何缩放/位移导致最新一颗棋大小不对。
        storyboard.Completed += (_, _) =>
        {
            stone.RenderTransform = Transform.Identity;
            stone.Opacity = 1.0;
        };

        storyboard.Begin();
    }

    private void BoardCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_shifting)
        {
            return; // 棋盘扩展动画进行中
        }

        if (_controller.State != GameSessionState.Playing || !_controller.CanHumanPlace)
        {
            if (_controller.State == GameSessionState.Playing && _controller.Mode == GameMode.VsAi)
            {
                StatusText.Text = "AI 正在思考，请稍候";
            }
            return;
        }

        Point p = e.GetPosition(BoardCanvas);
        int gx = (int)Math.Round(p.X / CellSize);
        int gy = (int)Math.Round(p.Y / CellSize);
        if (gx < 0 || gx >= Board.Size || gy < 0 || gy >= Board.Size)
        {
            return;
        }

        // 窗口相对坐标 → 世界坐标
        Board board = _controller.Board;
        MoveResult result = _controller.ApplyMove(new Position(board.ViewRow + gy, board.ViewCol + gx));
        Refresh();
        if (result == MoveResult.BoardFull)
        {
            ShowBoardFullPanel();
            return;
        }
        TryAiTurn();
    }

    /// <summary>人机模式下轮到 AI 时自动落子；AI 未就绪时保持界面提示。</summary>
    private void TryAiTurn()
    {
        if (_shifting || _aiBusy || _controller.Mode != GameMode.VsAi || _controller.CurrentPlayer == _controller.HumanColor)
        {
            return;
        }

        _aiBusy = true;
        StatusText.Text = "AI 正在思考，请稍候";

        // AI 搜索在后台线程执行，避免阻塞界面（否则落子动画与整窗都会卡住）
        _aiCts = new System.Threading.CancellationTokenSource();
        System.Threading.CancellationToken token = _aiCts.Token;
        var task = System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                _controller.TryPerformAiMove(out _, token);
            }
            catch (OperationCanceledException)
            {
                // 玩家已认输等：放弃本次落子
            }
        });
        task.ContinueWith(_ =>
        {
            Dispatcher.BeginInvoke((Action)(() =>
            {
                if (token.IsCancellationRequested)
                {
                    _aiBusy = false;
                    return; // 已被认输等操作取消，不再执行落子后处理
                }
                OnAiMoveCompleted();
            }));
        }, System.Threading.Tasks.TaskScheduler.Default);
    }

    /// <summary>AI 思考完成回到界面线程：同步棋盘、结算或继续。</summary>
    private void OnAiMoveCompleted()
    {
        _aiBusy = false;
        if (!IsLoaded)
        {
            return; // 用户已返回主菜单，不再操作已卸载的视图
        }

        if (_controller.State == GameSessionState.Playing && _controller.IsViewFull)
        {
            // AI 落子后棋盘占满：等待玩家选择和棋或扩展
            Refresh();
            ShowBoardFullPanel();
            return;
        }

        if (_controller.State == GameSessionState.Finished)
        {
            // AI 获胜：先立刻画出这步落子，延迟片刻后再弹结算，让玩家先看清
            _delayEnd = true;
            Refresh();
            _endTimer?.Stop();
            _endTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
            _endTimer.Tick += (_, _) => { _endTimer.Stop(); _delayEnd = false; PlayEndAnimation(_controller.Winner); };
            _endTimer.Start();
        }
        else
        {
            Refresh();
        }
    }

    private void UndoButton_Click(object sender, RoutedEventArgs e)
    {
        if (_aiBusy)
        {
            return; // AI 思考中不允许悔棋，避免与后台搜索冲突
        }

        if (_shifting)
        {
            return; // 棋盘扩展动画中不允许悔棋
        }

        BoardFullPanel.Visibility = Visibility.Collapsed;
        DevPanel.Visibility = Visibility.Collapsed;
        if (_controller.Undo())
        {
            Refresh();
            TryAiTurn();
        }
    }

    /// <summary>棋盘占满：停止计时并弹出「和棋/扩展方向」选择面板。</summary>
    private void ShowBoardFullPanel()
    {
        StopTurnTimer();
        BoardFullPanel.Visibility = Visibility.Visible;
        StatusText.Text = "棋盘已满：可选择和棋，或选择方向扩展棋盘继续";
    }

    // ---------- 开发者测试模块 ----------

    /// <summary>打开开发者测试面板（AI 思考或扩展动画期间不允许，避免破坏进行中的对局状态）。</summary>
    private void DevButton_Click(object sender, RoutedEventArgs e)
    {
        if (_aiBusy)
        {
            StatusText.Text = "AI 正在思考，请稍候再测试";
            return;
        }

        if (_shifting)
        {
            StatusText.Text = "棋盘扩展动画中，请稍候再测试";
            return;
        }

        if (_controller.State != GameSessionState.Playing)
        {
            StatusText.Text = "对局已结束，请先重开一局再测试";
            return;
        }

        StopTurnTimer();
        DevPanel.Visibility = Visibility.Visible;
    }

    private void DevClose_Click(object sender, RoutedEventArgs e)
    {
        DevPanel.Visibility = Visibility.Collapsed;
        if (_controller.State == GameSessionState.Playing && !_aiBusy && !_shifting)
        {
            StartTurnTimer();
            TryAiTurn();
        }
    }

    /// <summary>一键填满当前可见棋盘（黑白交替直接落子、跳过胜负检测），随后弹出占满扩展面板供手动测试。</summary>
    private void DevFillBoard_Click(object sender, RoutedEventArgs e)
    {
        Board board = _controller.Board;
        for (int row = board.ViewRow; row < board.ViewRow + Board.Size; row++)
        {
            for (int col = board.ViewCol; col < board.ViewCol + Board.Size; col++)
            {
                if (board.IsEmpty(row, col))
                {
                    Stone color = (row + col) % 2 == 0 ? Stone.Black : Stone.White;
                    board.PlaceStone(new Position(row, col), color);
                }
            }
        }

        DevPanel.Visibility = Visibility.Collapsed;
        Refresh();
        ShowBoardFullPanel();
    }

    /// <summary>快速获胜：在当前窗口找一条水平线上连续 5 个空位，前 4 子直接落盘、第 5 子走正式流程触发胜利结算。</summary>
    private void DevWin_Click(object sender, RoutedEventArgs e)
    {
        Board board = _controller.Board;
        var line = FindFiveEmptyCells(board);
        if (line == null)
        {
            StatusText.Text = "找不到连续 5 个空位，无法快速获胜";
            return;
        }

        Stone my = board.CurrentPlayer == 0 ? Stone.Black : Stone.White;
        for (int i = 0; i < 4; i++)
        {
            board.PlaceStone(line[i], my);
        }

        // 4 次 PlaceStone 翻转后 CurrentPlayer 回到原值，第 5 子颜色与前面一致，触发五连
        DevPanel.Visibility = Visibility.Collapsed;
        _controller.ApplyMove(line[4]);
        Refresh(); // UpdateStatusAndTimer 自动播放胜利结算动画
    }

    /// <summary>在当前可见窗口内找水平方向连续 5 个空位（按行从上到下扫描），找不到返回 null。</summary>
    private static List<Position>? FindFiveEmptyCells(Board board)
    {
        for (int row = board.ViewRow; row < board.ViewRow + Board.Size; row++)
        {
            int run = 0;
            for (int col = board.ViewCol; col < board.ViewCol + Board.Size; col++)
            {
                if (board.IsEmpty(row, col))
                {
                    run++;
                    if (run >= 5)
                    {
                        var cells = new List<Position>();
                        for (int c = col - 4; c <= col; c++)
                        {
                            cells.Add(new Position(row, c));
                        }
                        return cells;
                    }
                }
                else
                {
                    run = 0;
                }
            }
        }

        return null;
    }

    /// <summary>选择「和棋」：以平局结束本局。</summary>
    private void BoardFull_Draw_Click(object sender, RoutedEventArgs e)
    {
        BoardFullPanel.Visibility = Visibility.Collapsed;
        _controller.DrawGame();
        Refresh(); // UpdateStatusAndTimer 会显示"平局！"并播放平局结算动画
    }

    /// <summary>选择扩展方向（Button.Tag 为 "0"=上 "1"=下 "2"=左 "3"=右），播放棋盘平移动画后继续对局。</summary>
    private void BoardFull_Extend_Click(object sender, RoutedEventArgs e)
    {
        // XAML 中 Tag="0" 是字符串，须解析为 int（直接按 int 匹配会失败导致点了没反应）
        if (sender is not Button { Tag: string tag } || !int.TryParse(tag, out int direction))
        {
            return;
        }

        BoardFullPanel.Visibility = Visibility.Collapsed;
        if (!_controller.ExtendBoard(direction))
        {
            StatusText.Text = "已达棋盘扩展极限，无法继续扩展";
            ShowBoardFullPanel(); // 重新打开面板，让用户改选和棋
            return;
        }

        StartBoardShiftAnimation(direction);
    }

    /// <summary>
    /// 棋盘平移动画：在约 600ms 内把窗口偏移从旧值平滑过渡到新值，窗口内棋子同步位移，
    /// 视觉上棋盘向扩展方向的反方向平移半块，露出另一半新棋盘。
    /// </summary>
    private void StartBoardShiftAnimation(int direction)
    {
        _shifting = true;
        Board b = _controller.Board;
        _shiftToRow = b.ViewRow;
        _shiftToCol = b.ViewCol;
        _shiftFromRow = _shiftToRow - (direction == 0 ? -Board.ExtendStep : direction == 1 ? Board.ExtendStep : 0);
        _shiftFromCol = _shiftToCol - (direction == 2 ? -Board.ExtendStep : direction == 3 ? Board.ExtendStep : 0);
        _shiftStart = DateTime.Now;

        _shiftTimer?.Stop();
        _shiftTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _shiftTimer.Tick += OnShiftTick;
        _shiftTimer.Start();
    }

    private void OnShiftTick(object? sender, EventArgs e)
    {
        double t = (DateTime.Now - _shiftStart).TotalMilliseconds / 600.0;
        if (t >= 1)
        {
            _shiftTimer?.Stop();
            _shifting = false;
            SyncBoard(); // 按新窗口重画并清理移出窗口的棋子
            UpdateLastMoveMarker();
            if (_controller.State == GameSessionState.Playing)
            {
                StatusText.Text = "";
                StartTurnTimer();
                TryAiTurn();
            }
            return;
        }

        // ease-out 缓动：先快后慢，更自然
        double eased = 1 - Math.Pow(1 - t, 3);
        double interpRow = _shiftFromRow + (_shiftToRow - _shiftFromRow) * eased;
        double interpCol = _shiftFromCol + (_shiftToCol - _shiftFromCol) * eased;

        foreach (var kv in _stoneCache)
        {
            Position pos = kv.Key;
            Canvas.SetLeft(kv.Value, (pos.Col - interpCol) * CellSize - StoneRadius);
            Canvas.SetTop(kv.Value, (pos.Row - interpRow) * CellSize - StoneRadius);
        }
    }

    /// <summary>当前玩家主动认输，弹出确认框避免误点。AI 思考时认输会立即中止 AI 搜索。</summary>
    private void ResignButton_Click(object sender, RoutedEventArgs e)
    {
        BoardFullPanel.Visibility = Visibility.Collapsed;
        DevPanel.Visibility = Visibility.Collapsed;
        if (_controller.State != GameSessionState.Playing)
        {
            return; // 对局已结束，无需认输
        }

        var answer = MessageBox.Show(
            "确定要认输吗？本局将立即结束并记录为对手获胜。",
            "认输确认",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);

        if (answer != MessageBoxResult.OK)
        {
            return;
        }

        _aiCts?.Cancel(); // 若 AI 正在思考，立即停止等待
        if (_controller.Resign())
        {
            Refresh();
        }
    }

    /// <summary>返回上一界面（例如回到主菜单）。</summary>
    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        _shiftTimer?.Stop();
        _shifting = false;
        BoardFullPanel.Visibility = Visibility.Collapsed;
        DevPanel.Visibility = Visibility.Collapsed;
        if (Window.GetWindow(this) is MainWindow window)
        {
            window.GoBack();
        }
    }
    private void FullScreen_Click(object sender, RoutedEventArgs e)
        => (Window.GetWindow(this) as MainWindow)?.ToggleFullScreen();

    private void Exit_Click(object sender, RoutedEventArgs e)
        => System.Windows.Application.Current.Shutdown();

    private void RestartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_aiBusy)
        {
            return; // AI 思考中暂不允许重开
        }

        if (_shifting)
        {
            return; // 棋盘扩展动画中不允许重开
        }

        BoardFullPanel.Visibility = Visibility.Collapsed;
        DevPanel.Visibility = Visibility.Collapsed;
        if (_controller.Restart())
        {
            Refresh();
            TryAiTurn(); // 玩家执白时重开后黑方（AI）先行；执黑时内部会直接跳过
        }
    }
}
