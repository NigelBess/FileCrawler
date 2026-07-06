using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace FileCrawler.Services;

/// <summary>The outcome of checking whether a newer build is available.</summary>
public enum UpdateAvailability
{
    /// <summary>Not running from a git working tree (e.g. copied/installed binaries), or git/upstream is unavailable.</summary>
    NotApplicable,
    /// <summary>The local checkout already matches the remote branch.</summary>
    UpToDate,
    /// <summary>The remote branch is ahead and the working tree is clean, so an update can be applied.</summary>
    Available,
    /// <summary>The remote branch is ahead but the working tree has uncommitted tracked changes; updating is skipped.</summary>
    AvailableButDirty,
    /// <summary>The check itself failed (no network, fetch error, timeout). We stay silent in this case.</summary>
    CheckFailed,
}

/// <summary>Result of an update check. <see cref="CommitsBehind"/> is only meaningful when an update is available.</summary>
public sealed record UpdateStatus(UpdateAvailability State, int CommitsBehind = 0);

/// <summary>
/// Detects whether the app is running from its source checkout and, if so, whether the remote branch has newer
/// commits. Applies an update by pulling + rebuilding + relaunching via a detached helper process, since the
/// running executable's own files are locked on Windows and cannot be rebuilt in-process.
/// </summary>
public interface IUpdateService
{
    /// <summary>Fetches from the remote and reports whether a newer build is available. Never throws.</summary>
    Task<UpdateStatus> CheckAsync();

    /// <summary>
    /// Spawns the detached updater (git pull + build + relaunch) and returns. The caller is responsible for
    /// shutting the app down immediately afterwards so the build can overwrite the running files.
    /// </summary>
    bool LaunchUpdater();
}

public sealed class GitUpdateService : IUpdateService
{
    // Fetch reaches the network; cap it so a slow/offline remote can't stall startup indefinitely.
    private static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan QuickGitTimeout = TimeSpan.FromSeconds(10);

    private readonly string _exePath;
    private readonly string? _repoRoot;

    public GitUpdateService()
    {
        _exePath = Environment.ProcessPath ?? "";
        var startDir = AppContext.BaseDirectory;
        _repoRoot = FindRepoRoot(startDir);
    }

    public async Task<UpdateStatus> CheckAsync()
    {
        if (_repoRoot is null || string.IsNullOrEmpty(_exePath))
            return new UpdateStatus(UpdateAvailability.NotApplicable);

        try
        {
            // No configured upstream (detached HEAD, local-only branch) → nothing to compare against.
            var upstream = await RunGitAsync("rev-parse --abbrev-ref --symbolic-full-name @{u}", QuickGitTimeout);
            if (!upstream.Ok || string.IsNullOrWhiteSpace(upstream.StdOut))
                return new UpdateStatus(UpdateAvailability.NotApplicable);

            var fetch = await RunGitAsync("fetch --quiet", FetchTimeout);
            if (!fetch.Ok)
                return new UpdateStatus(UpdateAvailability.CheckFailed);

            var behind = await RunGitAsync("rev-list --count HEAD..@{u}", QuickGitTimeout);
            if (!behind.Ok || !int.TryParse(behind.StdOut.Trim(), out var count))
                return new UpdateStatus(UpdateAvailability.CheckFailed);

            if (count == 0)
                return new UpdateStatus(UpdateAvailability.UpToDate);

            // Only tracked changes block an update; untracked files (bin/obj build output) are irrelevant to the pull.
            var status = await RunGitAsync("status --porcelain --untracked-files=no", QuickGitTimeout);
            var dirty = status.Ok && !string.IsNullOrWhiteSpace(status.StdOut);
            return new UpdateStatus(dirty ? UpdateAvailability.AvailableButDirty : UpdateAvailability.Available, count);
        }
        catch (Win32Exception)
        {
            // git isn't on PATH — treat as not applicable rather than nagging the user.
            return new UpdateStatus(UpdateAvailability.NotApplicable);
        }
        catch
        {
            return new UpdateStatus(UpdateAvailability.CheckFailed);
        }
    }

