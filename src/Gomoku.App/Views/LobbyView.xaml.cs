using System.Windows;
using System.Windows.Controls;
using Gomoku.App;
using Gomoku.Core.Game;
using Gomoku.Core.Networking;

namespace Gomoku.App.Views;

/// <summary>
/// 联机大厅：连接服务器、注册登录、快速匹配，或创建/按房号加入房间。
/// 收到 start_game 后把连接交给对局界面，由 GameBoardView 继续收发棋步。
/// </summary>
public partial class LobbyView : UserControl
{
    private const int DefaultPort = 9527;

    private GameClient _client = new();
    private bool _busy;
    private int _color = -1;
    private bool _enteredGame;
    private bool _matching;
    private string? _nickname;
    private string? _opponentName;

    public LobbyView()
    {
        InitializeComponent();
        AttachClient();
        Loaded += (_, _) =>
        {
            ServerBox.Focus();
            ServerBox.SelectAll();
        };
        Unloaded += OnUnloaded;
    }

    private void AttachClient()
    {
        _client.MessageReceived += OnMessage;
        _client.ConnectionClosed += OnConnectionClosed;
    }

    private void DetachClient()
    {
        _client.MessageReceived -= OnMessage;
        _client.ConnectionClosed -= OnConnectionClosed;
    }

