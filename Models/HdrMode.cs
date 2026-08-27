namespace LumiereMediaPlayer.Models;

/// <summary>
/// Controls how the app handles HDR content.
/// </summary>
public enum HdrMode
{
    /// <summary>Let Windows decide — pass HDR through when the display supports it.</summary>
    Auto,
    /// <summary>Force HDR output on. Best for OLED/Mini-LED displays.</summary>
    ForceOn,
    /// <summary>Always tone-map down to SDR regardless of display capability.</summary>
    ForceSdr
}

/// <summary>
/// Tone-mapping algorithm used when converting HDR → SDR or for
/// controlling peak brightness on HDR displays.
/// </summary>
public enum ToneMappingMode
{
    /// <summary>Display-Adaptive: Automatically calibrates tone-mapping, diffuse white, and gamut mapping specifically to the active screen hardware.</summary>
    DisplayAdaptive,
    /// <summary>BT.2408 reference tone mapping (ITU standard) with balanced midtones.</summary>
    Bt2408,
    /// <summary>ACES film-curve approximation — cinematic highlights.</summary>
    Aces,
    /// <summary>Reinhard global operator — smooth, preserves mid-tones.</summary>
    Reinhard,
    /// <summary>No tone mapping — clip at the display's white point.</summary>
    Clip
}

/// <summary>
/// Detected HDR format of the currently-playing content.
/// </summary>
public enum HdrContentFormat
{
    None,
    Hdr10,
    Hlg,
    DolbyVision
}

/// <summary>
/// Capability of the current display adapter.
/// </summary>
public enum DisplayHdrCapability
{
    /// <summary>Display does not support HDR.</summary>
    Sdr,
    /// <summary>Display supports WCG (Wide Color Gamut) but not HDR luminance.</summary>
    Wcg,
    /// <summary>Display supports HDR10 (PQ curve, BT.2020).</summary>
    Hdr10,
    /// <summary>Display supports Dolby Vision.</summary>
    DolbyVision
}
