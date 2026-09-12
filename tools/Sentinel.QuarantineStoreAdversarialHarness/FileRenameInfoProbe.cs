using Microsoft.Win32.SafeHandles;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

internal static class FileRenameInfoProbe
{
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint DeleteAccess = 0x00010000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint FileShareDelete = 0x00000004;
    private const uint OpenExisting = 3;
    private const uint FileAttributeNormal = 0x00000080;
    private const int FileRenameInfoClass = 3;

    [ModuleInitializer]
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows()) return;
        Console.WriteLine("=== FILE_RENAME_INFO diagnostic probe ===");
        RunCase("byte-count", lengthAsCharacters: false);
        RunCase("character-count", lengthAsCharacters: true);
        Console.WriteLine("=== FILE_RENAME_INFO diagnostic probe complete ===");
    }

    private static void RunCase(string name, bool lengthAsCharacters)
    {
        string root = Path.Combine(Path.GetTempPath(), "SentinelRenameProbe", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string source = Path.Combine(root, "source.txt");
        string destination = Path.Combine(root, "destination.txt");
        File.WriteAllText(source, "sentinel-rename-probe");

        try
        {
            using SafeFileHandle handle = CreateFileW(
                source,
                GenericRead | GenericWrite | DeleteAccess,
                FileShareRead,
                IntPtr.Zero,
                OpenExisting,
                FileAttributeNormal,
                IntPtr.Zero);
            if (handle.IsInvalid)
            {
                Console.WriteLine($"rename-probe {name}: open failed Win32={Marshal.GetLastWin32Error()}");
                return;
            }

            byte[] nameBytes = Encoding.Unicode.GetBytes(destination);
            int rootOffset = IntPtr.Size == 8 ? 8 : 4;
            int lengthOffset = rootOffset + IntPtr.Size;
            int nameOffset = lengthOffset + sizeof(int);
            int bufferLength = nameOffset + nameBytes.Length + sizeof(char);
            IntPtr buffer = Marshal.AllocHGlobal(bufferLength);
            bool renameResult;
            int renameError;
            try
            {
                byte[] zeros = new byte[bufferLength];
                Marshal.Copy(zeros, 0, buffer, zeros.Length);
                Marshal.WriteByte(buffer, 0, 0);
                Marshal.WriteIntPtr(buffer, rootOffset, IntPtr.Zero);
                Marshal.WriteInt32(buffer, lengthOffset, lengthAsCharacters ? destination.Length : nameBytes.Length);
                Marshal.Copy(nameBytes, 0, IntPtr.Add(buffer, nameOffset), nameBytes.Length);
                renameResult = SetFileInformationByHandle(handle, FileRenameInfoClass, buffer, (uint)bufferLength);
                renameError = Marshal.GetLastWin32Error();
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }

            bool sourceWhileOpen = File.Exists(source);
            bool destinationWhileOpen = File.Exists(destination);
            Console.WriteLine($"rename-probe {name}: result={renameResult}; Win32={renameError}; sourceWhileOpen={sourceWhileOpen}; destinationWhileOpen={destinationWhileOpen}");

            using (SafeFileHandle reopened = CreateFileW(
                destination,
                GenericRead,
                FileShareRead | FileShareWrite | FileShareDelete,
                IntPtr.Zero,
                OpenExisting,
                FileAttributeNormal,
                IntPtr.Zero))
            {
                int nativeError = reopened.IsInvalid ? Marshal.GetLastWin32Error() : 0;
                Console.WriteLine($"rename-probe {name}: native-reopen valid={!reopened.IsInvalid}; Win32={nativeError}");
            }

            try
            {
                using FileStream stream = new(destination, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                Console.WriteLine($"rename-probe {name}: managed-reopen success length={stream.Length}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"rename-probe {name}: managed-reopen failed {ex.GetType().Name}: {ex.Message}");
            }

            handle.Dispose();
            Console.WriteLine($"rename-probe {name}: afterClose source={File.Exists(source)}; destination={File.Exists(destination)}; files=[{string.Join(",", Directory.EnumerateFiles(root).Select(Path.GetFileName))}]");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(
        SafeFileHandle hFile,
        int fileInformationClass,
        IntPtr lpFileInformation,
        uint dwBufferSize);
}
