using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace SingleFileMc;

/// <summary>
/// 双显卡 (核显 + 独显) 笔记本的强制独显逻辑。
///
/// Minecraft 走 OpenGL (WGL)，Windows/NVIDIA 按进程决定用哪块 GPU：SingleFileMc.exe 不在任何
/// NVIDIA 配置档案里时，默认落到核显。方案与 HMCL 的"强制独显"一致 —— 把当前 exe 写入
/// HKCU\Software\Microsoft\DirectX\UserGpuPreferences (Windows 图形设置"高性能"的真实存储位置)：
///
///     <exe 路径> = GpuPreference=2;
///
/// 该偏好只在进程启动时生效，所以本进程内写入对当前进程无效 —— 检测到混合显卡 (核显+独显)
/// 时自动重启自身一次 (带 --sfmc-gpu-relaunch)，重启后的进程即命中独显偏好。用注册表
/// 标记记录"本 exe 已重启过"，避免每次启动都重启。
///
/// 环境变量 MCPACK_NO_GPU_FORCE=1/true 可完全关闭本逻辑。
/// </summary>
[SupportedOSPlatform("windows")]
internal static class GpuPreference
{
    private const string UserGpuPrefsKey = @"Software\Microsoft\DirectX\UserGpuPreferences";
    private const string RelaunchMarkerKey = @"Software\SingleFileMc\GpuRelaunch";
    private const string RelaunchArg = "--sfmc-gpu-relaunch";

    // DXGI
    private static readonly Guid IID_IDXGIFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");
    private const uint DXGI_ERROR_NOT_FOUND = 0x887A0002;
    private const uint DXGI_ADAPTER_FLAG_SOFTWARE = 2;
    private const uint VendorNvidia = 0x10DE;
    private const uint VendorAmd = 0x1002;
    private const uint VendorAmd2 = 0x1638;
    private const long MinDiscreteVram = 512L * 1024 * 1024; // 512 MB

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DxgiAdapterDesc1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public IntPtr DedicatedVideoMemory;
        public IntPtr DedicatedSystemMemory;
        public IntPtr SharedSystemMemory;
        public long AdapterLuid;
        public uint Flags;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int FnEnumAdapters1(IntPtr factory, uint adapter, out IntPtr ppAdapter);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int FnGetDesc1(IntPtr adapter, out DxgiAdapterDesc1 pDesc);

