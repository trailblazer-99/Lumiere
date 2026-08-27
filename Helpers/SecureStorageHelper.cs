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
            
            // Remove existing entry if present
            try
            {
                var existing = vault.Retrieve(ResourceName, key);
                if (existing != null)
                {
                    vault.Remove(existing);
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
            var cred = vault.Retrieve(ResourceName, key);
            return cred?.Password ?? string.Empty;
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
            var cred = vault.Retrieve(ResourceName, key);
            if (cred != null)
            {
                vault.Remove(cred);
            }
        }
        catch { }
    }
}
