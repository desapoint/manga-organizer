using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MangaOrganizer.Server;

internal sealed record PhysicalFile(ulong Device, ulong Index, uint Links);

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

    public static PhysicalFile? Identity(FileInfo file, long size, DateTime modifiedAt) =>
        Read(file, size, modifiedAt, handle =>
        {
            if (OperatingSystem.IsLinux())
            {
                return ReadLinuxStat(handle.DangerousGetHandle().ToInt32(), "", 0x1000, out var stat) && (stat.Mask & 4) != 0
                    ? new PhysicalFile(((ulong)stat.DeviceMajor << 32) | stat.DeviceMinor, stat.Inode, stat.LinkCount) : null;
            }
            if (OperatingSystem.IsWindows() && GetFileInformationByHandle(handle, out var info))
            {
                return new PhysicalFile(info.Volume, ((ulong)info.IndexHigh << 32) | info.IndexLow, info.LinkCount);
            }
            return null;
        });

    private static readonly Lazy<string?> LinuxBootId = new(() =>
    {
        try
        {
            return Guid.TryParse(File.ReadAllText("/proc/sys/kernel/random/boot_id").Trim(), out var id)
                ? id.ToString("N") : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    });

    // Called only on a validated, open regular file. The caller validates it again before accepting the result.
    public static string? ScanStamp(SafeFileHandle handle)
    {
        if (OperatingSystem.IsLinux())
        {
            if (LinuxBootId.Value is not { } boot) return null;
            var descriptor = handle.DangerousGetHandle().ToInt32();
            if (StatFileSystem(descriptor, out var fs) != 0 ||
                fs.Type is not (0xEF53 or 0x58465342 or 0x9123683E or 0x01021994) ||
                !ReadLinuxStat(descriptor, "", 0x1000, out var info) || info.Inode == 0)
                return null;
            return FormattableString.Invariant(
                $"linux/1/{boot}/{fs.Id}/{info.DeviceMajor}/{info.DeviceMinor}/{info.Inode}/{info.Size}/{info.ModifiedSeconds}/{info.ModifiedNanoseconds}/{info.ChangeSeconds}/{info.ChangeNanoseconds}");
        }
        if (!OperatingSystem.IsWindows() || !GetFileInformationByHandle(handle, out var windows) ||
            !GetFileInformationByHandleEx(handle, 0, out var basic, (uint)Marshal.SizeOf<WindowsBasicInformation>()) || basic.ChangeTime <= 0)
            return null;

        // Remote shares cannot return a local volume GUID. Restrict reuse to local NTFS.
        var volumePath = new System.Text.StringBuilder(512);
        var length = GetFinalPathNameByHandle(handle, volumePath, (uint)volumePath.Capacity, 0x9);
        if (length == 0) return null;
        // A short buffer still gets the required path length. Use a long-path buffer for such files.
        if (length >= volumePath.Capacity)
        {
            volumePath = new System.Text.StringBuilder((int)length);
            length = GetFinalPathNameByHandle(handle, volumePath, (uint)volumePath.Capacity, 0x9);
            if (length == 0 || length >= volumePath.Capacity) return null;
        }
        var path = volumePath.ToString();
        var volumeEnd = path.IndexOf('}');
        if (volumeEnd < 0 || !path.StartsWith(@"\\?\Volume{", StringComparison.OrdinalIgnoreCase)) return null;
        var format = new System.Text.StringBuilder(16);
        if (!GetVolumeInformationByHandle(handle, IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, format, (uint)format.Capacity)
            || !format.ToString().Equals("NTFS", StringComparison.OrdinalIgnoreCase)) return null;
        return FormattableString.Invariant(
            $"windows/1/{path[..(volumeEnd + 1)]}/{windows.IndexHigh}/{windows.IndexLow}/{windows.SizeHigh}/{windows.SizeLow}/{basic.CreationTime}/{basic.LastWriteTime}/{basic.ChangeTime}");
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
        const uint requested = 0x3C5; // Type, modification time, change time, inode and size.
        return StatLinux(descriptor, path, flags, requested, out stat) == 0 &&
            (stat.Mask & requested) == requested && (stat.Mode & 0xF000) == 0x8000;
    }

    // Linux statx uses this layout on both supported architectures.
    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct LinuxFileStat
    {
        [FieldOffset(0)] public uint Mask;
        [FieldOffset(16)] public uint LinkCount;
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

    [StructLayout(LayoutKind.Explicit, Size = 120)]
    private struct LinuxFileSystem
    {
        [FieldOffset(0)] public long Type;
        [FieldOffset(56)] public long Id;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowsBasicInformation
    {
        public long CreationTime;
        public long LastAccessTime;
        public long LastWriteTime;
        public long ChangeTime;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowsFileInformation
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME AccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME WriteTime;
        public uint Volume;
        public uint SizeHigh;
        public uint SizeLow;
        public uint LinkCount;
        public uint IndexHigh;
        public uint IndexLow;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out WindowsFileInformation information);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int informationClass,
        out WindowsBasicInformation information, uint length);

    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, System.Text.StringBuilder path, uint length, uint flags);

    [DllImport("kernel32.dll", EntryPoint = "GetVolumeInformationByHandleW", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeInformationByHandle(SafeFileHandle handle, IntPtr name, uint nameLength,
        IntPtr serial, IntPtr componentLength, IntPtr flags, System.Text.StringBuilder format, uint formatLength);

    [DllImport("libc", EntryPoint = "fstatfs")]
    private static extern int StatFileSystem(int descriptor, out LinuxFileSystem info);

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
