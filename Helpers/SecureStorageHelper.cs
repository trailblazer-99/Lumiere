using System;
using Windows.Security.Credentials;

namespace LumiereMediaPlayer.Helpers;

/// <summary>
/// Provides hardware/OS-backed encrypted storage for sensitive secrets (API keys, tokens)
/// using Windows Credential Locker (PasswordVault). Keys are never stored in plain text.
/// </summary>
public static class SecureStorageHelper
{
    private const string ResourceName = "LumiereMediaPlayer_Vault";

    public static void SaveSecret(string key, string secret)
    {
        if (string.IsNullOrWhiteSpace(key)) return;

        try
        {
            var vault = new PasswordVault();

            // Remove existing entry if present (query vault list to avoid first-chance COMException when empty)
            try
            {
                foreach (var c in vault.RetrieveAll())
                {
                    if (string.Equals(c.Resource, ResourceName, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(c.UserName, key, StringComparison.OrdinalIgnoreCase))
                    {
                        vault.Remove(c);
                        break;
                    }
                }
            }
            catch { }

            // Save new secret if non-empty
            if (!string.IsNullOrEmpty(secret))
            {
                vault.Add(new PasswordCredential(ResourceName, key, secret));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SecureStorageHelper] SaveSecret failed: {ex.Message}");
        }
    }

    public static string GetSecret(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return string.Empty;

        try
        {
            var vault = new PasswordVault();
            System.Collections.Generic.IReadOnlyList<PasswordCredential> list;
            try
            {
                list = vault.RetrieveAll();
            }
            catch
            {
                return string.Empty;
            }

            foreach (var cred in list)
            {
                if (string.Equals(cred.Resource, ResourceName, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(cred.UserName, key, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        cred.RetrievePassword();
                        return cred.Password ?? string.Empty;
                    }
                    catch
                    {
                        return string.Empty;
                    }
                }
            }

            return string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    public static void DeleteSecret(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;

        try
        {
            var vault = new PasswordVault();
            foreach (var c in vault.RetrieveAll())
            {
                if (string.Equals(c.Resource, ResourceName, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(c.UserName, key, StringComparison.OrdinalIgnoreCase))
                {
                    vault.Remove(c);
                    break;
                }
            }
        }
        catch { }
    }
}
