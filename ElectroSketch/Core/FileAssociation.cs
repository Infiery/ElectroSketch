using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ElectroSketch.Core;

public static class FileAssociation
{
    private const string ProgId = "ElectroSketch.Topology";
    private const uint SiidNetworkConnect = 85;
    private const uint ShcneAssocChanged = 0x08000000;

    public static void Register()
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable)) return;

        using (var extension = Registry.CurrentUser.CreateSubKey(@"Software\Classes\.ets"))
        {
            extension?.SetValue("", ProgId);
            extension?.SetValue("Content Type", "application/x-electrosketch");
            extension?.SetValue("PerceivedType", "document");
        }

        using (var openWith = Registry.CurrentUser.CreateSubKey(@"Software\Classes\.ets\OpenWithProgids"))
            openWith?.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);

        using (var type = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}"))
        {
            type?.SetValue("", "ElectroSketch topology");
            type?.SetValue("FriendlyTypeName", "ElectroSketch topology");
        }

        using (var icon = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}\DefaultIcon"))
            icon?.SetValue("", GetNativeWindowsIcon(), RegistryValueKind.ExpandString);

        using (var command = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}\shell\open\command"))
            command?.SetValue("", $"\"{executable}\" \"%1\"");

        SHChangeNotify(ShcneAssocChanged, 0, IntPtr.Zero, IntPtr.Zero);
    }

    private static string GetNativeWindowsIcon()
    {
        var info = new ShStockIconInfo { cbSize = (uint)Marshal.SizeOf<ShStockIconInfo>() };
        var result = SHGetStockIconInfo(SiidNetworkConnect, 0, ref info);
        if (result == 0 && !string.IsNullOrWhiteSpace(info.szPath))
            return $"\"{info.szPath}\",{info.iIcon}";
        return @"%SystemRoot%\System32\imageres.dll,-102";
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShStockIconInfo
    {
        public uint cbSize;
        public IntPtr hIcon;
        public int iSysImageIndex;
        public int iIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szPath;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetStockIconInfo(uint siid, uint uFlags, ref ShStockIconInfo psii);

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
}
