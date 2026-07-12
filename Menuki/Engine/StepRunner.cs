using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using Menuki.Config;

namespace Menuki.Engine;

/// <summary>
/// Result of running one step (or a whole sequence - then <see cref="Steps"/> is
/// populated). Serialized for agents, so field names are snake_case.
/// </summary>
public record StepResult(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("exit_code")] int ExitCode,
    [property: JsonPropertyName("stdout")] string? StdOut,
    [property: JsonPropertyName("stderr")] string? StdErr,
    [property: JsonPropertyName("error")] string? Error,
    [property: JsonPropertyName("steps")] List<StepResult>? Steps = null,
    [property: JsonPropertyName("stopped_early")] bool? StoppedEarly = null,
    [property: JsonPropertyName("attempts")] int? Attempts = null)
{
    [JsonPropertyName("ok")]
    public bool Ok => Error == null && ExitCode == 0;
}

/// <summary>
/// Shared execution core for "runnable" actions (shell / script / delay / nested
/// sequence). Separates the work from the presentation: <see cref="RunCaptured"/>
/// captures output for the headless/agent path, <see cref="RunSequenceStreaming"/>
/// streams live to the console for the interactive TUI. Both apply the same
/// stop-on-error semantics so behaviour matches across paths.
/// </summary>
public static class StepRunner
{
    /// <summary>Types that may appear as a step inside a sequence or parallel block.</summary>
    public static bool IsSequenceStep(string type) =>
        type is ActionTypes.Shell or ActionTypes.Script or ActionTypes.Delay
            or ActionTypes.Sequence or ActionTypes.Parallel;

    public static string Label(ActionDefinition a) => a.Type switch
    {
        ActionTypes.Shell => a.Command ?? "(no command)",
        ActionTypes.Script => "script " + (a.Path ?? "?") + (string.IsNullOrEmpty(a.Args) ? "" : " " + a.Args),
        ActionTypes.Delay => $"delay {a.Seconds ?? 0}s",
        ActionTypes.Sequence => $"sequence ({a.Steps?.Count ?? 0} steps)",
        ActionTypes.Parallel => $"parallel ({a.Steps?.Count ?? 0} steps)",
        ActionTypes.InputShell => a.CommandTemplate ?? "input+shell",
        _ => a.Type
    };

    // --- captured (headless) --------------------------------------------

    public static StepResult RunCaptured(ActionDefinition step)
    {
        var attempts = Math.Max(0, step.Retry ?? 0) + 1;
        StepResult result;
        var n = 0;
        do
        {
            if (n > 0) Sleep(step.RetryDelay ?? 0);
            result = RunCapturedOnce(step);
            n++;
        }
        while (!result.Ok && n < attempts);

        return n > 1 ? result with { Attempts = n } : result;
    }

    private static StepResult RunCapturedOnce(ActionDefinition step)
    {
        switch (step.Type)
        {
            case ActionTypes.Shell:
                if (string.IsNullOrEmpty(step.Command))
                    return Fail(step, "shell step has no 'command'.");
                return Captured(step, step.Command);

            case ActionTypes.Script:
            {
                var path = ExpandPath(step.Path ?? "");
                if (!File.Exists(path))
                    return new StepResult(step.Type, Label(step), 127, "", $"Script not found: {path}", null);
                return Captured(step, BuildScriptCommand(step, path));
            }

            case ActionTypes.Delay:
            {
                var s = step.Seconds ?? 0;
                Sleep(s);
                return new StepResult(step.Type, Label(step), 0, $"waited {s}s", "", null);
            }

            case ActionTypes.Sequence:
                return RunSequenceCaptured(step);

            case ActionTypes.Parallel:
                return RunParallelCaptured(step);

            default:
                return Fail(step, $"type '{step.Type}' is not allowed as a sequence step.");
        }
    }

    private static StepResult RunParallelCaptured(ActionDefinition par)
    {
        var steps = par.Steps ?? new List<ActionDefinition>();
        var results = new StepResult[steps.Count];
        var max = par.MaxParallel is int m and > 0 ? m : Math.Max(1, steps.Count);

        using (var slots = new SemaphoreSlim(max))
        {
            var tasks = new List<Task>();
            for (var i = 0; i < steps.Count; i++)
            {
                var idx = i;
                tasks.Add(Task.Run(() =>
                {
                    slots.Wait();
                    try { results[idx] = RunCaptured(steps[idx]); }
                    finally { slots.Release(); }
                }));
            }
            Task.WaitAll(tasks.ToArray());
        }

        var children = results.ToList();
        var ok = children.All(c => c.Ok);

        var hook = ok ? par.OnSuccess : par.OnFailure;
        if (hook != null)
        {
            var hr = RunCaptured(hook);
            children.Add(hr with { Label = $"[{(ok ? "on_success" : "on_failure")}] {hr.Label}" });
        }

        return new StepResult(ActionTypes.Parallel, Label(par), ok ? 0 : 1, null, null, null, children, null);
    }

