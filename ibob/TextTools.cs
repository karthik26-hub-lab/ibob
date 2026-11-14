using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ibob;

// Small developer helpers that work on copied text: JSON, Base64, URLs, JWTs, file hashes, UUIDs.
// Each takes text and returns the result text (or throws with a readable message).
static class TextTools
{
    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    static readonly JsonSerializerOptions Compact = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string FormatJson(string text) => JsonSerializer.Serialize(ParseJson(text), Pretty);
    public static string MinifyJson(string text) => JsonSerializer.Serialize(ParseJson(text), Compact);

    static JsonElement ParseJson(string text)
    {
        try { return JsonDocument.Parse(text).RootElement; }
        catch (JsonException e) { throw new FormatException("not valid JSON (" + e.Message.Split('.')[0] + ")"); }
    }

    // Decodes if the text is Base64 of readable text, otherwise encodes it.
    public static string Base64(string text)
    {
        string trimmed = text.Trim();
        if (trimmed.Length % 4 == 0 && Regex.IsMatch(trimmed, @"^[A-Za-z0-9+/]+={0,2}$"))
        {
            try { return new UTF8Encoding(false, true).GetString(System.Convert.FromBase64String(trimmed)); }
            catch (DecoderFallbackException) { }   // bytes aren't text: encode instead
        }
        return System.Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
    }

    // Decodes "%20"-style text, otherwise encodes it for use in a URL.
    public static string Url(string text) =>
        Regex.IsMatch(text, "%[0-9A-Fa-f]{2}") ? Uri.UnescapeDataString(text) : Uri.EscapeDataString(text);

    // Shows a JWT's header and payload as readable JSON, plus when it expires.
    public static string Jwt(string text)
    {
        var parts = text.Trim().Split('.');
        if (parts.Length != 3) throw new FormatException("not a JWT (expected three parts separated by dots)");

        string payload = FromBase64Url(parts[1]);
        string result = "Header:\n" + FormatJson(FromBase64Url(parts[0])) + "\n\nPayload:\n" + FormatJson(payload);
        if (ParseJson(payload).TryGetProperty("exp", out var exp) && exp.TryGetInt64(out long seconds))
            result += "\n\nExpires: " + DateTimeOffset.FromUnixTimeSeconds(seconds).LocalDateTime.ToString("d MMM yyyy, h:mm tt");
        return result;
    }

    static string FromBase64Url(string part)
    {
        string base64 = part.Replace('-', '+').Replace('_', '/');
        base64 += new string('=', (4 - base64.Length % 4) % 4);
        return Encoding.UTF8.GetString(System.Convert.FromBase64String(base64));
    }

    // SHA-256 of a file, as lowercase hex (what download pages usually show for checking a file).
    public static string Sha256(string path)
    {
        using var file = File.OpenRead(path);
        return System.Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant();
    }

    public static string NewUuid() => Guid.NewGuid().ToString();
}
