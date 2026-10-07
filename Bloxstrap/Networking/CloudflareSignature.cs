using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace Bloxstrap.Networking
{
    internal static class CloudflareSignature
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct TrustFile
        {
            public uint Size;
            [MarshalAs(UnmanagedType.LPWStr)] public string Path;
            public IntPtr FileHandle, KnownSubject;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct TrustData
        {
            public uint Size;
            public IntPtr PolicyData, SipData;
            public uint UiChoice, RevocationChecks, UnionChoice;
            public IntPtr File;
            public uint StateAction;
            public IntPtr StateData, Url;
            public uint ProviderFlags, UiContext;
            public IntPtr SignatureSettings;
        }
        [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        private static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);
        [DllImport("wintrust.dll", ExactSpelling = true)]
        private static extern IntPtr WTHelperProvDataFromStateData(IntPtr state);
        [DllImport("wintrust.dll", ExactSpelling = true)]
        private static extern IntPtr WTHelperGetProvSignerFromChain(IntPtr provider, uint signerIndex, [MarshalAs(UnmanagedType.Bool)] bool counterSigner, uint counterIndex);
        [DllImport("wintrust.dll", ExactSpelling = true)]
        private static extern IntPtr WTHelperGetProvCertFromChain(IntPtr signer, uint certificateIndex);
        [StructLayout(LayoutKind.Sequential)]
        private struct ProviderCertificate { public uint Size; public IntPtr Context; }
        [StructLayout(LayoutKind.Sequential)]
        private struct CertificateContext { public uint Encoding; public IntPtr Encoded; public uint EncodedSize; public IntPtr Information, Store; }
        public static void Verify(string path)
        {
            var file = new TrustFile { Size = (uint)Marshal.SizeOf<TrustFile>(), Path = Path.GetFullPath(path) };
            IntPtr memory = Marshal.AllocHGlobal(Marshal.SizeOf<TrustFile>());
            try
            {
                Marshal.StructureToPtr(file, memory, false);
                var data = new TrustData { Size = (uint)Marshal.SizeOf<TrustData>(), UiChoice = 2, UnionChoice = 1,
                    File = memory, StateAction = 1, ProviderFlags = 0x80 }; // verify chain revocation excluding root
                var action = new Guid("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");
                try
                {
                    int status = WinVerifyTrust(new IntPtr(-1), ref action, ref data);
                    if (status != 0) throw new InvalidDataException($"Cloudflare package failed Windows signature verification (0x{status:X8}). Installation/control was stopped.");
                    IntPtr provider = WTHelperProvDataFromStateData(data.StateData);
                    IntPtr signer = provider == IntPtr.Zero ? IntPtr.Zero : WTHelperGetProvSignerFromChain(provider, 0, false, 0);
                    IntPtr trustedCertificate = signer == IntPtr.Zero ? IntPtr.Zero : WTHelperGetProvCertFromChain(signer, 0);
                    if (trustedCertificate == IntPtr.Zero) throw new InvalidDataException("Windows did not return the verified package signer.");
                    var entry = Marshal.PtrToStructure<ProviderCertificate>(trustedCertificate);
                    var context = Marshal.PtrToStructure<CertificateContext>(entry.Context);
                    if (context.Encoded == IntPtr.Zero || context.EncodedSize is 0 or > 1_000_000) throw new InvalidDataException("Invalid signer certificate.");
                    var encoded = new byte[context.EncodedSize]; Marshal.Copy(context.Encoded, encoded, 0, encoded.Length);
                    using var certificate = X509CertificateLoader.LoadCertificate(encoded);
                    if (!Regex.IsMatch(certificate.Subject, "(?:^|,\\s*)O=\"?Cloudflare,? Inc\\.?\"?(?:,|$)"))
                        throw new InvalidDataException("The verified package publisher is not Cloudflare, Inc. Installation/control was stopped.");
                }
                finally { data.StateAction = 2; WinVerifyTrust(new IntPtr(-1), ref action, ref data); }
            }
            finally { Marshal.DestroyStructure<TrustFile>(memory); Marshal.FreeHGlobal(memory); }
        }
    }
}
