using System.IO;
using System.Net.Sockets;
using System.Text;

namespace Gomoku.Core.Networking;

/// <summary>
/// 联机客户端：与房间服务器保持一条 TCP 长连接，按「一行一条 JSON」收发消息。
/// 接收在后台线程进行，通过 <see cref="MessageReceived"/> 抛给界面层；
/// 连接断开时抛 <see cref="ConnectionClosed"/>，由界面提示对手或服务器不可用。
/// </summary>
public sealed class GameClient : IDisposable
{
    private TcpClient? _tcp;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private CancellationTokenSource? _cts;
    private readonly object _sendLock = new();
    private bool _disposed;

    /// <summary>收到服务器消息（在后台线程触发，界面层需切回 UI 线程）。</summary>
    public event EventHandler<NetworkMessage>? MessageReceived;

    /// <summary>连接断开（服务器关闭、网络异常或主动 Dispose）。</summary>
    public event EventHandler<string>? ConnectionClosed;

    /// <summary>当前是否已连接。</summary>
    public bool IsConnected => _tcp?.Connected == true;

    /// <summary>连接服务器；失败时抛出异常由调用方提示。</summary>
    public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        if (_tcp is not null)
        {
            throw new InvalidOperationException("客户端已连接，请先断开。");
        }

        var tcp = new TcpClient();
        await tcp.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);
        _tcp = tcp;

        NetworkStream stream = tcp.GetStream();
        _reader = new StreamReader(stream, new UTF8Encoding(false));
        _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => ReadLoopAsync(_cts.Token));
    }

    /// <summary>发送一条消息；未连接时静默丢弃并返回 false。</summary>
    public bool Send(string type, IReadOnlyDictionary<string, object?>? payload = null)
    {
        if (_writer is null)
        {
            return false;
        }

        string line = GameProtocol.Encode(type, payload);
        lock (_sendLock)
        {
            try
            {
                _writer.WriteLine(line);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }
    }

    /// <summary>后台读循环：逐行解析并分发，连接结束时通知界面。</summary>
    private async Task ReadLoopAsync(CancellationToken token)
    {
        string reason = "与服务器的连接已断开";
        try
        {
            while (!token.IsCancellationRequested && _reader is not null)
            {
                string? line = await _reader.ReadLineAsync(token).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                if (GameProtocol.TryDecode(line, out NetworkMessage? message) && message is not null)
                {
                    MessageReceived?.Invoke(this, message);
                }
            }
        }
        catch (OperationCanceledException)
        {
            reason = "已断开连接";
        }
        catch (IOException)
        {
            reason = "与服务器的连接中断";
        }
        catch (ObjectDisposedException)
        {
            reason = "已断开连接";
        }
        finally
        {
            if (!_disposed)
            {
                ConnectionClosed?.Invoke(this, reason);
            }
        }
    }

    /// <summary>关闭连接并释放资源。</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        try
        {
            _writer?.Dispose();
        }
        catch (IOException)
        {
            // 关闭阶段写端已断，忽略
        }

        _reader?.Dispose();
        _tcp?.Close();
        _tcp = null;
        _reader = null;
        _writer = null;
    }
}