    /// <summary>离开大厅且未进入对局时，断开连接。</summary>
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_enteredGame)
        {
            return; // 连接已交给对局界面
        }

        DetachClient();
        _client.Dispose();
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        _client.Dispose();
        if (Window.GetWindow(this) is MainWindow window)
        {
            window.GoBack();
        }
    }

    // ---------- 账号 ----------

    private async void Register_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        string user = UserBox.Text.Trim();
        string pass = PassBox.Password;
        if (user.Length == 0 || pass.Length == 0)
        {
            StatusText.Text = "请先填写用户名和密码";
            return;
        }

        if (!await EnsureConnected())
        {
            return;
        }

        _busy = true;
        StatusText.Text = "正在注册…";
        _client.Send(GameProtocol.Register, new Dictionary<string, object?>
        {
            ["user"] = user,
            ["pass"] = pass
        });
    }

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        string user = UserBox.Text.Trim();
        string pass = PassBox.Password;
        if (user.Length == 0 || pass.Length == 0)
        {
            StatusText.Text = "请先填写用户名和密码";
            return;
        }

        if (!await EnsureConnected())
        {
            return;
        }

        _busy = true;
        StatusText.Text = "正在登录…";
        _client.Send(GameProtocol.Login, new Dictionary<string, object?>
        {
            ["user"] = user,
            ["pass"] = pass
        });
    }

    private void Logout_Click(object sender, RoutedEventArgs e)
    {
        if (_client.IsConnected)
        {
            _client.Send(GameProtocol.Logout);
        }

        ApplyLoggedOut();
        StatusText.Text = "已退出登录，当前为游客身份";
    }

    private void ApplyLoggedOut()
    {
        _nickname = null;
        LogoutButton.Visibility = Visibility.Collapsed;
        UserBox.IsEnabled = true;
        AccountText.Text = "当前以游客身份对战，战绩不会保存";
    }

    // ---------- 匹配 ----------

    private async void Match_Click(object sender, RoutedEventArgs e)
    {
        if (_matching)
        {
            if (_client.IsConnected)
            {
                _client.Send(GameProtocol.CancelMatch);
            }

            _matching = false;
            _busy = false;
            MatchButton.Content = "快速匹配（按胜率找对手）";
            StatusText.Text = "已取消匹配";
            return;
        }

        if (_busy)
        {
            return;
        }

        if (!await EnsureConnected())
        {
            return;
        }

        _matching = true;
        _busy = true;
        MatchButton.Content = "取消匹配";
        StatusText.Text = "正在寻找对手…";
        _client.Send(GameProtocol.QuickMatch);
    }

    // ---------- 房间 ----------

    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        if (!await EnsureConnected())
        {
            return;
        }

        _busy = true;
        StatusText.Text = "正在创建房间…";
        _client.Send(GameProtocol.CreateRoom);
    }

    private async void Join_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        string code = RoomBox.Text.Trim();
        if (code.Length != 4 || !code.All(char.IsDigit))
        {
            StatusText.Text = "请输入四位数字房间号";
            return;
        }

        if (!await EnsureConnected())
        {
            return;
        }

        _busy = true;
        StatusText.Text = $"正在加入房间 {code}…";
        _client.Send(GameProtocol.JoinRoom, new Dictionary<string, object?>
        {
            ["room"] = code
        });
    }

    /// <summary>解析服务器地址（host:port 或纯端口），并建立连接。</summary>
    private async Task<bool> EnsureConnected()
    {
        if (_client.IsConnected)
        {
            return true;
        }

        string address = ServerBox.Text.Trim();
        string host = "127.0.0.1";
        int port = DefaultPort;
        if (address.Contains(':'))
        {
            string[] parts = address.Split(':', 2);
            host = parts[0].Trim();
            if (int.TryParse(parts[1].Trim(), out int parsedPort))
            {
                port = parsedPort;
            }
        }
        else if (int.TryParse(address, out int onlyPort))
        {
            port = onlyPort;
        }
        else if (address.Length > 0)
        {
            host = address;
        }

        StatusText.Text = $"正在连接 {host}:{port}…";
        try
        {
            await _client.ConnectAsync(host, port);
            StatusText.Text = "已连接服务器";
            return true;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"连接失败：{ex.Message}。请确认服务端已启动（Gomoku.Server）。";
            return false;
        }
    }

    /// <summary>服务器消息回到界面线程处理。</summary>
    private void OnMessage(object? sender, NetworkMessage message)
        => Dispatcher.BeginInvoke(new Action(() => HandleMessage(message)));

    private void HandleMessage(NetworkMessage message)
    {
        switch (message.Type)
        {
            case GameProtocol.AuthOk:
            {
                _nickname = GameProtocol.GetString(message, "user");
                int wins = GameProtocol.GetInt(message, "wins");
                int losses = GameProtocol.GetInt(message, "losses");
                int draws = GameProtocol.GetInt(message, "draws");
                int rate = GameProtocol.GetInt(message, "rate");
                AccountText.Text = $"{_nickname}：{wins} 胜 {losses} 负 {draws} 平，胜率 {rate}%";
                LogoutButton.Visibility = Visibility.Visible;
                UserBox.IsEnabled = false;
                PassBox.Password = string.Empty;
                StatusText.Text = "登录成功，可以快速匹配或建房了";
                _busy = false;
                break;
            }

            case GameProtocol.AuthFail:
                StatusText.Text = GameProtocol.GetString(message, "message") ?? "注册或登录失败";
                _busy = false;
                break;

            case GameProtocol.LoggedOut:
                ApplyLoggedOut();
                break;

            case GameProtocol.MatchWaiting:
                _matching = true;
                _busy = false;
                MatchButton.Content = "取消匹配";
                StatusText.Text = GameProtocol.GetString(message, "message") ?? "已在匹配队列中，等待对手…";
                break;

            case GameProtocol.MatchCanceled:
                _matching = false;
                _busy = false;
                MatchButton.Content = "快速匹配（按胜率找对手）";
                StatusText.Text = "已取消匹配";
                break;

            case GameProtocol.RoomCreated:
                _color = GameProtocol.GetInt(message, "color", 0);
                RoomCodeText.Text = $"房间号 {GameProtocol.GetString(message, "room")}";
                StatusText.Text = "房间已创建，把房间号告诉对手，等待对方加入…";
                _busy = false;
                break;

            case GameProtocol.RoomJoined:
                _color = GameProtocol.GetInt(message, "color", 1);
                RoomCodeText.Text = $"已加入房间 {GameProtocol.GetString(message, "room")}";
                StatusText.Text = "加入成功，等待对局开始…";
                _busy = false;
                break;

            case GameProtocol.GameStarted:
                OnGameStarted(message);
                break;

            case GameProtocol.Error:
                StatusText.Text = GameProtocol.GetString(message, "message") ?? "服务器返回错误";
                _busy = false;
                _matching = false;
                MatchButton.Content = "快速匹配（按胜率找对手）";
                break;

            default:
                break;
        }
    }

    /// <summary>开局消息自带执子颜色与双方昵称：匹配进入时没有 room_joined 可依赖。</summary>
    private void OnGameStarted(NetworkMessage message)
    {
        int color = GameProtocol.GetInt(message, "color", _color);
        _color = color;

        string black = GameProtocol.GetString(message, "black") ?? "黑方";
        string white = GameProtocol.GetString(message, "white") ?? "白方";
        _opponentName = color == 0 ? white : black;

        _matching = false;
        _busy = false;
        EnterGame();
    }

    /// <summary>进入对局：把已建立的连接、自己的执子颜色与对手昵称交给棋盘界面。</summary>
    private void EnterGame()
    {
        if (_enteredGame)
        {
            return;
        }

        _enteredGame = true;
        DetachClient();

        var controller = new GameController();
        controller.StartSession(GameMode.Online, _color);

        string material = App.Settings.BoardMaterialIndex == 1 ? "stone" : "wood";
        bool videoBackground = App.Settings.BackgroundIndex == 1;
        if (Window.GetWindow(this) is MainWindow window)
        {
            window.Navigate(new GameBoardView(controller, material, videoBackground, _client, _opponentName));
        }
    }

    /// <summary>连接断开：未进对局时提示，并换一个干净的客户端实例，便于重新连接。</summary>
    private void OnConnectionClosed(object? sender, string reason)
        => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_enteredGame)
            {
                return;
            }

            DetachClient();
            _client.Dispose();
            _client = new GameClient();
            AttachClient();

            _busy = false;
            _matching = false;
            MatchButton.Content = "快速匹配（按胜率找对手）";
            StatusText.Text = $"连接已断开：{reason}";
        }));
}
