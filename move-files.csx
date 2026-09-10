using System;
using System.IO;

var baseDir = @"e:\A-Chenzk\Code\MyProject\ConvenientSystem\ConvenientSystem.Desktop";

var dirs = new[] { "Controllers", "Services", @"UI\Forms", @"UI\Controls", @"UI\Shell" };
foreach (var d in dirs)
{
    var p = Path.Combine(baseDir, d);
    if (!Directory.Exists(p)) { Directory.CreateDirectory(p); Console.WriteLine($"Created: {d}"); }
}

void Move(string file, string subDir)
{
    var src = Path.Combine(baseDir, file);
    var dst = Path.Combine(baseDir, subDir, file);
    if (File.Exists(src)) { File.Move(src, dst); Console.WriteLine($"Moved {file} -> {subDir}"); }
    else Console.WriteLine($"NOT FOUND: {file}");
}

// Controllers
foreach (var f in new[] { "ApiSpecController.cs", "GitController.cs", "LocalAttendanceController.cs", "LocalMonitorController.cs", "PipelineController.cs", "UiStateController.cs", "UniversalBuildController.cs", "WebUpdateController.cs" })
    Move(f, "Controllers");

// Services
foreach (var f in new[] { "AppIndexService.cs", "DeployService.cs", "DesktopUpdateService.cs", "FileIndexService.cs", "GitService.cs", "HostFileService.cs", "LocalBuildService.cs", "LocalMonitorService.cs", "PipelineService.cs", "PipelineStore.cs", "SshCredentialStore.cs", "UiStateStore.cs", "UniversalBuildService.cs", "UniversalScheduleService.cs", "WebUpdateService.cs" })
    Move(f, "Services");

// Hosting
Move("ReverseProxyMiddleware.cs", "Hosting");

// UI/Forms
foreach (var f in new[] { "BrowserForm.cs", "MainForm.cs", "SystemHelpDialog.cs", "UpdateDialog.cs" })
    Move(f, @"UI\Forms");

// UI/Controls
foreach (var f in new[] { "LockOverlayControl.cs", "TreemapControl.cs" })
    Move(f, @"UI\Controls");

// UI/Shell
foreach (var f in new[] { "FloatingButton.cs", "FloatingPanel.cs", "LauncherEntryEditor.cs", "QuickLauncher.cs" })
    Move(f, @"UI\Shell");

// Infrastructure
foreach (var f in new[] { "GlobalHotkeyManager.cs", "LauncherModels.cs", "LauncherStore.cs", "UiStyle.cs" })
    Move(f, "Infrastructure");

Console.WriteLine("\nRemaining .cs files in root:");
foreach (var f in Directory.GetFiles(baseDir, "*.cs"))
    Console.WriteLine($"  {Path.GetFileName(f)}");
