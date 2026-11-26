using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using GeolabTITools.Application.Features.Windows.Contracts;
using GeolabTITools.Application.Features.Windows.DTOs;

namespace GeolabTITools.Infrastructure.Windows.Security;

/// <summary>Authenticode verification through Windows trust policy, preserving the application's existing WinTrust flags.</summary>
public sealed class WindowsAuthenticodeSignatureVerifier : IAuthenticodeSignatureVerifier
{
    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
    private const uint WtdRevocationCheckNone = 0x10;

    public AuthenticodeVerificationResult Verify(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        int status = VerifyTrust(filePath);
        string? signer = null;
        string? certificateError = null;
        bool hasSignature = false;
        try
        {
#pragma warning disable SYSLIB0057
            using X509Certificate certificate = X509Certificate.CreateFromSignedFile(filePath);
            using var certificate2 = new X509Certificate2(certificate);
#pragma warning restore SYSLIB0057
            hasSignature = true;
            signer = certificate2.Subject;
        }
        catch (Exception ex) { certificateError = ex.Message; }

        bool trusted = status == 0;
        string? failure = trusted ? certificateError : $"WinVerifyTrust returned 0x{status:X8}.";
        return new AuthenticodeVerificationResult(hasSignature, trusted, signer, status, failure);
    }

    private static int VerifyTrust(string filePath)
    {
        var file = new WinTrustFileInfo { FilePath = filePath, StructSize = (uint)Marshal.SizeOf<WinTrustFileInfo>() };
        IntPtr filePointer = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        try
        {
            Marshal.StructureToPtr(file, filePointer, false);
            var data = new WinTrustData
            {
                StructSize = (uint)Marshal.SizeOf<WinTrustData>(),
                UiChoice = 2,
                RevocationChecks = 0,
                UnionChoice = 1,
                FileInfo = filePointer,
                StateAction = 0,
                // Preserved from the legacy verifier; changing revocation policy is a separate security decision.
                ProviderFlags = WtdRevocationCheckNone,
            };
            return WinVerifyTrust(new IntPtr(-1), GenericVerifyV2, ref data);
        }
        finally
        {
            Marshal.DestroyStructure<WinTrustFileInfo>(filePointer);
            Marshal.FreeHGlobal(filePointer);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint StructSize;
        [MarshalAs(UnmanagedType.LPWStr)] public string FilePath;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustData
    {
        public uint StructSize;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr FileInfo;
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr UrlReference;
        public uint ProviderFlags;
        public uint UiContext;
        public IntPtr SignatureSettings;
    }

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false)]
    private static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid actionId, ref WinTrustData data);
}