    /// <summary>
    /// Prints the buffered results of a parallel block (used by the TUI executor
    /// and by a parallel step nested inside a streaming sequence).
    /// </summary>
    public static void PrintBuffered(StepResult parallel)
    {
        var children = parallel.Steps ?? new List<StepResult>();
        for (var i = 0; i < children.Count; i++)
        {
            var c = children[i];
            Console.ForegroundColor = c.Ok ? ConsoleColor.Green : ConsoleColor.Red;
            Console.WriteLine($"{(c.Ok ? "✓" : "✗")} [{i + 1}/{children.Count}] {c.Label}  (exit {c.ExitCode})");
            Console.ResetColor();

            if (!string.IsNullOrEmpty(c.StdOut))
                Console.WriteLine(Indent(c.StdOut));
            if (!string.IsNullOrEmpty(c.StdErr))
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine(Indent(c.StdErr));
                Console.ResetColor();
            }
            if (c.Error != null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("  " + c.Error);
                Console.ResetColor();
            }
            Console.WriteLine();
        }
    }

    private static string Indent(string text) =>
        string.Join("\n", text.Split('\n').Select(l => "  " + l));

    private static StepResult RunSequenceCaptured(ActionDefinition seq)
    {
        var children = new List<StepResult>();
        var stoppedEarly = false;

        foreach (var st in seq.Steps ?? new List<ActionDefinition>())
        {
            var r = RunCaptured(st);
            children.Add(r);
            if (!r.Ok && seq.StopOnError)
            {
                stoppedEarly = true;
                break;
            }
        }

        var ok = children.All(c => c.Ok);

        // Run the success/failure hook (informational - does not change the sequence outcome).
        var hook = ok ? seq.OnSuccess : seq.OnFailure;
        if (hook != null)
        {
            var hr = RunCaptured(hook);
            children.Add(hr with { Label = $"[{(ok ? "on_success" : "on_failure")}] {hr.Label}" });
        }

        return new StepResult(ActionTypes.Sequence, Label(seq), ok ? 0 : 1, null, null, null, children, stoppedEarly);
    }

    private static StepResult Captured(ActionDefinition step, string command)
    {
        var (code, stdout, stderr) = ShellRunner.RunCaptured(command);
        return new StepResult(step.Type, Label(step), code, stdout, stderr, null);
    }

    private static StepResult Fail(ActionDefinition step, string error) =>
        new(step.Type, Label(step), -1, null, null, error);

    // --- streaming (interactive TUI) ------------------------------------

    /// <summary>
    /// Run a sequence's steps with live output and per-step ✓/✗ headers.
    /// Returns true if every executed step succeeded.
    /// </summary>
    public static bool RunSequenceStreaming(
        List<ActionDefinition> steps, bool stopOnError,
        ActionDefinition? onSuccess = null, ActionDefinition? onFailure = null)
    {
        var allOk = true;

        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"▶ [{i + 1}/{steps.Count}] {Label(step)}");
            Console.ResetColor();

            var code = RunStreaming(step);
            Console.WriteLine();

            if (code != 0)
            {
                allOk = false;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"✗ step {i + 1} failed (exit {code})");
                Console.ResetColor();

                if (stopOnError)
                {
                    var skipped = steps.Count - i - 1;
                    if (skipped > 0)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.WriteLine($"(stopping - {skipped} step(s) skipped)");
                        Console.ResetColor();
                    }
                    break;
                }
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"✓ step {i + 1} done");
                Console.ResetColor();
            }

            Console.WriteLine();
        }

        var hook = allOk ? onSuccess : onFailure;
        if (hook != null)
        {
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine($"▶ {(allOk ? "on_success" : "on_failure")}: {Label(hook)}");
            Console.ResetColor();
            RunStreaming(hook);
            Console.WriteLine();
        }

        return allOk;
    }

    private static int RunStreaming(ActionDefinition step)
    {
        var attempts = Math.Max(0, step.Retry ?? 0) + 1;
        var code = -1;
        for (var n = 1; n <= attempts; n++)
        {
            if (n > 1)
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine($"  retry {n - 1}/{attempts - 1}...");
                Console.ResetColor();
                Sleep(step.RetryDelay ?? 0);
            }
            code = RunStreamingOnce(step);
            if (code == 0) break;
        }
        return code;
    }

    private static int RunStreamingOnce(ActionDefinition step)
    {
        switch (step.Type)
        {
            case ActionTypes.Shell:
                if (string.IsNullOrEmpty(step.Command))
                {
                    PrintError("shell step has no 'command'.");
                    return 1;
                }
                return ShellRunner.RunInteractive(step.Command);

            case ActionTypes.Script:
            {
                var path = ExpandPath(step.Path ?? "");
                if (!File.Exists(path))
                {
                    PrintError($"Script not found: {path}");
                    return 127;
                }
                return ShellRunner.RunInteractive(BuildScriptCommand(step, path));
            }

            case ActionTypes.Delay:
            {
                var s = step.Seconds ?? 0;
                Console.WriteLine($"waiting {s}s...");
                Sleep(s);
                return 0;
            }

            case ActionTypes.Sequence:
                return RunSequenceStreaming(
                    step.Steps ?? new List<ActionDefinition>(), step.StopOnError,
                    step.OnSuccess, step.OnFailure) ? 0 : 1;

            case ActionTypes.Parallel:
            {
                // Parallel can't stream without interleaving - run buffered, then print.
                var pr = RunParallelCaptured(step);
                PrintBuffered(pr);
                return pr.ExitCode;
            }

            default:
                PrintError($"type '{step.Type}' is not supported inside a sequence.");
                return 1;
        }
    }

    // --- helpers --------------------------------------------------------

    private static string BuildScriptCommand(ActionDefinition step, string path)
    {
        string command;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
            path.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
            command = $"powershell -ExecutionPolicy Bypass -File \"{path}\"";
        else
            command = $"\"{path}\"";

        if (!string.IsNullOrEmpty(step.Args))
            command += " " + step.Args;

        return command;
    }

    private static void Sleep(double seconds)
    {
        if (seconds > 0)
            Thread.Sleep((int)(seconds * 1000));
    }

    private static void PrintError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    private static string ExpandPath(string path)
    {
        if (path.StartsWith("~/") || path.StartsWith("~\\"))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, path[2..]);
        }
        return path;
    }
}
