using System.Text.RegularExpressions;

namespace NovaClient.Core.Logging;

/// <summary>
/// Removes secrets from any text before it reaches a log sink. Covers Microsoft access/refresh
/// tokens, JWTs (Minecraft/Xbox/XSTS), OAuth authorization codes, PKCE verifiers, Authorization
/// headers, XBL3.0 tokens, cookies and device codes. Behavior is locked in by automated tests.
/// </summary>
public static partial class LogRedactor
{
    private const string Redacted = "[REDACTED]";

    // Ordered: structured token formats first, then generic key/value pairs, then raw token shapes.
    // KeepsPrefix patterns preserve their group(1) (the label) and redact only the value.
    private static readonly (Regex Regex, bool KeepsPrefix)[] Patterns =
    {
        // Xbox/Minecraft identity headers: "XBL3.0 x=<uhs>;<xsts jwt>"
        (new Regex(@"(XBL3\.0\s+x=)\S+", RegexOptions.Compiled), true),
        // HTTP Authorization header values
        (new Regex(@"(Bearer\s+)[A-Za-z0-9\-_.~+/=]+", RegexOptions.Compiled), true),
        // JSON or query values for sensitive keys:  "access_token":"..."  code=...  code_verifier=...
        (new Regex(@"([""']?(?:access_token|refresh_token|id_token|Token|token|code|code_verifier|code_challenge|device_code|user_code|session_id|sessionId|Cookie|cookie|authorization)[""']?\s*[:=]\s*[""']?)([^""'&\s,}\]]+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled), true),
        // Raw JWTs anywhere in the text
        (new Regex(@"\beyJ[A-Za-z0-9_\-]{8,}\.[A-Za-z0-9_\-]{2,}\.[A-Za-z0-9_\-]+", RegexOptions.Compiled), false),
        // Microsoft account access tokens ("EwA…") and refresh/authorization codes ("M.C…" / "M.R3_…")
        (new Regex(@"\bEwA[A-Za-z0-9+/=!$\-_.]{40,}", RegexOptions.Compiled), false),
        (new Regex(@"\bM\.(?:C\d|R3)[A-Za-z0-9_\-.!*$]{10,}", RegexOptions.Compiled), false),
    };

    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var result = text!;
        foreach (var (regex, keepsPrefix) in Patterns)
        {
            result = keepsPrefix
                ? regex.Replace(result, m => m.Groups[1].Value + Redacted)
                : regex.Replace(result, Redacted);
        }
        return result;
    }
}
