using System;

namespace ApkShellext2.Interop {
    //  Flag and enum types shared by the COM declarations in this directory. Values are copied
    //  from the Windows headers (and cross-checked against the vendored SharpShell declarations,
    //  see the per-type remarks); the underlying type follows the Win32 typedef so that a member
    //  declared as UINT stays 4 bytes wide and unsigned.

    /// <summary>IContextMenu.QueryContextMenu flags (<c>CMF_*</c>, winuser.h).</summary>
    [Flags]
    internal enum CMF : uint {
        Normal = 0x00000000,
        DefaultOnly = 0x00000001,
        VerbsOnly = 0x00000002,
        Explore = 0x00000004,
        NoVerbs = 0x00000008,
        CanRename = 0x00000010,
        NoDefault = 0x00000020,
        IncludeStatic = 0x00000040,
        ItemMenu = 0x00000080,
        ExtendedVerbs = 0x00000100,
        DisabledVerbs = 0x00000200,
        AsyncVerbState = 0x00000400,
        OptimizeForInvoke = 0x00000800,
        SyncCascadeMenu = 0x00001000,
        DoNotPickDefault = 0x00002000,
        Reserved = 0xFFFF0000
    }

    /// <summary>IContextMenu.GetCommandString flags (<c>GCS_*</c>).</summary>
    [Flags]
    internal enum GCS : uint {
        VerbA = 0x00000000,
        HelpTextA = 0x00000001,
        ValidateA = 0x00000002,
        VerbW = 0x00000004,
        HelpTextW = 0x00000005,
        ValidateW = 0x00000006,
        VerbIconW = 0x00000014,
        Unicode = 0x00000004
    }

    /// <summary>IExtractIconW.GetIconLocation input flags (<c>GIL_*</c>).</summary>
    [Flags]
    internal enum GILInFlags : uint {
        OpenIcon = 0x0001,
        ForShell = 0x0002,
        Async = 0x0020,
        DefaultIcon = 0x0040,
        ForShortcut = 0x0080,
        CheckShield = 0x0200
    }

    /// <summary>IExtractIconW.GetIconLocation output flags (<c>GIL_*</c>).</summary>
    [Flags]
    internal enum GILOutFlags : uint {
        SimulateDoc = 0x0001,
        PerInstance = 0x0002,
        PerClass = 0x0004,
        NotFilename = 0x0008,
        DontCache = 0x0010,
        Shield = 0x0200,
        ForceNoShield = 0x0400
    }

    /// <summary>IThumbnailProvider alpha channel type (<c>WTS_ALPHATYPE</c>, thumbcache.h).</summary>
    /// <remarks>Win32 declares this enum without a fixed underlying type, i.e. int (SharpShell IThumbnailProvider.cs:11).</remarks>
    internal enum WTS_ALPHATYPE {
        Unknown = 0,
        Rgb = 1,
        Argb = 2
    }

    /// <summary>IQueryInfo.GetInfoTip flags (<c>QITIPF_*</c>, shlobj_core.h).</summary>
    [Flags]
    internal enum QITIPF {
        Default = 0x00000000,
        UseName = 0x00000001,
        LinkNoTarget = 0x00000002,
        LinkUseTarget = 0x00000004,
        UseSlowTip = 0x00000008,
        SingleLine = 0x00000010
    }

    /// <summary>CoCreateInstance class context (<c>CLSCTX_*</c>).</summary>
    [Flags]
    internal enum CLSCTX : uint {
        InprocServer = 0x00000001,
        InprocHandler = 0x00000002,
        LocalServer = 0x00000004,
        RemoteServer = 0x00000010,
        All = InprocServer | InprocHandler | LocalServer | RemoteServer
    }

    /// <summary>STGMEDIUM transfer medium type (<c>TYMED_*</c>).</summary>
    [Flags]
    internal enum TYMED : uint {
        Null = 0x00000000,
        HGLOBAL = 0x00000001,
        File = 0x00000002,
        IStream = 0x00000004,
        IStorage = 0x00000008,
        Gdi = 0x00000010,
        MfPict = 0x00000020,
        EnhMf = 0x00000040
    }

    /// <summary>FORMATETC drawing aspect (<c>DVASPECT_*</c>).</summary>
    [Flags]
    internal enum DVASPECT : uint {
        Content = 0x00000001,
        Thumbnail = 0x00000002,
        Icon = 0x00000004,
        DocPrint = 0x00000008
    }

    /// <summary>MENUITEMINFO.fMask (<c>MIIM_*</c>).</summary>
    [Flags]
    internal enum MIIM : uint {
        State = 0x00000001,
        ID = 0x00000002,
        SubMenu = 0x00000004,
        CheckMarks = 0x00000008,
        Type = 0x00000010,
        Data = 0x00000020,
        String = 0x00000040,
        Bitmap = 0x00000080,
        FType = 0x00000100
    }

    /// <summary>MENUITEMINFO.fType (<c>MFT_*</c>).</summary>
    [Flags]
    internal enum MFT : uint {
        String = 0x00000000,
        Bitmap = 0x00000004,
        MenuBarBreak = 0x00000020,
        MenuBreak = 0x00000040,
        OwnerDraw = 0x00000100,
        RadioCheck = 0x00000200,
        Separator = 0x00000800,
        RightOrder = 0x00002000,
        RightJustify = 0x00004000
    }

    /// <summary>MENUITEMINFO.fState (<c>MFS_*</c>).</summary>
    [Flags]
    internal enum MFS : uint {
        Grayed = 0x00000001,
        Disabled = 0x00000002,
        Checked = 0x00000008,
        Highlight = 0x00000080,
        Unchecked = 0x00000000,
        Enabled = 0x00000000
    }

    /// <summary>TrackPopupMenuEx flags (<c>TPM_*</c>).</summary>
    [Flags]
    internal enum TPM : uint {
        LeftButton = 0x00000000,
        RightButton = 0x00000002,
        CenterAlign = 0x00000004,
        RightAlign = 0x00000008,
        TopAlign = 0x00000000,
        BottomAlign = 0x00000020,
        Horizontal = 0x00000000,
        Vertical = 0x00000040,
        NoNotify = 0x00000080,
        ReturnCmd = 0x0100
    }

    /// <summary>SHChangeNotify event ids (<c>SHCNE_*</c>); only the one the registrar needs.</summary>
    internal enum SHCNE : uint {
        AssocChanged = 0x08000000
    }

    /// <summary>SHChangeNotify flags (<c>SHCNF_*</c>).</summary>
    [Flags]
    internal enum SHCNF : uint {
        IdList = 0x0000,
        PathA = 0x0001,
        PrintepA = 0x0002,
        PathW = 0x0005,
        TypeMask = 0x00FF,
        Flush = 0x1000,
        PathUnicode = 0xA000
    }
}
