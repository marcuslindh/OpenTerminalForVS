
using EnvDTE;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Terminal.Gui;

List<string> solutionDirs = new List<string>();
foreach (DTE dte in GetRunningVisualStudios())
{
    try
    {
        if (dte.Solution != null && dte.Solution.IsOpen)
        {
            if (string.IsNullOrEmpty(dte.Solution.FullName))
            {
                continue;
            }

            string? dir = Path.GetDirectoryName(dte.Solution.FullName);
            if (!string.IsNullOrEmpty(dir))
            {
                solutionDirs.Add(dir);
            }
        }
    }
    catch
    {
        // någon instans kan stängas mitt i iteration
    }
}

Application.Init();
Toplevel top = Application.Top;

Terminal.Gui.Window win = new Terminal.Gui.Window("Välj lösningsmapp att öppna i PowerShell")
{
    X = 0,
    Y = 1, // lämna plats för menyrad
    Width = Dim.Fill(),
    Height = Dim.Fill()
};
top.Add(win);

if (solutionDirs.Count == 0)
{
    win.Add(new Label(1, 1, "Inga öppna Visual Studio-lösningar hittades."));
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

    int lastIndex = -1;

    listView.SelectedItemChanged += (args) =>
    {
        if (args.Item == lastIndex || args.Item < 0 || args.Item >= solutionDirs.Count)
        {
            return;
        }

        lastIndex = args.Item;
        string dir = solutionDirs[args.Item];
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoExit -Command cd \"{dir}\"",
            UseShellExecute = true,
            WorkingDirectory = dir
        });
        Application.RequestStop();
    };

    win.Add(listView);
}

Application.Run();




static System.Collections.Generic.IEnumerable<DTE> GetRunningVisualStudios()
{
    GetRunningObjectTable(0, out IRunningObjectTable rot);
    rot.EnumRunning(out IEnumMoniker enumMoniker);

    IMoniker[] monikers = new IMoniker[1];

    while (enumMoniker.Next(1, monikers, IntPtr.Zero) == 0)
    {
        CreateBindCtx(0, out IBindCtx ctx);
        monikers[0].GetDisplayName(ctx, null, out string name);

        if (name.StartsWith("!VisualStudio"))
        {
            rot.GetObject(monikers[0], out object obj);
            if (obj is DTE dte)
            {
                yield return dte;
            }
        }
    }
}

[DllImport("ole32.dll")]
static extern int GetRunningObjectTable(int reserved, out IRunningObjectTable prot);

[DllImport("ole32.dll")]
static extern int CreateBindCtx(int reserved, out IBindCtx ppbc);