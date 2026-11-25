using System.Runtime.InteropServices;

namespace ResetExplode;

/// <summary>Verifica assinaturas Authenticode usando o mecanismo de confiança do Windows.</summary>
internal static class AuthenticodeVerifier
{
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

    internal static int WinVerifyTrustFile(string path)
    {
        var file = new WinTrustFileInfo { FilePath = path };
        file.StructSize = (uint)Marshal.SizeOf<WinTrustFileInfo>();
        IntPtr filePtr = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        try
        {
            Marshal.StructureToPtr(file, filePtr, false);
            var data = new WinTrustData
            {
                UiChoice = 2,
                RevocationChecks = 0,
                UnionChoice = 1,
                FileInfo = filePtr,
                StateAction = 0,
                ProviderFlags = 0x10,
            };
            data.StructSize = (uint)Marshal.SizeOf<WinTrustData>();
            return WinVerifyTrust(new IntPtr(-1), new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE"), ref data);
        }
        finally
        {
            Marshal.DestroyStructure<WinTrustFileInfo>(filePtr);
            Marshal.FreeHGlobal(filePtr);
        }
    }
}
