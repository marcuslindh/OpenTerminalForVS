
using System.Runtime.InteropServices;
using Terminal.Gui;

List<string> solutionDirs = VisualStudioInterop.GetOpenSolutionDirectories();

Application.Init();
Toplevel top = Application.Top;

Terminal.Gui.Window win = new Terminal.Gui.Window("Select solution folder  —  Enter: PowerShell   E: Explorer   C: Claude   Esc: Cancel")
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

    void OpenInClaude(string dir)
    {
        // sessionsnamnet blir mappens namn, t.ex. "OpenTerminalForVS.github"
        string sessionName = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoExit -Command claude --remote-control \"{sessionName}\"",
            UseShellExecute = true,
            WorkingDirectory = dir
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
        else if (args.KeyEvent.KeyValue == 'c' || args.KeyEvent.KeyValue == 'C')
        {
            int idx = listView.SelectedItem;
            if (idx < 0 || idx >= solutionDirs.Count) return;
            OpenInClaude(solutionDirs[idx]);
            Application.RequestStop();
            args.Handled = true;
        }
    };

    win.Add(listView);
}

Application.Run();


/// <summary>
/// Hittar öppna Visual Studio-lösningar via Running Object Table.
/// Alla COM-anrop går mot råa vtable-pekare (IUnknown/IDispatch) istället för
/// EnvDTE:s typbibliotek — inbyggd COM-interop stöds inte av NativeAOT.
/// </summary>
internal static unsafe class VisualStudioInterop
{
    private const int S_OK = 0;
    private const int COINIT_APARTMENTTHREADED = 0x2;
    private const int LOCALE_USER_DEFAULT = 0x0400;
    private const ushort DISPATCH_PROPERTYGET = 0x2;
    private const ushort VT_BSTR = 8;
    private const ushort VT_DISPATCH = 9;
    private const ushort VT_BOOL = 11;

    private static readonly Guid IID_IDispatch = new Guid("00020400-0000-0000-C000-000000000046");

    public static List<string> GetOpenSolutionDirectories()
    {
        List<string> dirs = new List<string>();

        // RPC_E_CHANGED_MODE m.m. ignoreras — tråden är då redan initierad
        CoInitializeEx(IntPtr.Zero, COINIT_APARTMENTTHREADED);

        if (GetRunningObjectTable(0, out IntPtr rot) != S_OK || rot == IntPtr.Zero)
        {
            return dirs;
        }

        try
        {
            IntPtr enumMoniker;
            if (RotEnumRunning(rot, &enumMoniker) != S_OK || enumMoniker == IntPtr.Zero)
            {
                return dirs;
            }

            try
            {
                IntPtr moniker;
                uint fetched;
                while (EnumMonikerNext(enumMoniker, 1, &moniker, &fetched) == S_OK && fetched == 1 && moniker != IntPtr.Zero)
                {
                    try
                    {
                        string? displayName = GetDisplayName(moniker);
                        if (displayName == null || !displayName.StartsWith("!VisualStudio", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        IntPtr unknown;
                        if (RotGetObject(rot, moniker, &unknown) != S_OK || unknown == IntPtr.Zero)
                        {
                            continue;
                        }

                        try
                        {
                            string? dir = GetSolutionDirectory(unknown);
                            if (!string.IsNullOrEmpty(dir))
                            {
                                dirs.Add(dir);
                            }
                        }
                        catch
                        {
                            // någon instans kan stängas mitt i iteration
                        }
                        finally
                        {
                            Release(unknown);
                        }
                    }
                    finally
                    {
                        Release(moniker);
                    }
                }
            }
            finally
            {
                Release(enumMoniker);
            }
        }
        finally
        {
            Release(rot);
        }

        return dirs;
    }

    /// <summary>Motsvarar dte.Solution.IsOpen / dte.Solution.FullName via IDispatch.</summary>
    private static string? GetSolutionDirectory(IntPtr dteUnknown)
    {
        IntPtr dte = QueryInterface(dteUnknown, IID_IDispatch);
        if (dte == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            if (!TryGetProperty(dte, "Solution", out Variant solutionValue))
            {
                return null;
            }

            try
            {
                if (solutionValue.vt != VT_DISPATCH || solutionValue.data == IntPtr.Zero)
                {
                    return null;
                }

                IntPtr solution = solutionValue.data;

                if (!TryGetProperty(solution, "IsOpen", out Variant isOpenValue))
                {
                    return null;
                }

                bool isOpen = isOpenValue.vt == VT_BOOL && (short)isOpenValue.data.ToInt64() != 0;
                VariantClear(&isOpenValue);
                if (!isOpen)
                {
                    return null;
                }

                if (!TryGetProperty(solution, "FullName", out Variant fullNameValue))
                {
                    return null;
                }

                try
                {
                    if (fullNameValue.vt != VT_BSTR || fullNameValue.data == IntPtr.Zero)
                    {
                        return null;
                    }

                    string? fullName = Marshal.PtrToStringBSTR(fullNameValue.data);
                    return string.IsNullOrEmpty(fullName) ? null : Path.GetDirectoryName(fullName);
                }
                finally
                {
                    VariantClear(&fullNameValue);
                }
            }
            finally
            {
                VariantClear(&solutionValue);
            }
        }
        finally
        {
            Release(dte);
        }
    }

    private static string? GetDisplayName(IntPtr moniker)
    {
        if (CreateBindCtx(0, out IntPtr bindCtx) != S_OK || bindCtx == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            IntPtr name;
            if (MonikerGetDisplayName(moniker, bindCtx, IntPtr.Zero, &name) != S_OK || name == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                return Marshal.PtrToStringUni(name);
            }
            finally
            {
                Marshal.FreeCoTaskMem(name);
            }
        }
        finally
        {
            Release(bindCtx);
        }
    }

    private static bool TryGetProperty(IntPtr dispatch, string name, out Variant result)
    {
        result = default;

        Guid iidNull = Guid.Empty;
        int dispId;

        fixed (char* namePtr = name)
        {
            char* nameArg = namePtr;
            if (DispatchGetIDsOfNames(dispatch, &iidNull, &nameArg, 1, LOCALE_USER_DEFAULT, &dispId) != S_OK)
            {
                return false;
            }
        }

        DispParams noArgs = default;
        fixed (Variant* resultPtr = &result)
        {
            return DispatchInvoke(
                dispatch,
                dispId,
                &iidNull,
                LOCALE_USER_DEFAULT,
                DISPATCH_PROPERTYGET,
                &noArgs,
                resultPtr,
                IntPtr.Zero,
                IntPtr.Zero) == S_OK;
        }
    }

    // --- råa vtable-anrop -------------------------------------------------
    // Slotnumren är fasta positioner i respektive gränssnitts vtable
    // (IUnknown-metoderna upptar alltid slot 0-2).

    private static void** VTable(IntPtr instance) => *(void***)instance;

    private static IntPtr QueryInterface(IntPtr instance, Guid iid)
    {
        IntPtr result;
        int hr = ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)VTable(instance)[0])(instance, &iid, &result);
        return hr == S_OK ? result : IntPtr.Zero;
    }

