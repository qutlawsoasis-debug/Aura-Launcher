using System;
using System.Text.RegularExpressions;

namespace AuraLauncher.Core.WebBridge;

/// <summary>
/// Ссылки aura://join/КОД и aura://friend/КОД (как MainViewModel.HandleProtocolUri):
/// разбираются здесь, а подтверждение показывает интерфейс.
/// </summary>
public partial class WebBridge
{
    private object? _pendingProtocol;

    /// <summary>Разбор ссылки; null — ссылка не подходит.</summary>
    internal static (string Action, string Code)? ParseProtocolUri(string? rawUri)
    {
        if (string.IsNullOrWhiteSpace(rawUri)) return null;
        var cleaned = rawUri.Trim().Trim('"', '\'').Trim();
        if (cleaned.StartsWith("aura://", StringComparison.OrdinalIgnoreCase)) cleaned = cleaned.Substring(7);
        else if (cleaned.StartsWith("aura:", StringComparison.OrdinalIgnoreCase)) cleaned = cleaned.Substring(5);

        var parts = cleaned.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return null;

        string action = parts[0].Trim().ToLowerInvariant();
        string code = parts[1].Trim().ToUpperInvariant();
        if (action != "join" && action != "friend") return null;
        if (!Regex.IsMatch(code, "^[A-Z0-9]{6,8}$")) return null;
        return (action, code);
    }

    /// <summary>Ссылка пришла при запуске или из второго экземпляра лаунчера.</summary>
    public void HandleProtocolUri(string? rawUri)
    {
        _logger($"[PROTOCOL] Received URI: {rawUri}");
        var parsed = ParseProtocolUri(rawUri);
        if (parsed == null)
        {
            _logger($"[PROTOCOL: WARN] Unsupported URI: {rawUri}");
            return;
        }
        _pendingProtocol = new { action = parsed.Value.Action, code = parsed.Value.Code };
        SendEvent("protocol.prompt", _pendingProtocol);
    }

    /// <summary>Интерфейс забирает ссылку, пришедшую до загрузки страницы.</summary>
    private void HandleProtocolPending(BridgeMessage msg)
    {
        var pending = _pendingProtocol;
        _pendingProtocol = null;
        SendResponse(msg.Id, msg.Name, new { pending });
    }
}
