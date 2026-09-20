using System;
using Microsoft.UI.Xaml;
using Microsoft.Graphics.Display;
using LumiereMediaPlayer.Services.Display;

namespace LumiereMediaPlayer.Services;

/// <summary>
/// Single authoritative source for display advanced-color state and screen hardware characterization.
/// </summary>
public interface IDisplayManager
{
    event EventHandler? AdvancedColorInfoChanged;

    bool IsHdrActive { get; }
    bool CanStreamHdr { get; }
    bool IsHdrStreamingCapableOnly { get; }
    float SdrWhiteLevelInNits { get; }
    float MaxLuminanceInNits { get; }
    float MinLuminanceInNits { get; }
    float MaxFullFrameLuminanceInNits { get; }
    bool SupportsHdr10 { get; }
    bool SupportsWcg { get; }
    DisplayProfileKind ActiveDisplayProfile { get; }
    DisplayAdvancedColorKind CurrentColorKind { get; }
    string DisplayProfileSummary { get; }

    void InitializeForWindow(Window window);
    void UpdateColorInfo();
}
