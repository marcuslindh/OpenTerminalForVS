
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Terminal.Gui;

List<string> solutionDirs = VisualStudio.GetOpenSolutionDirectories();

Application.Init();
Toplevel top = Application.Top;

Terminal.Gui.Window win = new Terminal.Gui.Window("Select solution folder  —  Enter: PowerShell   E: Explorer   Esc: Cancel")
{
    X = 0,
    Y = 1, // lämna plats för menyrad
    Width = Dim.Fill(),
    Height = Dim.Fill()
};
top.Add(win);

if (solutionDirs.Count == 0)
{
    win.Add(new Label(1, 1, "No open Visual Studio solutions found."));
}
else
{
    ListView listView = new ListView(solutionDirs)
    {
        X = 1,
        Y = 1,
        Width = Dim.Fill() - 2,
        Height = Dim.Fill() - 2,
    };

    void OpenInPowerShell(string dir)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoExit -Command cd \"{dir}\"",
            UseShellExecute = true,
            WorkingDirectory = dir
        });
    }

    void OpenInExplorer(string dir)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{dir}\"",
            UseShellExecute = true
        });
    }

    listView.OpenSelectedItem += (args) =>
    {
        if (args.Item < 0 || args.Item >= solutionDirs.Count) return;
        OpenInPowerShell(solutionDirs[args.Item]);
        Application.RequestStop();
    };

    listView.KeyPress += (args) =>
    {
        if (args.KeyEvent.KeyValue == 'e' || args.KeyEvent.KeyValue == 'E')
        {
            int idx = listView.SelectedItem;
            if (idx < 0 || idx >= solutionDirs.Count) return;
            OpenInExplorer(solutionDirs[idx]);
            Application.RequestStop();
            args.Handled = true;
        }
    };

    win.Add(listView);
}

Application.Run();


/// <summary>
/// Läser ut öppna solutions från körande Visual Studio-instanser via Running Object Table.
///
/// All COM-åtkomst sker genom råa vtable-anrop (<c>delegate* unmanaged</c>) i stället för
/// EnvDTE:s [ComImport]-interfaces. Det är ett krav för Native AOT: den inbyggda
/// COM-marshallingen är avstängd där (BuiltInComInterop.IsSupported=false), så både
/// "out IRunningObjectTable" och castningen "obj is DTE" kastar NotSupportedException.
/// ComWrappers.RegisterForMarshalling hjälper inte — [ComImport]-typer saknar de
/// vtable-stubbar den behöver.
///
/// DTE nås via IDispatch (GetIDsOfNames + Invoke) i stället för DTE-interfacets vtable,
/// eftersom slot-numren i ett dual interface är känsliga för TLB-ordningen.
/// </summary>
static unsafe class VisualStudio
{
    // vtable-slots. IUnknown upptar alltid slot 0-2 (QueryInterface, AddRef, Release).
    const int IUnknown_QueryInterface = 0;
    const int IUnknown_Release = 2;
    const int ROT_GetObject = 6;
    const int ROT_EnumRunning = 9;
    const int EnumMoniker_Next = 3;
    // IMoniker ärver IPersistStream ärver IPersist, därav den höga slotten.
    const int Moniker_GetDisplayName = 20;
    const int Dispatch_GetIDsOfNames = 5;
    const int Dispatch_Invoke = 6;

    const ushort DISPATCH_PROPERTYGET = 2;
    const int COINIT_APARTMENTTHREADED = 2;

    static readonly Guid IID_IDispatch = new("00020400-0000-0000-C000-000000000046");

    [DllImport("ole32.dll")]
    static extern int CoInitializeEx(IntPtr reserved, int coInit);

    [DllImport("ole32.dll")]
    static extern int GetRunningObjectTable(int reserved, out void* prot);

    [DllImport("ole32.dll")]
    static extern int CreateBindCtx(int reserved, out void* ppbc);

    static void** Vtable(void* p) => *(void***)p;

    static void Release(void* p)
    {
        if (p != null)
        {
            ((delegate* unmanaged<void*, int>)Vtable(p)[IUnknown_Release])(p);
        }
    }

