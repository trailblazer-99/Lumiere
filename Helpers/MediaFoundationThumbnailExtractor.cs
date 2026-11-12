using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace LumiereMediaPlayer.Helpers;

/// <summary>
/// High-efficiency Media Foundation thumbnail extractor (Option A: Hardware-downscaled IMFSourceReader).
/// Bypasses the heavyweight MediaComposition pipeline and decodes video frames directly into raw RGB32
/// in system memory, downsampling directly to a compact 160x90 BGRA byte array (57.6 KB).
/// </summary>
public static class MediaFoundationThumbnailExtractor
{
    private static int _isInitialized = 0;
    private static readonly object _readerLock = new();
    private static IMFSourceReader? _activeReader;
    private static string? _activeFilePath;
    private static uint _videoStreamIndex = MF_SOURCE_READER_FIRST_VIDEO_STREAM;
    private static uint _nativeWidth;
    private static uint _nativeHeight;
    private static Timer? _idleTimer;

    #region Media Foundation COM Interop Definitions

    [ComImport]
    [Guid("2cd2d921-c447-44a7-a13c-4adabfc247e3")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFAttributes
    {
        [PreserveSig] int GetItem(ref Guid guidKey, IntPtr pValue);
        [PreserveSig] int GetItemType(ref Guid guidKey, out int pType);
        [PreserveSig] int CompareItem(ref Guid guidKey, IntPtr Value, [MarshalAs(UnmanagedType.Bool)] out bool pbResult);
        [PreserveSig] int Compare(IMFAttributes pTheirs, int MatchType, [MarshalAs(UnmanagedType.Bool)] out bool pbResult);
        [PreserveSig] int GetUINT32(ref Guid guidKey, out uint punValue);
        [PreserveSig] int GetUINT64(ref Guid guidKey, out ulong punValue);
        [PreserveSig] int GetDouble(ref Guid guidKey, out double pfValue);
        [PreserveSig] int GetGUID(ref Guid guidKey, out Guid pguidValue);
        [PreserveSig] int GetStringLength(ref Guid guidKey, out uint pcchLength);
        [PreserveSig] int GetString(ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pwszValue, uint cchBufSize, out uint pcchLength);
        [PreserveSig] int GetAllocatedString(ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] out string ppwszValue, out uint pcchLength);
        [PreserveSig] int GetBlobSize(ref Guid guidKey, out uint pcbBlobSize);
        [PreserveSig] int GetBlob(ref Guid guidKey, [Out] byte[] pBuf, uint cbBufSize, out uint pcbBlobSize);
        [PreserveSig] int GetAllocatedBlob(ref Guid guidKey, out IntPtr ppBuf, out uint pcbSize);
        [PreserveSig] int GetUnknown(ref Guid guidKey, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int SetItem(ref Guid guidKey, IntPtr Value);
        [PreserveSig] int DeleteItem(ref Guid guidKey);
        [PreserveSig] int DeleteAllItems();
        [PreserveSig] int SetUINT32(ref Guid guidKey, uint unValue);
        [PreserveSig] int SetUINT64(ref Guid guidKey, ulong unValue);
        [PreserveSig] int SetDouble(ref Guid guidKey, double fValue);
        [PreserveSig] int SetGUID(ref Guid guidKey, ref Guid guidValue);
        [PreserveSig] int SetString(ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string wszValue);
        [PreserveSig] int SetBlob(ref Guid guidKey, [In] byte[] pBuf, uint cbBufSize);
        [PreserveSig] int SetUnknown(ref Guid guidKey, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        [PreserveSig] int LockStore();
        [PreserveSig] int UnlockStore();
        [PreserveSig] int GetCount(out uint pcItems);
        [PreserveSig] int GetItemByIndex(uint unIndex, out Guid pguidKey, IntPtr pValue);
        [PreserveSig] int CopyAllItems(IMFAttributes pDest);
    }

    [ComImport]
    [Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFMediaType
    {
        // 30 methods of IMFAttributes required for .NET COM vtable matching
        [PreserveSig] int GetItem(ref Guid guidKey, IntPtr pValue);
        [PreserveSig] int GetItemType(ref Guid guidKey, out int pType);
        [PreserveSig] int CompareItem(ref Guid guidKey, IntPtr Value, [MarshalAs(UnmanagedType.Bool)] out bool pbResult);
        [PreserveSig] int Compare(IMFAttributes pTheirs, int MatchType, [MarshalAs(UnmanagedType.Bool)] out bool pbResult);
        [PreserveSig] int GetUINT32(ref Guid guidKey, out uint punValue);
        [PreserveSig] int GetUINT64(ref Guid guidKey, out ulong punValue);
        [PreserveSig] int GetDouble(ref Guid guidKey, out double pfValue);
        [PreserveSig] int GetGUID(ref Guid guidKey, out Guid pguidValue);
        [PreserveSig] int GetStringLength(ref Guid guidKey, out uint pcchLength);
        [PreserveSig] int GetString(ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pwszValue, uint cchBufSize, out uint pcchLength);
        [PreserveSig] int GetAllocatedString(ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] out string ppwszValue, out uint pcchLength);
        [PreserveSig] int GetBlobSize(ref Guid guidKey, out uint pcbBlobSize);
        [PreserveSig] int GetBlob(ref Guid guidKey, [Out] byte[] pBuf, uint cbBufSize, out uint pcbBlobSize);
        [PreserveSig] int GetAllocatedBlob(ref Guid guidKey, out IntPtr ppBuf, out uint pcbSize);
        [PreserveSig] int GetUnknown(ref Guid guidKey, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int SetItem(ref Guid guidKey, IntPtr Value);
        [PreserveSig] int DeleteItem(ref Guid guidKey);
        [PreserveSig] int DeleteAllItems();
        [PreserveSig] int SetUINT32(ref Guid guidKey, uint unValue);
        [PreserveSig] int SetUINT64(ref Guid guidKey, ulong unValue);
        [PreserveSig] int SetDouble(ref Guid guidKey, double fValue);
        [PreserveSig] int SetGUID(ref Guid guidKey, ref Guid guidValue);
        [PreserveSig] int SetString(ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string wszValue);
        [PreserveSig] int SetBlob(ref Guid guidKey, [In] byte[] pBuf, uint cbBufSize);
        [PreserveSig] int SetUnknown(ref Guid guidKey, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        [PreserveSig] int LockStore();
        [PreserveSig] int UnlockStore();
        [PreserveSig] int GetCount(out uint pcItems);
        [PreserveSig] int GetItemByIndex(uint unIndex, out Guid pguidKey, IntPtr pValue);
        [PreserveSig] int CopyAllItems(IMFAttributes pDest);

        // IMFMediaType methods
        [PreserveSig] int GetMajorType(out Guid pguidMajorType);
        [PreserveSig] int IsCompressedFormat([MarshalAs(UnmanagedType.Bool)] out bool pfCompressed);
        [PreserveSig] int IsEqual(IMFMediaType pIMediaType, out uint pdwFlags);
        [PreserveSig] int GetRepresentation(Guid guidRepresentation, out IntPtr ppvRepresentation);
        [PreserveSig] int FreeRepresentation(Guid guidRepresentation, IntPtr pvRepresentation);
    }

    [ComImport]
    [Guid("045FA593-8799-42b8-BC8D-8968C6453507")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFMediaBuffer
    {
        [PreserveSig] int Lock(out IntPtr ppbBuffer, out uint pcbMaxLength, out uint pcbCurrentLength);
        [PreserveSig] int Unlock();
        [PreserveSig] int GetCurrentLength(out uint pcbCurrentLength);
        [PreserveSig] int SetCurrentLength(uint cbCurrentLength);
        [PreserveSig] int GetMaxLength(out uint pcbMaxLength);
    }

    [ComImport]
    [Guid("7DC9D5F9-9ED9-44ec-9BBF-0600BB589FBB")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMF2DBuffer
    {
        [PreserveSig] int Lock2D(out IntPtr pbScanline0, out int plPitch);
        [PreserveSig] int Unlock2D();
        [PreserveSig] int IsContiguousFormat([MarshalAs(UnmanagedType.Bool)] out bool pfIsContiguous);
        [PreserveSig] int GetContiguousLength(out uint pdwLength);
        [PreserveSig] int ContiguousCopyTo(IntPtr pbDestBuffer, uint cbDestBuffer);
        [PreserveSig] int ContiguousCopyFrom(IntPtr pbSrcBuffer, uint cbSrcBuffer);
    }

    [ComImport]
    [Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFSample
    {
        // 30 methods of IMFAttributes required for .NET COM vtable matching
        [PreserveSig] int GetItem(ref Guid guidKey, IntPtr pValue);
        [PreserveSig] int GetItemType(ref Guid guidKey, out int pType);
        [PreserveSig] int CompareItem(ref Guid guidKey, IntPtr Value, [MarshalAs(UnmanagedType.Bool)] out bool pbResult);
        [PreserveSig] int Compare(IMFAttributes pTheirs, int MatchType, [MarshalAs(UnmanagedType.Bool)] out bool pbResult);
        [PreserveSig] int GetUINT32(ref Guid guidKey, out uint punValue);
        [PreserveSig] int GetUINT64(ref Guid guidKey, out ulong punValue);
        [PreserveSig] int GetDouble(ref Guid guidKey, out double pfValue);
        [PreserveSig] int GetGUID(ref Guid guidKey, out Guid pguidValue);
        [PreserveSig] int GetStringLength(ref Guid guidKey, out uint pcchLength);
        [PreserveSig] int GetString(ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pwszValue, uint cchBufSize, out uint pcchLength);
        [PreserveSig] int GetAllocatedString(ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] out string ppwszValue, out uint pcchLength);
        [PreserveSig] int GetBlobSize(ref Guid guidKey, out uint pcbBlobSize);
        [PreserveSig] int GetBlob(ref Guid guidKey, [Out] byte[] pBuf, uint cbBufSize, out uint pcbBlobSize);
        [PreserveSig] int GetAllocatedBlob(ref Guid guidKey, out IntPtr ppBuf, out uint pcbSize);
        [PreserveSig] int GetUnknown(ref Guid guidKey, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int SetItem(ref Guid guidKey, IntPtr Value);
        [PreserveSig] int DeleteItem(ref Guid guidKey);
        [PreserveSig] int DeleteAllItems();
        [PreserveSig] int SetUINT32(ref Guid guidKey, uint unValue);
        [PreserveSig] int SetUINT64(ref Guid guidKey, ulong unValue);
        [PreserveSig] int SetDouble(ref Guid guidKey, double fValue);
        [PreserveSig] int SetGUID(ref Guid guidKey, ref Guid guidValue);
        [PreserveSig] int SetString(ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string wszValue);
        [PreserveSig] int SetBlob(ref Guid guidKey, [In] byte[] pBuf, uint cbBufSize);
        [PreserveSig] int SetUnknown(ref Guid guidKey, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        [PreserveSig] int LockStore();
        [PreserveSig] int UnlockStore();
        [PreserveSig] int GetCount(out uint pcItems);
        [PreserveSig] int GetItemByIndex(uint unIndex, out Guid pguidKey, IntPtr pValue);
        [PreserveSig] int CopyAllItems(IMFAttributes pDest);

        // IMFSample methods
        [PreserveSig] int GetSampleFlags(out uint pdwSampleFlags);
        [PreserveSig] int SetSampleFlags(uint dwSampleFlags);
        [PreserveSig] int GetSampleTime(out long phnsSampleTime);
        [PreserveSig] int SetSampleTime(long hnsSampleTime);
        [PreserveSig] int GetSampleDuration(out long phnsSampleDuration);
        [PreserveSig] int SetSampleDuration(long hnsSampleDuration);
        [PreserveSig] int GetBufferCount(out uint pdwBufferCount);
        [PreserveSig] int GetBufferByIndex(uint dwIndex, out IMFMediaBuffer ppBuffer);
        [PreserveSig] int ConvertToContiguousBuffer(out IMFMediaBuffer ppBuffer);
        [PreserveSig] int AddBuffer(IMFMediaBuffer pBuffer);
        [PreserveSig] int RemoveBufferByIndex(uint dwIndex);
        [PreserveSig] int RemoveAllBuffers();
        [PreserveSig] int GetTotalLength(out uint pcbTotalLength);
        [PreserveSig] int CopyToBuffer(IMFMediaBuffer pBuffer);
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    public struct PropVariant
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public long hVal;
    }

    [ComImport]
    [Guid("70ae66f2-c809-4e4f-8915-bdcb406b7993")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFSourceReader
    {
        [PreserveSig] int GetStreamSelection(uint dwStreamIndex, [MarshalAs(UnmanagedType.Bool)] out bool pfSelected);
        [PreserveSig] int SetStreamSelection(uint dwStreamIndex, [MarshalAs(UnmanagedType.Bool)] bool fSelected);
        [PreserveSig] int GetNativeMediaType(uint dwStreamIndex, uint dwMediaTypeIndex, out IMFMediaType ppMediaType);
        [PreserveSig] int GetCurrentMediaType(uint dwStreamIndex, out IMFMediaType ppMediaType);
        [PreserveSig] int SetCurrentMediaType(uint dwStreamIndex, IntPtr pdwReserved, IMFMediaType pMediaType);
        [PreserveSig] int SetCurrentPosition(ref Guid pguidTimeFormat, ref PropVariant pvarStartPosition);
        [PreserveSig] int ReadSample(uint dwStreamIndex, uint dwControlFlags, out uint pdwActualStreamIndex, out uint pdwStreamFlags, out long pllTimestamp, out IMFSample ppSample);
        [PreserveSig] int Flush(uint dwStreamIndex);
        [PreserveSig] int GetServiceForStream(uint dwStreamIndex, ref Guid guidService, ref Guid riid, out IntPtr ppvObject);
        [PreserveSig] int GetPresentationAttribute(uint dwStreamIndex, ref Guid guidAttribute, out PropVariant pvarAttribute);
    }

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFStartup(uint version, uint dwFlags);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFShutdown();

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFCreateAttributes(out IMFAttributes ppMFAttributes, uint cInitialSize);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFCreateMediaType(out IMFMediaType ppMFMediaType);

    [DllImport("mfreadwrite.dll", ExactSpelling = true)]
    private static extern int MFCreateSourceReaderFromURL([MarshalAs(UnmanagedType.LPWStr)] string pwszURL, IMFAttributes? pAttributes, out IMFSourceReader ppSourceReader);

    public static readonly Guid MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING = new("fb394f3d-ccf1-42ee-bbb3-f9b845d5681d");
    public static readonly Guid MF_SOURCE_READER_ENABLE_ADVANCED_VIDEO_PROCESSING = new("0f8134e4-4808-4940-b9f5-1523f60ab99d");
    public static readonly Guid MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS = new("a634a91c-822b-41b9-a494-4de4643612b0");
    public static readonly Guid MF_MT_MAJOR_TYPE = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    public static readonly Guid MFMediaType_Video = new("73646976-0000-0010-8000-00aa00389b71");
    public static readonly Guid MF_MT_SUBTYPE = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    public static readonly Guid MFVideoFormat_RGB32 = new("00000016-0000-0010-8000-00aa00389b71");
    public static readonly Guid MF_MT_FRAME_SIZE = new("1652c33d-d6b2-4012-b834-72030849a37d");

    public const uint MF_SOURCE_READER_FIRST_VIDEO_STREAM = 0xFFFFFFFC;
    public const uint MF_SOURCE_READER_ALL_STREAMS = 0xFFFFFFFE;
    public const uint MF_SOURCE_READERF_ENDOFSTREAM = 0x00000200;

    #endregion

    /// <summary>
    /// Ensures Media Foundation runtime is initialized for this process.
    /// </summary>
    public static void EnsureInitialized()
    {
        if (Interlocked.CompareExchange(ref _isInitialized, 1, 0) == 0)
        {
            try
            {
                int hr = MFStartup(0x00020070, 0);
                if (hr == 0)
                {
                    AppDomain.CurrentDomain.ProcessExit += (_, _) =>
                    {
                        try { CleanupActiveReader(); MFShutdown(); } catch { }
                    };
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MFStartup initialization exception: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Extracts a high-speed video frame at the specified timestamp in seconds,
    /// downsampling it directly into a compact 160x90 BGRA byte array (57,600 bytes).
    /// Returns null on failure to allow seamless fallback to MediaComposition.
    /// </summary>
    public static byte[]? ExtractThumbnailBgra(string filePath, double seconds, int targetWidth = 160, int targetHeight = 90)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return null;

        EnsureInitialized();

        lock (_readerLock)
        {
            ResetIdleTimer();

            try
            {
                // Re-use active source reader if seeking within the same video track
                if (_activeReader == null || !string.Equals(_activeFilePath, filePath, StringComparison.OrdinalIgnoreCase))
                {
                    CloseActiveReaderLocked();

                    int hrAttrs = MFCreateAttributes(out var attrs, 3);
                    if (hrAttrs != 0 || attrs == null) return null;

                    Guid kVideoProc = MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING;
                    attrs.SetUINT32(ref kVideoProc, 1);
                    Guid kAdvProc = MF_SOURCE_READER_ENABLE_ADVANCED_VIDEO_PROCESSING;
                    attrs.SetUINT32(ref kAdvProc, 1);
                    Guid kHw = MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS;
                    attrs.SetUINT32(ref kHw, 1); // Enable hardware transforms for fast GPU frame decoding

                    int hrReader = MFCreateSourceReaderFromURL(filePath, attrs, out var reader);
                    Marshal.ReleaseComObject(attrs);

                    if (hrReader != 0 || reader == null) return null;

                    // Configure output media type to RGB32 (BGRA)
                    int hrMt = MFCreateMediaType(out var mtRgb);
                    if (hrMt != 0 || mtRgb == null)
                    {
                        Marshal.ReleaseComObject(reader);
                        return null;
                    }

                    Guid kMajor = MF_MT_MAJOR_TYPE;
                    Guid valMajor = MFMediaType_Video;
                    mtRgb.SetGUID(ref kMajor, ref valMajor);

                    Guid kSub = MF_MT_SUBTYPE;
                    Guid valSub = MFVideoFormat_RGB32;
                    mtRgb.SetGUID(ref kSub, ref valSub);

                    int hrSet = reader.SetCurrentMediaType(MF_SOURCE_READER_FIRST_VIDEO_STREAM, IntPtr.Zero, mtRgb);
                    Marshal.ReleaseComObject(mtRgb);

                    if (hrSet != 0)
                    {
                        Marshal.ReleaseComObject(reader);
                        return null;
                    }

                    // Select only the video stream to avoid demuxing unused audio tracks
                    reader.SetStreamSelection(MF_SOURCE_READER_ALL_STREAMS, false);
                    reader.SetStreamSelection(MF_SOURCE_READER_FIRST_VIDEO_STREAM, true);

                    // Read native frame dimensions
                    uint w = 0, h = 0;
                    int hrCur = reader.GetCurrentMediaType(MF_SOURCE_READER_FIRST_VIDEO_STREAM, out var curMt);
                    if (hrCur == 0 && curMt != null)
                    {
                        Guid kSize = MF_MT_FRAME_SIZE;
                        if (curMt.GetUINT64(ref kSize, out ulong sizeVal) == 0)
                        {
                            w = (uint)(sizeVal >> 32);
                            h = (uint)(sizeVal & 0xFFFFFFFF);
                        }
                        Marshal.ReleaseComObject(curMt);
                    }

                    if (w == 0 || h == 0)
                    {
                        w = 1920;
                        h = 1080;
                    }

                    _activeReader = reader;
                    _activeFilePath = filePath;
                    _videoStreamIndex = MF_SOURCE_READER_FIRST_VIDEO_STREAM;
                    _nativeWidth = w;
                    _nativeHeight = h;
                }

                var activeReader = _activeReader;
                if (activeReader == null) return null;

                // Seek to the target timestamp in 100-nanosecond units
                Guid timeFormat = Guid.Empty;
                PropVariant pv = new PropVariant
                {
                    vt = 20, // VT_I8
                    hVal = (long)(Math.Max(0.0, seconds) * 10_000_000.0)
                };

                activeReader.SetCurrentPosition(ref timeFormat, ref pv);

                // Read sample (loop up to 15 times to skip preroll or format events)
                IMFSample? sample = null;
                uint flags = 0;

                for (int i = 0; i < 15; i++)
                {
                    int hrRead = activeReader.ReadSample(_videoStreamIndex, 0, out _, out flags, out _, out sample);
                    if (hrRead != 0 || (flags & MF_SOURCE_READERF_ENDOFSTREAM) != 0) break;
                    if (sample != null)
                    {
                        int hrCount = sample.GetBufferCount(out uint count);
                        if (hrCount == 0 && count > 0) break;
                        Marshal.ReleaseComObject(sample);
                        sample = null;
                    }
                }

                if (sample == null) return null;

                try
                {
                    int hrBuf = sample.ConvertToContiguousBuffer(out var buffer);
                    if (hrBuf != 0 || buffer == null) return null;

                    try
                    {
                        int hrLock = buffer.Lock(out IntPtr pData, out _, out uint curLen);
                        if (hrLock != 0 || pData == IntPtr.Zero || curLen == 0) return null;

                        try
                        {
                            byte[] result = new byte[targetWidth * targetHeight * 4];
                            uint srcW = _nativeWidth > 0 ? _nativeWidth : (uint)targetWidth;
                            uint srcH = _nativeHeight > 0 ? _nativeHeight : (uint)targetHeight;

                            unsafe
                            {
                                byte* pSrc = (byte*)pData.ToPointer();
                                fixed (byte* pDst = result)
                                {
                                    uint* pDst32 = (uint*)pDst;
                                    int srcStride = (int)(srcW * 4);
                                    for (int y = 0; y < targetHeight; y++)
                                    {
                                        int srcY = (int)((long)y * srcH / targetHeight);
                                        int dstRowOffset = y * targetWidth;

                                        for (int x = 0; x < targetWidth; x++)
                                        {
                                            int srcX = (int)((long)x * srcW / targetWidth);
                                            long byteOffset = (long)srcY * srcStride + ((long)srcX * 4);
                                            if (byteOffset >= 0 && byteOffset + 4 <= curLen)
                                            {
                                                uint pixel = *(uint*)(pSrc + byteOffset);
                                                // Force full opacity in alpha channel
                                                pDst32[dstRowOffset + x] = pixel | 0xFF000000;
                                            }
                                        }
                                    }
                                }
                            }

                            return result;
                        }
                        finally
                        {
                            buffer.Unlock();
                        }
                    }
                    finally
                    {
                        try { Marshal.ReleaseComObject(buffer); } catch { }
                    }
                }
                finally
                {
                    try { Marshal.ReleaseComObject(sample); } catch { }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MediaFoundationThumbnailExtractor.ExtractThumbnailBgra error: {ex.Message}");
                CloseActiveReaderLocked();
                return null;
            }
        }
    }

    /// <summary>
    /// Explicitly cleans up and releases any active Media Foundation source reader resources.
    /// </summary>
    public static void CleanupActiveReader()
    {
        lock (_readerLock)
        {
            CloseActiveReaderLocked();
        }
    }

    private static void CloseActiveReaderLocked()
    {
        try
        {
            _idleTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _idleTimer?.Dispose();
            _idleTimer = null;
        }
        catch { }

        // Let .NET RCW garbage collection manage COM reader release naturally to avoid invalidating wrappers across worker threads
        _activeReader = null;
        _activeFilePath = null;
    }

    private static void ResetIdleTimer()
    {
        try
        {
            if (_idleTimer == null)
            {
                _idleTimer = new Timer(_ => CleanupActiveReader(), null, TimeSpan.FromSeconds(6), Timeout.InfiniteTimeSpan);
            }
            else
            {
                _idleTimer.Change(TimeSpan.FromSeconds(6), Timeout.InfiniteTimeSpan);
            }
        }
        catch { }
    }
}
