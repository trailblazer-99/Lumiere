using System;
using System.Threading.Tasks;
using LumiereMediaPlayer.Models;

namespace LumiereMediaPlayer.Services;

/// <summary>
/// Interface for managing application settings.
/// </summary>
public interface ISettingsService
{
    AppSettings Current { get; }

    event EventHandler? SettingsChanged;

    void Load();
    void Save();
    void SetTheme(AppThemeOption theme);
    void AddLibraryFolder(string path);
    void RemoveLibraryFolder(string path);
    void ResetSettings();
    Task ResetPlaybackHistoryAndCacheAsync();
}
