namespace NovaClient.Core.Util;

public static class EmailMasker
{
    /// <summary>Masks "oliver@outlook.com" as "o*****r@outlook.com" for display.</summary>
    public static string Mask(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return string.Empty;
        var at = email.IndexOf('@');
        if (at <= 0) return "***";
        var local = email[..at];
        var domain = email[at..];
        return local.Length switch
        {
            1 => "*" + domain,
            2 => local[0] + "*" + domain,
            _ => local[0] + new string('*', local.Length - 2) + local[^1] + domain
        };
    }

    public static bool LooksValid(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        email = email.Trim();
        var at = email.IndexOf('@');
        if (at <= 0 || at != email.LastIndexOf('@') || at == email.Length - 1) return false;
        var domain = email[(at + 1)..];
        return domain.Contains('.') && !domain.StartsWith('.') && !domain.EndsWith('.') && !email.Contains(' ');
    }
}
