using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace MangaOrganizer.Server;

internal static class LibraryFileHasher
{
    private const FileAttributes NonRegularAttributes = FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Device;

    public static byte[]? Read(FileInfo file, long size, DateTime modifiedAt, byte[] buffer, CancellationToken cancellationToken)
    {
        try
        {
            LibraryScanner.RequireRoot(file.DirectoryName!);
            byte[]? digest = null;
            if (OperatingSystem.IsLinux())
            {
                digest = ReadLinux(file.FullName, size, buffer, cancellationToken);
            }
            else if (OperatingSystem.IsWindows())
            {
                digest = ReadWindows(file.FullName, size, modifiedAt, buffer, cancellationToken);
            }
            LibraryScanner.RequireRoot(file.DirectoryName!);
            file.Refresh();
            return file.Exists && (file.Attributes & NonRegularAttributes) == 0 &&
                file.Length == size && file.LastWriteTimeUtc == modifiedAt ? digest : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static byte[]? ReadWindows(string path, long size, DateTime modifiedAt, byte[] buffer, CancellationToken cancellationToken)
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

        // Open the reparse point itself and deny concurrent write or delete access while hashing.
        using var handle = CreateFile(path, genericRead, shareRead, IntPtr.Zero, openExisting,
            openReparsePoint | sequentialScan, IntPtr.Zero);
        if (handle.IsInvalid || GetFileType(handle) != diskFile || (File.GetAttributes(handle) & NonRegularAttributes) != 0 ||
            File.GetLastWriteTimeUtc(handle) != modifiedAt)
        {
            return null;
        }

        return ReadDigest(handle, size, buffer, cancellationToken);
    }

    private static byte[]? ReadLinux(string path, long size, byte[] buffer, CancellationToken cancellationToken)
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

        var digest = ReadDigest(handle, size, buffer, cancellationToken);
        return ReadLinuxStat(currentDirectory, path, noFollowLink, out var after) && before.Matches(after) ? digest : null;
    }

    private static byte[]? ReadDigest(SafeFileHandle handle, long size, byte[] buffer, CancellationToken cancellationToken)
    {
        // Use offset-based reads so the handle remains open for validation after hashing.
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long position = 0;
        while (position < size)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = RandomAccess.Read(handle, buffer.AsSpan(0, (int)Math.Min(buffer.Length, size - position)), position);
            if (read == 0)
            {
                return null;
            }

            hash.AppendData(buffer.AsSpan(0, read));
            position += read;
        }

        return RandomAccess.GetLength(handle) == size ? hash.GetHashAndReset() : null;
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
