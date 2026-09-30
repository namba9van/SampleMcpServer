using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;

/// <summary>
/// Operator-facing shell access for the local machine ("stand-shell" mode).
///
/// This is deliberately separate from the governed agent-host <c>shell</c> capability: these tools are invoked by the
/// MCP client the operator is sitting at (Claude Desktop / Cowork), not by the autonomous agent loop. They are OFF unless
/// <c>SHELL_ENABLED=true</c>, and every working directory and output file must lie under <c>SHELL_ALLOWED_ROOTS</c>.
///
/// Environment:
///   SHELL_ENABLED          true/false (default false)
///   SHELL_ALLOWED_ROOTS    ';'-separated absolute directories the shell may run in and write to (default: current directory)
///   SHELL_DEFAULT_CWD      default working directory (default: first allowed root)
///   SHELL_PROGRAM          shell executable (default: powershell.exe on Windows, /bin/sh elsewhere)
///   SHELL_TIMEOUT_SECONDS  default and maximum timeout per command (default 120, hard cap 3600)
///   SHELL_MAX_OUTPUT_CHARS characters of stdout/stderr returned inline before truncation (default 60000)
///
/// This is a convenience for a trusted operator on their own machine, not a sandbox: the shell runs with the server's
/// account and can reach anything that account can. Keep the roots narrow and the server local (stdio).
/// </summary>
public sealed class ShellTools
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private const int HardTimeoutCapSeconds = 3600;

    private const string DefaultPathExt = ".COM;.EXE;.BAT;.CMD;.VBS;.VBE;.JS;.JSE;.WSF;.WSH;.MSC;.PY;.PYW";
    private sealed record ShellSettings(bool Enabled, IReadOnlyList<string> AllowedRoots, string DefaultCwd, string Program, int DefaultTimeoutSeconds, int MaxOutputChars);

    private static ShellSettings ReadSettings()
    {
        static string? Env(string name) => Environment.GetEnvironmentVariable(name);
        var enabled = string.Equals(Env("SHELL_ENABLED"), "true", StringComparison.OrdinalIgnoreCase) || Env("SHELL_ENABLED") == "1";
        var roots = (Env("SHELL_ALLOWED_ROOTS") ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeDirectory).Where(r => r.Length > 0).Distinct(PathComparer).ToList();
        if (roots.Count == 0) roots.Add(NormalizeDirectory(Directory.GetCurrentDirectory()));
        var defaultCwd = string.IsNullOrWhiteSpace(Env("SHELL_DEFAULT_CWD")) ? roots[0] : NormalizeDirectory(Env("SHELL_DEFAULT_CWD")!);
        var program = string.IsNullOrWhiteSpace(Env("SHELL_PROGRAM"))
            ? (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "powershell.exe" : "/bin/sh")
            : Env("SHELL_PROGRAM")!.Trim();
        var timeout = int.TryParse(Env("SHELL_TIMEOUT_SECONDS"), out var t) && t > 0 ? Math.Min(t, HardTimeoutCapSeconds) : 120;
        var maxOutput = int.TryParse(Env("SHELL_MAX_OUTPUT_CHARS"), out var m) && m >= 1000 ? m : 60000;
        return new(enabled, roots, defaultCwd, program, timeout, maxOutput);
    }

    private static readonly StringComparer PathComparer = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static string NormalizeDirectory(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim().Trim('"')));

    private static bool IsUnderRoots(string fullPath, IReadOnlyList<string> roots)
    {
        var candidate = NormalizeDirectory(fullPath);
        foreach (var root in roots)
        {
            if (PathComparer.Equals(candidate, root)) return true;
            if (candidate.StartsWith(root + Path.DirectorySeparatorChar, PathComparer.Equals("a", "A") ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) return true;
        }
        return false;
    }

    private static ShellSettings Require()
    {
        var settings = ReadSettings();
        if (!settings.Enabled)
            throw new McpException("Shell tools are disabled. Set SHELL_ENABLED=true and SHELL_ALLOWED_ROOTS in the server environment to allow command execution.");
        return settings;
    }

    private static string ResolveCwd(string? cwd, ShellSettings settings)
    {
        var resolved = string.IsNullOrWhiteSpace(cwd) ? settings.DefaultCwd : NormalizeDirectory(cwd);
        if (!IsUnderRoots(resolved, settings.AllowedRoots))
            throw new McpException($"Working directory '{resolved}' is outside SHELL_ALLOWED_ROOTS ({string.Join("; ", settings.AllowedRoots)}).");
        if (!Directory.Exists(resolved)) throw new McpException($"Working directory does not exist: {resolved}");
        return resolved;
    }

    private static (string FileName, string Arguments) BuildCommand(string program, string command)
    {
        var name = Path.GetFileNameWithoutExtension(program).ToLowerInvariant();
        return name switch
        {
            "powershell" or "pwsh" => (program, "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command " + Quote(command)),
            "cmd" => (program, "/d /s /c " + Quote(command)),
            _ => (program, "-c " + Quote(command))
        };
        static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    private sealed record ShellResult(int ExitCode, bool TimedOut, string Cwd, long ElapsedMs, string Stdout, string Stderr);

    private static async Task<ShellResult> RunCoreAsync(string command, string? cwd, string? stdin, int? timeoutSeconds, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command)) throw new McpException("Command is required.");
        var settings = Require();
        var workingDirectory = ResolveCwd(cwd, settings);
        var timeout = TimeSpan.FromSeconds(Math.Min(timeoutSeconds is > 0 ? timeoutSeconds.Value : settings.DefaultTimeoutSeconds, HardTimeoutCapSeconds));
        var (fileName, arguments) = BuildCommand(settings.Program, command);
        var info = new ProcessStartInfo(fileName, arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
            UseShellExecute = false, CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        // Claude Desktop starts MCP servers with a stripped environment. Without PATHEXT, cmd/PowerShell cannot resolve
        // 'git' or 'dotnet' by name, so fill in the Windows default when the host did not pass one.
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PATHEXT")))
            info.Environment["PATHEXT"] = DefaultPathExt;
        // Child processes must not inherit the MCP stdio channel semantics; they get their own pipes above.
        using var process = new Process { StartInfo = info };
        var watch = Stopwatch.StartNew();
        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        try
        {
            if (stdin is not null) await process.StandardInput.WriteAsync(stdin.AsMemory(), ct);
        }
        catch (IOException) { /* the command may exit before reading stdin */ }
        finally { try { process.StandardInput.Close(); } catch { /* ignore */ } }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutSource.CancelAfter(timeout);
        var timedOut = false;
        try { await process.WaitForExitAsync(timeoutSource.Token); }
        catch (OperationCanceledException)
        {
            timedOut = !ct.IsCancellationRequested;
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            if (!timedOut) throw;
        }
        var stdout = await stdoutTask; var stderr = await stderrTask;
        watch.Stop();
        return new(timedOut ? -1 : process.ExitCode, timedOut, workingDirectory, watch.ElapsedMilliseconds, stdout, stderr);
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max] + $"\n… [truncated {text.Length - max} characters; use shell_run_to_file for the full output]";

    [McpServerTool(Name = "shell_run")]
    [Description("Runs a shell command on this machine and returns exit code, stdout and stderr. Working directory must be inside SHELL_ALLOWED_ROOTS when that variable is set. Long output is truncated; use shell_run_to_file for big outputs.")]
    public async Task<string> Run(
        [Description("Command line to execute (PowerShell by default; see SHELL_PROGRAM).")] string command,
        [Description("Working directory. Defaults to SHELL_DEFAULT_CWD.")] string? cwd = null,
        [Description("Optional stdin text passed to the process.")] string? stdin = null,
        [Description("Timeout in seconds. Defaults to SHELL_TIMEOUT_SECONDS.")] int? timeoutSeconds = null,
        CancellationToken cancellationToken = default)
    {
        var settings = Require();
        var result = await RunCoreAsync(command, cwd, stdin, timeoutSeconds, cancellationToken);
        var builder = new StringBuilder();
        builder.Append("exit_code: ").Append(result.ExitCode).Append(result.TimedOut ? " (timed out, process tree killed)" : "").Append('\n');
        builder.Append("cwd: ").Append(result.Cwd).Append('\n');
        builder.Append("elapsed_ms: ").Append(result.ElapsedMs).Append('\n');
        builder.Append("--- stdout ---\n").Append(Truncate(result.Stdout, settings.MaxOutputChars)).Append('\n');
        if (result.Stderr.Length > 0) builder.Append("--- stderr ---\n").Append(Truncate(result.Stderr, settings.MaxOutputChars)).Append('\n');
        return builder.ToString();
    }

    [McpServerTool(Name = "shell_run_to_file")]
    [Description("Runs a shell command and writes its full stdout and stderr to a UTF-8 file under SHELL_ALLOWED_ROOTS, returning only a short summary. Use for outputs larger than the inline limit.")]
    public async Task<string> RunToFile(
        [Description("Command line to execute.")] string command,
        [Description("Path of the output file to create or overwrite; must be inside SHELL_ALLOWED_ROOTS.")] string outputFile,
        [Description("Working directory. Defaults to SHELL_DEFAULT_CWD.")] string? cwd = null,
        [Description("Optional stdin text passed to the process.")] string? stdin = null,
        [Description("Timeout in seconds. Defaults to SHELL_TIMEOUT_SECONDS.")] int? timeoutSeconds = null,
        CancellationToken cancellationToken = default)
    {
        var settings = Require();
        if (string.IsNullOrWhiteSpace(outputFile)) throw new McpException("outputFile is required.");
        var fullOutput = Path.GetFullPath(outputFile.Trim().Trim('"'));
        var outputDirectory = Path.GetDirectoryName(fullOutput) ?? throw new McpException("outputFile must be a file path.");
        if (!IsUnderRoots(outputDirectory, settings.AllowedRoots))
            throw new McpException($"Output file '{fullOutput}' is outside SHELL_ALLOWED_ROOTS.");
        var result = await RunCoreAsync(command, cwd, stdin, timeoutSeconds, cancellationToken);
        Directory.CreateDirectory(outputDirectory);
        var content = "--- stdout ---\n" + result.Stdout + (result.Stderr.Length > 0 ? "\n--- stderr ---\n" + result.Stderr : "");
        await File.WriteAllTextAsync(fullOutput, content, new UTF8Encoding(false), cancellationToken);
        return JsonSerializer.Serialize(new
        {
            exit_code = result.ExitCode, timed_out = result.TimedOut, cwd = result.Cwd, elapsed_ms = result.ElapsedMs,
            output_file = fullOutput, stdout_chars = result.Stdout.Length, stderr_chars = result.Stderr.Length,
            stdout_head = Truncate(result.Stdout, 400)
        }, JsonOptions);
    }

    [McpServerTool(Name = "shell_info")]
    [Description("Shows whether shell tools are enabled and the effective shell program, allowed roots, default working directory, timeout and output limit.")]
    public string Info()
    {
        var settings = ReadSettings();
        return JsonSerializer.Serialize(new
        {
            enabled = settings.Enabled,
            program = settings.Program,
            allowed_roots = settings.AllowedRoots,
            default_cwd = settings.DefaultCwd,
            timeout_seconds = settings.DefaultTimeoutSeconds,
            max_output_chars = settings.MaxOutputChars,
            os = RuntimeInformation.OSDescription,
            note = settings.Enabled ? "Commands run with the server account inside the allowed roots; this is not a sandbox." : "Set SHELL_ENABLED=true and SHELL_ALLOWED_ROOTS to enable."
        }, JsonOptions);
    }
}
