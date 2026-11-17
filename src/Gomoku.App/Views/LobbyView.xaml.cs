using System.Windows;
using System.Windows.Controls;
using Gomoku.App;
using Gomoku.Core.Game;
using Gomoku.Core.Networking;

namespace Gomoku.App.Views;

/// <summary>
/// 联机大厅：连接服务器、创建房间或输入房号加入。
/// 收到 start_game 后把连接交给对局界面，由 GameBoardView 继续收发棋步。
/// </summary>
public partial class LobbyView : UserControl
{
    private const int DefaultPort = 9527;

    private readonly GameClient _client = new();
    private bool _busy;
    private int _color = -1;
    private bool _enteredGame;

    public LobbyView()
    {
        InitializeComponent();
        _client.MessageReceived += OnMessage;
        _client.ConnectionClosed += OnConnectionClosed;
        Loaded += (_, _) =>
        {
            ServerBox.Focus();
            ServerBox.SelectAll();
        };
        Unloaded += OnUnloaded;
    }

    /// <summary>离开大厅且未进入对局时，断开连接。</summary>
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_enteredGame)
        {
            return; // 连接已交给对局界面
        }

        _client.MessageReceived -= OnMessage;
        _client.ConnectionClosed -= OnConnectionClosed;
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
                EnterGame();
                break;

            case GameProtocol.Error:
                StatusText.Text = GameProtocol.GetString(message, "message") ?? "服务器返回错误";
                _busy = false;
                break;

            default:
                break;
        }
    }

    /// <summary>进入对局：把已建立的连接与自己的执子颜色交给棋盘界面。</summary>
    private void EnterGame()
    {
        if (_enteredGame)
        {
            return;
        }

        _enteredGame = true;
        _client.MessageReceived -= OnMessage;
        _client.ConnectionClosed -= OnConnectionClosed;

        var controller = new GameController();
        controller.StartSession(GameMode.Online, _color);

        string material = App.Settings.BoardMaterialIndex == 1 ? "stone" : "wood";
        bool videoBackground = App.Settings.BackgroundIndex == 1;
        if (Window.GetWindow(this) is MainWindow window)
        {
            window.Navigate(new GameBoardView(controller, material, videoBackground, _client));
        }
    }

    private void OnConnectionClosed(object? sender, string reason)
        => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!_enteredGame)
            {
                StatusText.Text = $"连接已断开：{reason}";
                _busy = false;
            }
        }));
}
