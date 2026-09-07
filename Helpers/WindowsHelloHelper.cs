using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Security.Credentials.UI;
using WinRT;

namespace LumiereMediaPlayer.Helpers;

public static class WindowsHelloHelper
{
    [ComImport]
    [Guid("39E050C3-4E74-441A-8DC0-B81104F9A56E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIInspectable)]
    private interface IUserConsentVerifierInterop
    {
        void RequestVerificationForWindowAsync(
            IntPtr appWindow,
            [MarshalAs(UnmanagedType.HString)] string message,
            [In] ref Guid riid,
            out IntPtr asyncOperation);
    }

    private static readonly Guid IAsyncOperationGuid = new("FCDCF02C-E5D8-4478-915A-4E90B74B8F03"); // IAsyncOperation<UserConsentVerificationResult>

    public static async Task<UserConsentVerifierAvailability> CheckAvailabilityAsync()
    {
        try
        {
            return await UserConsentVerifier.CheckAvailabilityAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WindowsHello] CheckAvailability error: {ex.Message}");
            return UserConsentVerifierAvailability.DeviceNotPresent;
        }
    }

    public static async Task<bool> IsAvailableAsync()
    {
        var availability = await CheckAvailabilityAsync();
        return availability == UserConsentVerifierAvailability.Available;
    }

    public static string GetAvailabilityDescription(UserConsentVerifierAvailability availability)
    {
        return availability switch
        {
            UserConsentVerifierAvailability.Available => "Windows Hello is ready (Face, Fingerprint, or PIN)",
            UserConsentVerifierAvailability.NotConfiguredForUser => "Windows Hello is not set up on this account",
            UserConsentVerifierAvailability.DeviceNotPresent => "No Windows Hello biometric or PIN device found",
            UserConsentVerifierAvailability.DisabledByPolicy => "Windows Hello is disabled by system policy",
            UserConsentVerifierAvailability.DeviceBusy => "Windows Hello device is currently busy",
            _ => "Windows Hello status unavailable"
        };
    }

    public static async Task<UserConsentVerificationResult> RequestVerificationAsync(IntPtr hwnd, string promptMessage = "Unlock Lumière Media Player")
    {
        try
        {
            if (hwnd != IntPtr.Zero)
            {
                try
                {
                    var interop = UserConsentVerifier.As<IUserConsentVerifierInterop>();
                    if (interop != null)
                    {
                        var riid = IAsyncOperationGuid;
                        interop.RequestVerificationForWindowAsync(hwnd, promptMessage, ref riid, out var asyncOpPtr);
                        if (asyncOpPtr != IntPtr.Zero)
                        {
                            var asyncOp = MarshalInterface<Windows.Foundation.IAsyncOperation<UserConsentVerificationResult>>.FromAbi(asyncOpPtr);
                            return await asyncOp;
                        }
                    }
                }
                catch (Exception interopEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[WindowsHello] Interop fallback: {interopEx.Message}");
                }
            }

            return await UserConsentVerifier.RequestVerificationAsync(promptMessage);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WindowsHello] RequestVerification error: {ex.Message}");
            return UserConsentVerificationResult.DeviceNotPresent;
        }
    }
}
