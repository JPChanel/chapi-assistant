using Chapi.Domain.Interfaces;
using System.Runtime.InteropServices;
using System.Text;

namespace Chapi.Infrastructure.Services;

/// <summary>
/// Implementación de almacenamiento de credenciales usando Windows Credential Manager.
/// </summary>
public class WindowsCredentialStorageService : ICredentialStorageService
{
    private const string TARGET_PREFIX = "ChapiAssistant_";

    public Task SaveCredentialAsync(string service, string username, string token)
    {
        var credential = new CREDENTIAL
        {
            Type = CRED_TYPE.GENERIC,
            TargetName = TARGET_PREFIX + service,
            UserName = username,
            CredentialBlob = Marshal.StringToCoTaskMemUni(token),
            CredentialBlobSize = (uint)Encoding.Unicode.GetByteCount(token),
            Persist = CRED_PERSIST.LOCAL_MACHINE,
            AttributeCount = 0,
            Attributes = IntPtr.Zero,
            Comment = null,
            TargetAlias = null
        };

        bool result = CredWrite(ref credential, 0);
        
        if (credential.CredentialBlob != IntPtr.Zero)
            Marshal.ZeroFreeCoTaskMemUnicode(credential.CredentialBlob);

        if (!result)
        {
            throw new InvalidOperationException($"Failed to save credential: {Marshal.GetLastWin32Error()}");
        }

        return Task.CompletedTask;
    }

    public Task<(string username, string token)?> GetCredentialAsync(string service)
    {
        IntPtr credPtr;
        bool result = CredRead(TARGET_PREFIX + service, CRED_TYPE.GENERIC, 0, out credPtr);

        if (!result)
        {
            return Task.FromResult<(string, string)?>(null);
        }

        try
        {
            var credential = Marshal.PtrToStructure<CREDENTIAL>(credPtr);
            string username = credential.UserName;
            string token = Marshal.PtrToStringUni(credential.CredentialBlob,
                (int)credential.CredentialBlobSize / 2);

            return Task.FromResult<(string, string)?>((username, token));
        }
        finally
        {
            CredFree(credPtr);
        }
    }

    public Task DeleteCredentialAsync(string service)
    {
        CredDelete(TARGET_PREFIX + service, CRED_TYPE.GENERIC, 0);
        return Task.CompletedTask;
    }

    public async Task<bool> HasCredentialAsync(string service)
    {
        var cred = await GetCredentialAsync(service);
        return cred.HasValue;
    }

    #region Windows API

    [DllImport("Advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite([In] ref CREDENTIAL userCredential, [In] uint flags);

    [DllImport("Advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, CRED_TYPE type, int reservedFlag, out IntPtr credentialPtr);

    [DllImport("Advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode)]
    private static extern bool CredDelete(string target, CRED_TYPE type, int flags);

    [DllImport("Advapi32.dll", EntryPoint = "CredFree", SetLastError = true)]
    private static extern bool CredFree([In] IntPtr cred);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags;
        public CRED_TYPE Type;
        public string TargetName;
        public string Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public CRED_PERSIST Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string TargetAlias;
        public string UserName;
    }

    private enum CRED_TYPE : uint
    {
        GENERIC = 1,
        DOMAIN_PASSWORD = 2,
        DOMAIN_CERTIFICATE = 3,
        DOMAIN_VISIBLE_PASSWORD = 4,
        GENERIC_CERTIFICATE = 5,
        DOMAIN_EXTENDED = 6,
        MAXIMUM = 7,
        MAXIMUM_EX = 1007
    }

    private enum CRED_PERSIST : uint
    {
        SESSION = 1,
        LOCAL_MACHINE = 2,
        ENTERPRISE = 3
    }

    #endregion
}
