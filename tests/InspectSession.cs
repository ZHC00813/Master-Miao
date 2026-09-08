using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

internal static class InspectSession
{
    [DllImport("ole32.dll")] private static extern int GetRunningObjectTable(int reserved, out IRunningObjectTable table);
    [DllImport("ole32.dll")] private static extern int CreateBindCtx(int reserved, out IBindCtx context);
    [STAThread]
    private static int Main()
    {
        IRunningObjectTable table; IBindCtx context; IEnumMoniker entries;
        Marshal.ThrowExceptionForHR(GetRunningObjectTable(0, out table));
        Marshal.ThrowExceptionForHR(CreateBindCtx(0, out context));
        table.EnumRunning(out entries);
        IMoniker[] next = new IMoniker[1]; int count = 0;
        while (entries.Next(1, next, IntPtr.Zero) == 0)
        {
            string name; next[0].GetDisplayName(context, null, out name);
            if (name.IndexOf("Solid", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Sld", StringComparison.OrdinalIgnoreCase) >= 0) Console.WriteLine(name);
            count++; Marshal.ReleaseComObject(next[0]);
        }
        Console.WriteLine("Visible ROT entries: " + count);
        Marshal.ReleaseComObject(entries); Marshal.ReleaseComObject(context); Marshal.ReleaseComObject(table);
        return 0;
    }
}