    [DllImport("dxgi.dll")]
    private static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr ppFactory);

    /// <summary>启动入口：写入偏好并按需重启。必须在任何 hook/JVM 初始化之前调用。</summary>
    public static bool TryApplyAndMaybeRelaunch(string[] args)
    {
        if (DisabledByEnv()) { return false; }

        string exePath = Environment.ProcessPath ?? "";
        if (exePath.Length == 0) { return false; }

        // 被重启出的进程：偏好已就绪，直接继续。
        foreach (string a in args)
        {
            if (string.Equals(a, RelaunchArg, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("[gpu] relaunched instance (GpuPreference=2 already in registry)");
                return false;
            }
        }

        EnsureHighPerformance(exePath);

        if (AlreadyRelaunched(exePath))
        {
            Console.WriteLine("[gpu] high-performance GPU preference active (this exe was relaunched once)");
            return false;
        }

        if (TryHasHybridDiscreteGpu(out bool hasDiscrete) && !hasDiscrete)
        {
            Console.WriteLine("[gpu] no discrete GPU detected, skip relaunch");
            return false;
        }
        if (TryRelaunch(exePath))
        {
            MarkRelaunched(exePath);
            Console.WriteLine("[gpu] relaunching to enable discrete GPU ...");
            return true; // 调用方应直接退出 Main
        }
        return false;
    }

    private static bool DisabledByEnv()
    {
        string? v = Environment.GetEnvironmentVariable("MCPACK_NO_GPU_FORCE");
        return v is "1" or "true" or "TRUE";
    }

    /// <summary>把当前 exe (全路径 + 文件名) 写入 Windows 图形设置的"高性能"偏好。</summary>
    private static void EnsureHighPerformance(string exePath)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.CreateSubKey(UserGpuPrefsKey);
            if (key is null) { return; }
            key.SetValue(exePath, "GpuPreference=2;", RegistryValueKind.String);
            string name = Path.GetFileName(exePath);
            if (!string.IsNullOrEmpty(name))
            {
                key.SetValue(name, "GpuPreference=2;", RegistryValueKind.String);
            }
            Console.WriteLine($"[gpu] GpuPreference=2 (high performance) written for {exePath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[gpu] write GPU preference failed (ignored): {ex.Message}");
        }
    }

    private static bool AlreadyRelaunched(string exePath)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RelaunchMarkerKey);
            return key?.GetValue("LastRelaunchExe", "") is string s
                && string.Equals(s, exePath, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static void MarkRelaunched(string exePath)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.CreateSubKey(RelaunchMarkerKey);
            key?.SetValue("LastRelaunchExe", exePath, RegistryValueKind.String);
        }
        catch { /* marker 失败只会在下次启动时多一次重启, 无害 */ }
    }

    private static bool TryRelaunch(string exePath)
    {
        try
        {
            string? dir = Path.GetDirectoryName(exePath);
            var psi = new ProcessStartInfo(exePath)
            {
                Arguments = RelaunchArg,
                UseShellExecute = true,
                WorkingDirectory = string.IsNullOrEmpty(dir) ? "" : dir,
            };
            Process.Start(psi);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[gpu] relaunch failed (continue current run): {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// DXGI 枚举适配器：统计非软件适配器数量，并判断是否存在"独显" (NVIDIA / AMD,
    /// 专用显存 ≥ 512 MB)。仅当混合显卡 (≥2 块非软件适配器且存在独显) 时才需要重启。
    /// 任何失败都保守返回 false (不重启, 注册表仍已写入, 下次启动生效)。
    /// </summary>
    private static bool TryHasHybridDiscreteGpu(out bool hasDiscrete)
    {
        hasDiscrete = false;
        IntPtr factory = IntPtr.Zero;
        try
        {
            Guid iid = IID_IDXGIFactory1;
            int hr = CreateDXGIFactory1(ref iid, out factory);
            if (hr != 0 || factory == IntPtr.Zero) { return false; }

            IntPtr factoryVtbl = Marshal.ReadIntPtr(factory);
            var enumAdapters1 = Marshal.GetDelegateForFunctionPointer<FnEnumAdapters1>(
                Marshal.ReadIntPtr(factoryVtbl, 12 * IntPtr.Size)); // IDXGIFactory1::EnumAdapters1

            int nonSoftware = 0;
            for (uint i = 0; ; i++)
            {
                IntPtr adapter = IntPtr.Zero;
            int r = enumAdapters1(factory, i, out adapter);
                if ((uint)r == DXGI_ERROR_NOT_FOUND) { break; }
                if (r != 0 || adapter == IntPtr.Zero) { continue; }
                try
                {
                    IntPtr adapterVtbl = Marshal.ReadIntPtr(adapter);
                    var getDesc1 = Marshal.GetDelegateForFunctionPointer<FnGetDesc1>(
                        Marshal.ReadIntPtr(adapterVtbl, 10 * IntPtr.Size)); // IDXGIAdapter1::GetDesc1
                    if (getDesc1(adapter, out DxgiAdapterDesc1 desc) != 0) { continue; }
                    if ((desc.Flags & DXGI_ADAPTER_FLAG_SOFTWARE) != 0) { continue; }
                    nonSoftware++;
                    bool discreteVendor = desc.VendorId == VendorNvidia
                        || desc.VendorId == VendorAmd
                        || desc.VendorId == VendorAmd2;
                    if (discreteVendor && desc.DedicatedVideoMemory.ToInt64() >= MinDiscreteVram)
                    {
                        hasDiscrete = true;
                    }
                }
                finally
                {
                    _ = Marshal.Release(adapter);
                }
            }
            return nonSoftware >= 2;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[gpu] DXGI enumeration failed (skip relaunch): {ex.Message}");
            return false;
        }
        finally
        {
            if (factory != IntPtr.Zero) { _ = Marshal.Release(factory); }
        }
    }
}
