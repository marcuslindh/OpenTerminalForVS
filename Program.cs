
using System.Runtime.InteropServices;
using Terminal.Gui;

List<string> solutionDirs = VisualStudioInterop.GetOpenSolutionDirectories();
List<ClaudeSession> claudeSessions = ClaudeSessionInterop.GetRunningSessions();

Application.Init();
Toplevel top = Application.Top;

Terminal.Gui.Window win = new Terminal.Gui.Window("Enter: PowerShell   E: Explorer   C: Claude   Tab: switch list   Esc: Cancel")
{
    X = 0,
    Y = 1, // lämna plats för menyrad
    Width = Dim.Fill(),
    Height = Dim.Fill()
};
top.Add(win);

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

// Varje lista har samma tre åtgärder; dirAt översätter radindex till mapp.
void WireActions(ListView listView, Func<int, string?> dirAt)
{
    listView.OpenSelectedItem += (args) =>
    {
        string? dir = dirAt(args.Item);
        if (dir == null) return;
        OpenInPowerShell(dir);
        Application.RequestStop();
    };

    listView.KeyPress += (args) =>
    {
        int key = args.KeyEvent.KeyValue;
        if (key == 'e' || key == 'E')
        {
            string? dir = dirAt(listView.SelectedItem);
            if (dir == null) return;
            OpenInExplorer(dir);
            Application.RequestStop();
            args.Handled = true;
        }
        else if (key == 'c' || key == 'C')
        {
            string? dir = dirAt(listView.SelectedItem);
            if (dir == null) return;
            OpenInClaude(dir);
            Application.RequestStop();
            args.Handled = true;
        }
    };
}

// en tom lista ska inte lägga beslag på halva fönstret
Dim vsHeight = claudeSessions.Count == 0 ? Dim.Fill()
    : solutionDirs.Count == 0 ? Dim.Sized(3)
    : Dim.Percent(50);

FrameView vsFrame = new FrameView($"Visual Studio solutions ({solutionDirs.Count})")
{
    X = 0,
    Y = 0,
    Width = Dim.Fill(),
    Height = vsHeight
};

FrameView claudeFrame = new FrameView($"Claude sessions ({claudeSessions.Count})")
{
    X = 0,
    Y = Pos.Bottom(vsFrame),
    Width = Dim.Fill(),
    Height = Dim.Fill()
};

win.Add(vsFrame, claudeFrame);

ListView? vsList = null;
ListView? claudeList = null;

if (solutionDirs.Count == 0)
{
    vsFrame.Add(new Label(0, 0, "No open Visual Studio solutions found."));
}
else
{
    // "*" markerar lösningar som redan har en Claude-session igång
    List<string> vsRows = new List<string>(solutionDirs.Count);
    foreach (string dir in solutionDirs)
    {
        bool hasSession = claudeSessions.Exists(s => string.Equals(s.Cwd.TrimEnd(Path.DirectorySeparatorChar), dir.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase));
        vsRows.Add((hasSession ? "* " : "  ") + dir);
    }

    vsList = new ListView(vsRows)
    {
        X = 0,
        Y = 0,
        Width = Dim.Fill(),
        Height = Dim.Fill(),
    };
    WireActions(vsList, i => i >= 0 && i < solutionDirs.Count ? solutionDirs[i] : null);
    vsFrame.Add(vsList);
}

if (claudeSessions.Count == 0)
{
    claudeFrame.Add(new Label(0, 0, "No running Claude sessions found."));
}
else
{
    List<string> claudeRows = new List<string>(claudeSessions.Count);
    foreach (ClaudeSession session in claudeSessions)
    {
        string status = string.IsNullOrEmpty(session.Status) ? "?" : session.Status;
        string name = string.IsNullOrEmpty(session.Name) ? $"pid {session.Pid}" : session.Name;
        claudeRows.Add($"[{status,-5}] {name}  —  {session.Cwd}");
    }

    claudeList = new ListView(claudeRows)
    {
        X = 0,
        Y = 0,
        Width = Dim.Fill(),
        Height = Dim.Fill(),
    };
    WireActions(claudeList, i => i >= 0 && i < claudeSessions.Count ? claudeSessions[i].Cwd : null);
    claudeFrame.Add(claudeList);
}

(vsList ?? claudeList)?.SetFocus();

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

/// <summary>En Claude Code-session som körs på den här datorn.</summary>
internal sealed class ClaudeSession
{
    public int Pid;
    public string Cwd = string.Empty;
    public string Name = string.Empty;
    public string Status = string.Empty;
}

/// <summary>
/// Hittar igångvarande Claude Code-sessioner. Claude skriver en fil per process i
/// %USERPROFILE%\.claude\sessions\&lt;pid&gt;.json med bland annat cwd, namn och status.
/// Filerna städas inte alltid bort, så varje pid kontrolleras mot processlistan.
/// JSON:en läses med en liten egen skanner istället för System.Text.Json — det håller
/// AOT-binären liten och filerna är platta objekt med enkla värden.
/// </summary>
internal static class ClaudeSessionInterop
{
    public static List<ClaudeSession> GetRunningSessions()
    {
        List<ClaudeSession> sessions = new List<ClaudeSession>();

        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude",
            "sessions");

