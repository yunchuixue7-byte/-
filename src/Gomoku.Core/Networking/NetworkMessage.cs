using System.Text.Json;

namespace Gomoku.Core.Networking;

/// <summary>联机消息体（消息类型 + JSON 载荷）。</summary>
public sealed record NetworkMessage(string Type, JsonElement? Payload);
