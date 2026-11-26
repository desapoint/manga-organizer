using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MangaOrganizer.Server;

internal static class RegularFile
{
    private const FileAttributes NonRegularAttributes = FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Device;

    public static T? Read<T>(FileInfo file, long size, DateTime modifiedAt, Func<SafeFileHandle, T?> read) where T : class
    {
        try
        {
            LibraryScanner.RequireRoot(file.DirectoryName!);
            T? result = null;
            if (OperatingSystem.IsLinux())
            {
                result = ReadLinux(file.FullName, size, read);
            }
            else if (OperatingSystem.IsWindows())
            {
                result = ReadWindows(file.FullName, modifiedAt, read);
            }
            LibraryScanner.RequireRoot(file.DirectoryName!);
            file.Refresh();
            return file.Exists && (file.Attributes & NonRegularAttributes) == 0 &&
                file.Length == size && file.LastWriteTimeUtc == modifiedAt ? result : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static T? ReadWindows<T>(string path, DateTime modifiedAt, Func<SafeFileHandle, T?> read) where T : class
    {
        const uint genericRead = 0x80000000;
        const uint shareRead = 1;
        const uint openExisting = 3;
        const uint openReparsePoint = 0x00200000;
        const uint sequentialScan = 0x08000000;
        const uint diskFile = 1;
        if (path.Length >= 260)
        {
            path = path.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + path[2..] : @"\\?\" + path;
        }

        // Open the reparse point itself and deny concurrent write or delete access while reading.
        using var handle = CreateFile(path, genericRead, shareRead, IntPtr.Zero, openExisting,
            openReparsePoint | sequentialScan, IntPtr.Zero);
        if (handle.IsInvalid || GetFileType(handle) != diskFile || (File.GetAttributes(handle) & NonRegularAttributes) != 0 ||
            File.GetLastWriteTimeUtc(handle) != modifiedAt)
        {
            return null;
        }

        return read(handle);
    }

    private static T? ReadLinux<T>(string path, long size, Func<SafeFileHandle, T?> read) where T : class
    {
        const int currentDirectory = -100;
        const int noFollowLink = 0x100;
        const int useDescriptor = 0x1000;
        const int nonBlocking = 0x800;
        const int closeOnExec = 0x80000;
        var noFollowOpen = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? 0x8000 : 0x20000;

        // Avoid opening special files. Nonblocking mode also protects against a FIFO replacing a file before open.
        if (!ReadLinuxStat(currentDirectory, path, noFollowLink, out var before) || before.Size != size)
        {
            return null;
        }

        var descriptor = OpenLinux(path, noFollowOpen | nonBlocking | closeOnExec);
        if (descriptor < 0)
        {
            return null;
        }

        using var handle = new SafeFileHandle((IntPtr)descriptor, ownsHandle: true);
        if (!ReadLinuxStat(descriptor, "", useDescriptor, out var opened) || !before.Matches(opened))
        {
            return null;
        }

        var result = read(handle);
        return ReadLinuxStat(currentDirectory, path, noFollowLink, out var after) && before.Matches(after) ? result : null;
    }

    private static bool ReadLinuxStat(int descriptor, string path, int flags, out LinuxFileStat stat)
    {
        const uint requested = 0x3C1; // Type, modification time, change time, inode and size.
        return StatLinux(descriptor, path, flags, requested, out stat) == 0 &&
            (stat.Mask & requested) == requested && (stat.Mode & 0xF000) == 0x8000;
    }

    // Linux statx uses this layout on both supported architectures.
    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct LinuxFileStat
    {
        [FieldOffset(0)] public uint Mask;
        [FieldOffset(28)] public ushort Mode;
        [FieldOffset(32)] public ulong Inode;
        [FieldOffset(40)] public long Size;
        [FieldOffset(96)] public long ChangeSeconds;
        [FieldOffset(104)] public uint ChangeNanoseconds;
        [FieldOffset(112)] public long ModifiedSeconds;
        [FieldOffset(120)] public uint ModifiedNanoseconds;
        [FieldOffset(136)] public uint DeviceMajor;
        [FieldOffset(140)] public uint DeviceMinor;

        public readonly bool Matches(LinuxFileStat other) => Inode == other.Inode && Size == other.Size &&
            DeviceMajor == other.DeviceMajor && DeviceMinor == other.DeviceMinor &&
            ChangeSeconds == other.ChangeSeconds && ChangeNanoseconds == other.ChangeNanoseconds &&
            ModifiedSeconds == other.ModifiedSeconds && ModifiedNanoseconds == other.ModifiedNanoseconds;
    }

    [DllImport("libc", EntryPoint = "open")]
    private static extern int OpenLinux([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

    [DllImport("libc", EntryPoint = "statx")]
    private static extern int StatLinux(int descriptor, [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        int flags, uint mask, out LinuxFileStat stat);

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint shareMode, IntPtr security,
        uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern uint GetFileType(SafeFileHandle handle);
}
