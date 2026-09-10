using System;
using System.IO;

var logFile = @"e:\A-Chenzk\Code\MyProject\ConvenientSystem\move-tool\result.txt";
var log = new System.Text.StringBuilder();

void Log(string msg) { log.AppendLine(msg); Console.WriteLine(msg); }

var b = @"e:\A-Chenzk\Code\MyProject\ConvenientSystem\ConvenientSystem.Desktop";

string[] dirs = ["Services", @"UI\Forms", @"UI\Controls", @"UI\Shell"];
foreach (var d in dirs)
{
    var p = Path.Combine(b, d);
    if (!Directory.Exists(p)) Directory.CreateDirectory(p);
}

void M(string file, string sub)
{
    var s = Path.Combine(b, file);
    var d = Path.Combine(b, sub, file);
    if (File.Exists(s) && !File.Exists(d))
    {
        File.Move(s, d);
        Console.WriteLine("OK: " + file + " -> " + sub);
    }
    else if (!File.Exists(s))
    {
        Console.WriteLine("SKIP (not found): " + file);
    }
    else
    {
        Console.WriteLine("SKIP (exists): " + file);
    }
}

// Services
M("FileIndexService.cs", "Services");
M("GitService.cs", "Services");
M("LocalMonitorService.cs", "Services");
M("PipelineService.cs", "Services");
M("PipelineStore.cs", "Services");
M("UniversalBuildService.cs", "Services");
M("WebUpdateService.cs", "Services");

// Hosting
M("ReverseProxyMiddleware.cs", "Hosting");

// UI/Forms
M("BrowserForm.cs", @"UI\Forms");
M("MainForm.cs", @"UI\Forms");
M("SystemHelpDialog.cs", @"UI\Forms");
M("UpdateDialog.cs", @"UI\Forms");

// UI/Controls
M("LockOverlayControl.cs", @"UI\Controls");
M("TreemapControl.cs", @"UI\Controls");

// UI/Shell
M("FloatingButton.cs", @"UI\Shell");
M("FloatingPanel.cs", @"UI\Shell");
M("LauncherEntryEditor.cs", @"UI\Shell");
M("QuickLauncher.cs", @"UI\Shell");

// Infrastructure
M("GlobalHotkeyManager.cs", "Infrastructure");
M("LauncherModels.cs", "Infrastructure");
M("LauncherStore.cs", "Infrastructure");
M("UiStyle.cs", "Infrastructure");

Log("Remaining .cs in root:");
foreach (var f in Directory.GetFiles(b, "*.cs"))
    Log("  " + Path.GetFileName(f));

File.WriteAllText(logFile, log.ToString());