    private static uint Release(IntPtr instance)
        => instance == IntPtr.Zero ? 0 : ((delegate* unmanaged[Stdcall]<IntPtr, uint>)VTable(instance)[2])(instance);

    // IRunningObjectTable::GetObject
    private static int RotGetObject(IntPtr rot, IntPtr moniker, IntPtr* unknown)
        => ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)VTable(rot)[6])(rot, moniker, unknown);

    // IRunningObjectTable::EnumRunning
    private static int RotEnumRunning(IntPtr rot, IntPtr* enumMoniker)
        => ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)VTable(rot)[9])(rot, enumMoniker);

    // IEnumMoniker::Next
    private static int EnumMonikerNext(IntPtr enumMoniker, uint count, IntPtr* monikers, uint* fetched)
        => ((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, uint*, int>)VTable(enumMoniker)[3])(enumMoniker, count, monikers, fetched);

    // IMoniker::GetDisplayName (arvet från IPersistStream skjuter ned metoden till slot 20)
    private static int MonikerGetDisplayName(IntPtr moniker, IntPtr bindCtx, IntPtr monikerToLeft, IntPtr* displayName)
        => ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, IntPtr*, int>)VTable(moniker)[20])(moniker, bindCtx, monikerToLeft, displayName);

    // IDispatch::GetIDsOfNames
    private static int DispatchGetIDsOfNames(IntPtr dispatch, Guid* iid, char** names, uint nameCount, int lcid, int* dispIds)
        => ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, char**, uint, int, int*, int>)VTable(dispatch)[5])(dispatch, iid, names, nameCount, lcid, dispIds);

    // IDispatch::Invoke
    private static int DispatchInvoke(IntPtr dispatch, int dispId, Guid* iid, int lcid, ushort flags, DispParams* args, Variant* result, IntPtr exceptionInfo, IntPtr argError)
        => ((delegate* unmanaged[Stdcall]<IntPtr, int, Guid*, int, ushort, DispParams*, Variant*, IntPtr, IntPtr, int>)VTable(dispatch)[6])(
            dispatch, dispId, iid, lcid, flags, args, result, exceptionInfo, argError);

    [StructLayout(LayoutKind.Sequential)]
    private struct Variant
    {
        public ushort vt;
        public ushort reserved1;
        public ushort reserved2;
        public ushort reserved3;
        public IntPtr data;
        public IntPtr dataHigh;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DispParams
    {
        public IntPtr rgvarg;
        public IntPtr rgdispidNamedArgs;
        public uint cArgs;
        public uint cNamedArgs;
    }

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(IntPtr reserved, int coInit);

    [DllImport("ole32.dll")]
    private static extern int GetRunningObjectTable(int reserved, out IntPtr rot);

    [DllImport("ole32.dll")]
    private static extern int CreateBindCtx(int reserved, out IntPtr bindCtx);

    [DllImport("oleaut32.dll")]
    private static extern int VariantClear(Variant* variant);
}
