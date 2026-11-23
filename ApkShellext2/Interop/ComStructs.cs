using System;
using System.Runtime.InteropServices;

namespace ApkShellext2.Interop {
    //  Blittable structures marshalled across the shell boundary. Deliberately no Pack attribute:
    //  the Win32 layout is the natural (packed-8 on x64 / packed-4 on x86) alignment, and forcing
    //  Pack=4 is what breaks FORMATETC/STGMEDIUM once the process is 64 bit. Sizes are pinned by
    //  InteropLayoutTests, one expectation per platform: 32 bit and 64 bit.

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT {
        public int X;
        public int Y;

        public POINT(int x, int y) {
            X = x;
            Y = y;
        }
    }

    /// <summary>
    /// <c>FORMATETC</c> (objidl.h): x64 32 bytes, x86 20 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct FORMATETC {
        public ushort cfFormat;
        public IntPtr ptd;
        public uint dwAspect;
        public int lindex;
        public uint tymed;
    }

    /// <summary>
    /// <c>STGMEDIUM</c> (objidl.h): x64 24 bytes, x86 12 bytes. <see cref="pUnkForRelease"/> is
    /// not the same thing as "free the handle": call <c>Ole32.ReleaseStgMedium</c> and let COM
    /// decide, otherwise streams returned by the shell leak or double free.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct STGMEDIUM {
        public uint tymed;
        public IntPtr unionmember;
        public IntPtr pUnkForRelease;
    }

    /// <summary>
    /// <c>CMINVOKECOMMANDINFO</c> (shlobj_core.h): x64 64 bytes, x86 40 bytes.
    /// <see cref="cbSize"/> must be set to <c>sizeof</c> or the shell rejects the structure.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct CMINVOKECOMMANDINFO {
        public uint cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public IntPtr lpVerb;
        public IntPtr lpParameters;
        public IntPtr lpDirectory;
        public int nShow;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr lpTitle;
    }

    /// <summary>
    /// <c>CMINVOKECOMMANDINFOEX</c> (shlobj_core.h): x64 104 bytes, x86 64 bytes. Only valid when
    /// <c>fMask</c> has <c>CMIC_MASK_UNICODE</c> (0x4000) set; the shell passes it when the menu
    /// was built with the extended verb flags.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct CMINVOKECOMMANDINFOEX {
        public uint cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public IntPtr lpVerb;
        public IntPtr lpParameters;
        public IntPtr lpDirectory;
        public int nShow;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr lpTitle;
        public IntPtr lpVerbW;
        public IntPtr lpParametersW;
        public IntPtr lpDirectoryW;
        public IntPtr lpTitleW;
        public POINT ptInvoke;
    }

    /// <summary>
    /// <c>MENUITEMINFO</c> (winuser.h): x64 80 bytes, x86 48 bytes.
    /// <see cref="dwTypeData"/> stays an <see cref="IntPtr"/> on purpose: for insert it points at
    /// the string, for query it points at a caller supplied buffer and <see cref="cch"/> is the
    /// capacity. A <c>string</c> field would silently pass a marshaler allocated buffer on query.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct MENUITEMINFO {
        public uint cbSize;
        public uint fMask;
        public uint fType;
        public uint fState;
        public uint wID;
        public IntPtr hSubMenu;
        public IntPtr hbmpChecked;
        public IntPtr hbmpUnchecked;
        public IntPtr dwItemData;
        public IntPtr dwTypeData;
        public uint cch;
        public IntPtr hbmpItem;

        /// <summary>
        /// Builds a zeroed structure with <see cref="cbSize"/> filled in. Every InsertMenuItemW /
        /// GetMenuItemInfoW call in the shell extension must go through this, because a stale or
        /// hard coded cbSize makes the call fail with no useful error.
        /// </summary>
        public static MENUITEMINFO Create(MIIM mask) {
            var info = new MENUITEMINFO();
            info.cbSize = (uint)Marshal.SizeOf(typeof(MENUITEMINFO));
            info.fMask = (uint)mask;
            return info;
        }
    }

    /// <summary>
    /// <c>PROPERTYKEY</c> (wtypes.h): 20 bytes on both platforms, because <see cref="Guid"/> is a
    /// 4 byte aligned structure (int, short, short, byte[8]) and not an 8 byte aligned one.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct PROPERTYKEY {
        public Guid fmtid;
        public uint pid;
    }

    /// <summary>
    /// <c>RTL_OSVERSIONINFOEXW</c> (winternl.h): 284 bytes on both platforms.
    /// <see cref="dwOSVersionInfoSize"/> must be set before calling <c>RtlGetVersion</c>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct OSVERSIONINFOEXW {
        public uint dwOSVersionInfoSize;
        public uint dwMajorVersion;
        public uint dwMinorVersion;
        public uint dwBuildNumber;
        public uint dwPlatformId;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szCSDVersion;

        public ushort wServicePackMajor;
        public ushort wServicePackMinor;
        public ushort wSuiteMask;
        public byte wProductType;
        public byte wReserved;
    }

    /// <summary>Clipboard / format values the shell hands out for file lists.</summary>
    internal static class ClipboardFormat {
        internal const ushort CF_HDROP = 15;
    }

    /// <summary>CMINVOKECOMMANDINFO.fMask bits used by the context menu handler.</summary>
    internal static class CMIC {
        internal const uint NONE = 0x00000000;
        internal const uint ICON = 0x00000010;
        internal const uint FLAG_NO_UI = 0x00000400;
        internal const uint UNICODE = 0x00004000;
        internal const uint ASYNCOK = 0x00010000;
        internal const uint NOASYNC = 0x00020000;
        internal const uint EXECUTECOMMAND = 0x00040000;
    }
}
