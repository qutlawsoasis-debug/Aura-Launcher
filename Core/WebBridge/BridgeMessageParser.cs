using System;
using System.Text.Json;

namespace AuraLauncher.Core.WebBridge;

public static class BridgeMessageParser
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static (BridgeMessage? Message, string? Error) Parse(string? rawInput)
    {
        if (string.IsNullOrWhiteSpace(rawInput))
        {
            return (null, "empty_message");
        }

        try
        {
            // If the string starts and ends with quotes, or is a JSON-encoded string literal,
            // Try unescaping/deserializing string or direct object
            string trimmed = rawInput.Trim();
            if (trimmed.StartsWith("\"") && trimmed.EndsWith("\"") && trimmed.Length >= 2)
            {
                try
                {
                    string unescaped = JsonSerializer.Deserialize<string>(trimmed, SerializerOptions) ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(unescaped))
                    {
                        var innerMsg = JsonSerializer.Deserialize<BridgeMessage>(unescaped, SerializerOptions);
                        if (innerMsg != null)
                        {
                            return (innerMsg, null);
                        }
                    }
                }
                catch
                {
                    // Fall back to direct deserialization
                }
            }

            var msg = JsonSerializer.Deserialize<BridgeMessage>(trimmed, SerializerOptions);
            if (msg == null)
            {
                return (null, "null_message");
            }

            return (msg, null);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }
}