        if (!Directory.Exists(dir))
        {
            return sessions;
        }

        foreach (string file in Directory.EnumerateFiles(dir, "*.json"))
        {
            try
            {
                string json = File.ReadAllText(file);

                string? pidText = ReadTopLevelValue(json, "pid");
                if (!int.TryParse(pidText, out int pid))
                {
                    continue;
                }

                string? cwd = ReadTopLevelValue(json, "cwd");
                if (string.IsNullOrEmpty(cwd))
                {
                    continue;
                }

                if (!IsSessionAlive(pid, ReadTopLevelValue(json, "procStart")))
                {
                    continue;
                }

                sessions.Add(new ClaudeSession
                {
                    Pid = pid,
                    Cwd = cwd,
                    Name = ReadTopLevelValue(json, "name") ?? string.Empty,
                    Status = ReadTopLevelValue(json, "status") ?? string.Empty
                });
            }
            catch
            {
                // filen kan skrivas om eller tas bort mitt i läsningen
            }
        }

        sessions.Sort((a, b) => string.Compare(a.Cwd, b.Cwd, StringComparison.OrdinalIgnoreCase));
        return sessions;
    }

    /// <summary>
    /// Pid:et kan ha återanvänts av en helt annan process, så starttiden jämförs med
    /// procStart (processens skapelsetid som FILETIME) när den finns.
    /// </summary>
    private static bool IsSessionAlive(int pid, string? procStart)
    {
        try
        {
            using System.Diagnostics.Process process = System.Diagnostics.Process.GetProcessById(pid);
            if (process.HasExited)
            {
                return false;
            }

            if (long.TryParse(procStart, out long started) && started > 0)
            {
                try
                {
                    return process.StartTime.ToFileTime() == started;
                }
                catch
                {
                    // StartTime kan nekas — fall tillbaka på processnamnet
                }
            }

            string name = process.ProcessName;
            return name.Contains("claude", StringComparison.OrdinalIgnoreCase)
                || name.Equals("node", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Läser värdet för en nyckel på objektets toppnivå. Nästlade nycklar ignoreras.</summary>
    private static string? ReadTopLevelValue(string json, string key)
    {
        int depth = 0;
        int i = 0;

        while (i < json.Length)
        {
            char c = json[i];

            if (c == '"')
            {
                int afterString = SkipString(json, i);
                int j = afterString;
                while (j < json.Length && char.IsWhiteSpace(json[j])) j++;

                if (depth == 1 && j < json.Length && json[j] == ':')
                {
                    string name = Unescape(json, i + 1, afterString - 1);
                    j++;
                    while (j < json.Length && char.IsWhiteSpace(json[j])) j++;

                    if (name == key && j < json.Length)
                    {
                        if (json[j] == '"')
                        {
                            int afterValue = SkipString(json, j);
                            return Unescape(json, j + 1, afterValue - 1);
                        }

                        int k = j;
                        while (k < json.Length && json[k] != ',' && json[k] != '}' && json[k] != ']' && !char.IsWhiteSpace(json[k])) k++;
                        return json.Substring(j, k - j);
                    }

                    i = j; // fortsätt vid värdet så att { och [ räknas in i djupet
                    continue;
                }

                i = afterString;
                continue;
            }

            if (c == '{' || c == '[') depth++;
            else if (c == '}' || c == ']') depth--;
            i++;
        }

        return null;
    }

    /// <summary>Returnerar index direkt efter strängens avslutande citattecken.</summary>
    private static int SkipString(string json, int quoteIndex)
    {
        int i = quoteIndex + 1;
        while (i < json.Length)
        {
            char c = json[i];
            if (c == '\\')
            {
                i += 2;
                continue;
            }
            if (c == '"')
            {
                return i + 1;
            }
            i++;
        }
        return json.Length;
    }

    private static string Unescape(string json, int start, int endExclusive)
    {
        if (endExclusive <= start)
        {
            return string.Empty;
        }

        System.Text.StringBuilder text = new System.Text.StringBuilder(endExclusive - start);
        int i = start;

        while (i < endExclusive)
        {
            char c = json[i];
            if (c != '\\' || i + 1 >= endExclusive)
            {
                text.Append(c);
                i++;
                continue;
            }

            char escape = json[i + 1];
            i += 2;
            switch (escape)
            {
                case 'n': text.Append('\n'); break;
                case 'r': text.Append('\r'); break;
                case 't': text.Append('\t'); break;
                case 'b': text.Append('\b'); break;
                case 'f': text.Append('\f'); break;
                case 'u':
                    if (i + 4 <= endExclusive && ushort.TryParse(json.AsSpan(i, 4), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out ushort code))
                    {
                        text.Append((char)code);
                        i += 4;
                    }
                    break;
                default: text.Append(escape); break;
            }
        }

        return text.ToString();
    }
}