    public static List<string> GetOpenSolutionDirectories()
    {
        List<string> dirs = new List<string>();

        CoInitializeEx(IntPtr.Zero, COINIT_APARTMENTTHREADED);

        if (GetRunningObjectTable(0, out void* rot) != 0 || rot == null)
        {
            return dirs;
        }

        void* enumMoniker;
        if (((delegate* unmanaged<void*, void**, int>)Vtable(rot)[ROT_EnumRunning])(rot, &enumMoniker) != 0)
        {
            Release(rot);
            return dirs;
        }

        void* moniker;
        uint fetched;
        while (((delegate* unmanaged<void*, uint, void**, uint*, int>)Vtable(enumMoniker)[EnumMoniker_Next])
                   (enumMoniker, 1, &moniker, &fetched) == 0 && fetched == 1)
        {
            try
            {
                string? name = GetDisplayName(moniker);
                if (name != null && name.StartsWith("!VisualStudio"))
                {
                    string? full = GetSolutionFullName(rot, moniker);
                    if (!string.IsNullOrEmpty(full))
                    {
                        string? dir = Path.GetDirectoryName(full);
                        if (!string.IsNullOrEmpty(dir))
                        {
                            dirs.Add(dir);
                        }
                    }
                }
            }
            catch
            {
                // någon instans kan stängas mitt i iteration
            }
            finally
            {
                Release(moniker);
            }
        }

        Release(enumMoniker);
        Release(rot);
        return dirs;
    }

    static string? GetDisplayName(void* moniker)
    {
        if (CreateBindCtx(0, out void* ctx) != 0)
        {
            return null;
        }

        try
        {
            void* pszName;
            int hr = ((delegate* unmanaged<void*, void*, void*, void**, int>)Vtable(moniker)[Moniker_GetDisplayName])
                (moniker, ctx, null, &pszName);
            if (hr != 0)
            {
                return null;
            }

            // GetDisplayName ger LPOLESTR (CoTaskMemAlloc), inte BSTR
            string? name = Marshal.PtrToStringUni((IntPtr)pszName);
            Marshal.FreeCoTaskMem((IntPtr)pszName);
            return name;
        }
        finally
        {
            Release(ctx);
        }
    }

    static string? GetSolutionFullName(void* rot, void* moniker)
    {
        void* punk;
        if (((delegate* unmanaged<void*, void*, void**, int>)Vtable(rot)[ROT_GetObject])(rot, moniker, &punk) != 0)
        {
            return null;
        }

        void* dte = null;
        try
        {
            Guid iid = IID_IDispatch;
            if (((delegate* unmanaged<void*, Guid*, void**, int>)Vtable(punk)[IUnknown_QueryInterface])
                    (punk, &iid, &dte) != 0)
            {
                return null;
            }

            if (!TryGetProperty(dte, "Solution", out ComVariant solution))
            {
                return null;
            }

            using (solution)
            {
                if (solution.VarType != VarEnum.VT_DISPATCH)
                {
                    return null;
                }

                void* sol = (void*)solution.GetRawDataRef<IntPtr>();
                if (sol == null || !IsOpen(sol))
                {
                    return null;
                }

                if (!TryGetProperty(sol, "FullName", out ComVariant fullName))
                {
                    return null;
                }

                using (fullName)
                {
                    return fullName.VarType == VarEnum.VT_BSTR
                        ? Marshal.PtrToStringBSTR(fullName.GetRawDataRef<IntPtr>())
                        : null;
                }
            }
        }
        finally
        {
            Release(dte);
            Release(punk);
        }
    }

    static bool IsOpen(void* solution)
    {
        if (!TryGetProperty(solution, "IsOpen", out ComVariant isOpen))
        {
            return false;
        }

        using (isOpen)
        {
            return isOpen.VarType == VarEnum.VT_BOOL && isOpen.GetRawDataRef<short>() != 0;
        }
    }

    static bool TryGetProperty(void* dispatch, string name, out ComVariant result)
    {
        result = default;

        if (!TryGetDispId(dispatch, name, out int dispid))
        {
            return false;
        }

        Guid iid = Guid.Empty;
        DISPPARAMS noArgs = default;
        ComVariant value = default;

        int hr = ((delegate* unmanaged<void*, int, Guid*, int, ushort, DISPPARAMS*, ComVariant*, void*, void*, int>)
                Vtable(dispatch)[Dispatch_Invoke])
            (dispatch, dispid, &iid, 0, DISPATCH_PROPERTYGET, &noArgs, &value, null, null);
        if (hr != 0)
        {
            return false;
        }

        result = value;
        return true;
    }

    static bool TryGetDispId(void* dispatch, string name, out int dispid)
    {
        dispid = 0;

        IntPtr pName = Marshal.StringToCoTaskMemUni(name);
        try
        {
            void* namePtr = (void*)pName;
            Guid iid = Guid.Empty;
            int id;

            int hr = ((delegate* unmanaged<void*, Guid*, void**, uint, int, int*, int>)
                    Vtable(dispatch)[Dispatch_GetIDsOfNames])
                (dispatch, &iid, &namePtr, 1, 0, &id);
            if (hr != 0)
            {
                return false;
            }

            dispid = id;
            return true;
        }
        finally
        {
            Marshal.FreeCoTaskMem(pName);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct DISPPARAMS
    {
        public IntPtr rgvarg;
        public IntPtr rgdispidNamedArgs;
        public int cArgs;
        public int cNamedArgs;
    }
}
