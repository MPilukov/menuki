using Menuki.Config;
using Menuki.Engine;
using Xunit;

namespace Menuki.Tests;

public class StepRunnerTests
{
    private static ActionDefinition Shell(string cmd) =>
        new() { Type = ActionTypes.Shell, Command = cmd };

    [Fact]
    public void Sequence_runs_all_steps_on_success()
    {
        var seq = new ActionDefinition
        {
            Type = ActionTypes.Sequence,
            Steps = new() { Shell("echo a"), Shell("echo b") }
        };

        var r = StepRunner.RunCaptured(seq);

        Assert.True(r.Ok);
        Assert.Equal(2, r.Steps!.Count);
        Assert.NotEqual(true, r.StoppedEarly);
    }

    [Fact]
    public void Sequence_stops_on_error_by_default()
    {
        var seq = new ActionDefinition
        {
            Type = ActionTypes.Sequence,
            Steps = new() { Shell("echo a"), Shell("exit 1"), Shell("echo c") }
        };

        var r = StepRunner.RunCaptured(seq);

        Assert.False(r.Ok);
        Assert.True(r.StoppedEarly);
        Assert.Equal(2, r.Steps!.Count); // third step never ran
    }

    [Fact]
    public void Sequence_continues_when_stop_on_error_is_false()
    {
        var seq = new ActionDefinition
        {
            Type = ActionTypes.Sequence,
            StopOnError = false,
            Steps = new() { Shell("echo a"), Shell("exit 1"), Shell("echo c") }
        };

        var r = StepRunner.RunCaptured(seq);

        Assert.False(r.Ok);
        Assert.NotEqual(true, r.StoppedEarly);
        Assert.Equal(3, r.Steps!.Count);
    }

    [Fact]
    public void Sequence_runs_on_success_hook()
    {
        var seq = new ActionDefinition
        {
            Type = ActionTypes.Sequence,
            OnSuccess = Shell("echo done"),
            Steps = new() { Shell("echo a") }
        };

        var r = StepRunner.RunCaptured(seq);

        Assert.True(r.Ok);
        Assert.Equal(2, r.Steps!.Count);
        Assert.StartsWith("[on_success]", r.Steps[^1].Label);
    }

    [Fact]
    public void Retry_reruns_a_failing_step()
    {
        var step = new ActionDefinition { Type = ActionTypes.Shell, Command = "exit 1", Retry = 2 };

        var r = StepRunner.RunCaptured(step);

        Assert.False(r.Ok);
        Assert.Equal(1, r.ExitCode);
        Assert.Equal(3, r.Attempts); // 1 + 2 retries
    }

    [Fact]
    public void No_attempts_recorded_when_first_try_succeeds()
    {
        var step = new ActionDefinition { Type = ActionTypes.Shell, Command = "echo ok", Retry = 2 };

        var r = StepRunner.RunCaptured(step);

        Assert.True(r.Ok);
        Assert.Null(r.Attempts);
        Assert.Equal("ok", r.StdOut);
    }

    [Fact]
    public void Parallel_runs_every_step()
    {
        var par = new ActionDefinition
        {
            Type = ActionTypes.Parallel,
            Steps = new() { Shell("echo 1"), Shell("echo 2"), Shell("echo 3") }
        };

        var r = StepRunner.RunCaptured(par);

        Assert.True(r.Ok);
        Assert.Equal(3, r.Steps!.Count);
        Assert.All(r.Steps, s => Assert.True(s.Ok));
    }

    [Fact]
    public void Parallel_fails_if_any_step_fails()
    {
        var par = new ActionDefinition
        {
            Type = ActionTypes.Parallel,
            Steps = new() { Shell("echo 1"), Shell("exit 1") }
        };

        var r = StepRunner.RunCaptured(par);

        Assert.False(r.Ok);
        Assert.Equal(1, r.ExitCode);
        Assert.Equal(2, r.Steps!.Count);
    }
}
