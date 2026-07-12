using Menuki.Config;

namespace Menuki.Actions;

public static class ActionExecutorFactory
{
    public static void RegisterBuiltIns(ActionExecutorRegistry registry)
    {
        registry.Register(ActionTypes.Shell, action =>
            new ShellActionExecutor(
                action.Command ?? throw new InvalidOperationException("Shell action requires 'command'"),
                action.Format, action.Query));

        registry.Register(ActionTypes.Submenu, action =>
            new SubmenuActionExecutor(
                action.Menu ?? throw new InvalidOperationException("Submenu action requires 'menu'")));

        registry.Register(ActionTypes.Exit, _ =>
            new ExitActionExecutor());

        registry.Register(ActionTypes.OpenUrl, action =>
            new OpenUrlActionExecutor(
                action.Url ?? throw new InvalidOperationException("OpenUrl action requires 'url'")));

        registry.Register(ActionTypes.InputShell, action =>
            new InputShellActionExecutor(
                action.Inputs ?? throw new InvalidOperationException("InputShell action requires 'inputs'"),
                action.CommandTemplate ?? throw new InvalidOperationException("InputShell action requires 'command_template'")));

        registry.Register(ActionTypes.OpenFile, action =>
            new OpenFileActionExecutor(
                action.Path ?? throw new InvalidOperationException("OpenFile action requires 'path'"),
                action.Editor));

        registry.Register(ActionTypes.Script, action =>
            new ScriptActionExecutor(
                action.Path ?? throw new InvalidOperationException("Script action requires 'path'"),
                action.Args));

        registry.Register(ActionTypes.Sequence, action =>
        {
            if (action.Steps == null)
                throw new InvalidOperationException("Sequence action requires 'steps'");
            return new SequenceActionExecutor(action);
        });

        registry.Register(ActionTypes.Parallel, action =>
        {
            if (action.Steps == null)
                throw new InvalidOperationException("Parallel action requires 'steps'");
            return new ParallelActionExecutor(action);
        });

        registry.Register(ActionTypes.Delay, action =>
            new DelayActionExecutor(action.Seconds ?? 0));

        registry.Register(ActionTypes.Background, action =>
            new BackgroundActionExecutor(
                action.Command ?? throw new InvalidOperationException("Background action requires 'command'"),
                action.Name));

        registry.Register(ActionTypes.Jobs, _ =>
            new JobsActionExecutor());

        registry.Register(ActionTypes.OpenConfig, action =>
            new OpenConfigActionExecutor(
                action.Path ?? throw new InvalidOperationException("OpenConfig action requires 'path'")));
    }

    /// <summary>New registry with all built-ins and any DLL plugins loaded.</summary>
    public static ActionExecutorRegistry BuildDefault()
    {
        var registry = new ActionExecutorRegistry();
        RegisterBuiltIns(registry);
        foreach (var plugin in Plugins.PluginLoader.LoadAll())
            registry.RegisterPlugin(plugin);
        return registry;
    }
}