    public bool LaunchUpdater()
    {
        if (_repoRoot is null || string.IsNullOrEmpty(_exePath)) return false;

        try
        {
            var config = DetectConfiguration(_exePath);
            var project = Path.Combine(_repoRoot, "FileCrawler", "FileCrawler.csproj");
            var scriptPath = Path.Combine(Path.GetTempPath(), $"filecrawler-update-{Environment.ProcessId}.ps1");
            File.WriteAllText(scriptPath, UpdaterScript, new UTF8Encoding(false));

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = true, // show a console so build progress and any errors are visible to the user.
                WindowStyle = ProcessWindowStyle.Normal,
            };
            psi.ArgumentList.Add("-ExecutionPolicy");
            psi.ArgumentList.Add("Bypass");
            psi.ArgumentList.Add("-File");
            psi.ArgumentList.Add(scriptPath);
            psi.ArgumentList.Add("-AppPid");
            psi.ArgumentList.Add(Environment.ProcessId.ToString());
            psi.ArgumentList.Add("-Repo");
            psi.ArgumentList.Add(_repoRoot);
            psi.ArgumentList.Add("-Project");
            psi.ArgumentList.Add(project);
            psi.ArgumentList.Add("-Exe");
            psi.ArgumentList.Add(_exePath);
            psi.ArgumentList.Add("-Config");
            psi.ArgumentList.Add(config);

            Process.Start(psi);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Walks up from <paramref name="start"/> looking for a <c>.git</c> entry (folder or worktree file).</summary>
    private static string? FindRepoRoot(string start)
    {
        var dir = new DirectoryInfo(start);
        while (dir is not null)
        {
            var gitPath = Path.Combine(dir.FullName, ".git");
            if (Directory.Exists(gitPath) || File.Exists(gitPath))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    /// <summary>Derives the build configuration from the exe path (.../bin/&lt;Config&gt;/net10.0/App.exe).</summary>
    private static string DetectConfiguration(string exePath)
    {
        var tfmDir = Path.GetDirectoryName(exePath);          // .../bin/<Config>/net10.0
        var configDir = Path.GetDirectoryName(tfmDir);        // .../bin/<Config>
        var name = Path.GetFileName(configDir);
        return string.IsNullOrEmpty(name) ? "Debug" : name;
    }

    private Task<GitResult> RunGitAsync(string arguments, TimeSpan timeout) => Task.Run(() =>
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = _repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        if (!process.WaitForExit((int)timeout.TotalMilliseconds))
        {
            try { process.Kill(true); } catch { /* best effort */ }
            return new GitResult(false, stdout, stderr);
        }
        return new GitResult(process.ExitCode == 0, stdout, stderr);
    });

    private readonly record struct GitResult(bool Ok, string StdOut, string StdErr);

    // Runs detached from the (soon to exit) app. Waits for the app process to release its file locks, then
    // pulls + builds. On any failure it relaunches the previous build so the user is never left with nothing.
    private const string UpdaterScript = """
        param(
            [int]$AppPid,
            [string]$Repo,
            [string]$Project,
            [string]$Exe,
            [string]$Config
        )
        $ErrorActionPreference = 'Continue'
        $Host.UI.RawUI.WindowTitle = 'FileCrawler Updater'
        Write-Host 'FileCrawler updater' -ForegroundColor Cyan
        Write-Host 'Waiting for the app to close...'
        try { Wait-Process -Id $AppPid -Timeout 60 -ErrorAction SilentlyContinue } catch {}

        Set-Location $Repo
        Write-Host 'Pulling latest changes...' -ForegroundColor Cyan
        git pull
        if ($LASTEXITCODE -ne 0) {
            Write-Host 'git pull failed. Relaunching the current version.' -ForegroundColor Red
            Start-Process -FilePath $Exe
            Read-Host 'Press Enter to close this window'
            exit 1
        }

        Write-Host "Building ($Config)..." -ForegroundColor Cyan
        dotnet build $Project -c $Config
        if ($LASTEXITCODE -ne 0) {
            Write-Host 'Build failed. Relaunching the previous version.' -ForegroundColor Red
            Start-Process -FilePath $Exe
            Read-Host 'Press Enter to close this window'
            exit 1
        }

        Write-Host 'Update complete. Relaunching...' -ForegroundColor Green
        Start-Process -FilePath $Exe
        Start-Sleep -Seconds 1
        exit 0
        """;
}
