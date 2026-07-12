namespace Menuki.Config;

public static class ActionTypes
{
    public const string Shell = "shell";
    public const string Submenu = "submenu";
    public const string Exit = "exit";
    public const string OpenUrl = "open-url";
    public const string InputShell = "input+shell";
    public const string OpenFile = "open-file";
    public const string Script = "script";
    public const string Sequence = "sequence";
    public const string Parallel = "parallel";
    public const string Delay = "delay";
    public const string Background = "background";
    public const string Jobs = "jobs";
    public const string OpenConfig = "open-config";
    public const string Settings = "settings";

    public static readonly string[] BuiltIn =
        { Shell, Submenu, Exit, OpenUrl, InputShell, OpenFile, Script, Sequence, Parallel, Delay, Background, Jobs, OpenConfig, Settings };
}

/// <summary>Output formats for a shell action's captured result.</summary>
public static class ActionFormats
{
    public const string Json = "json";
    public const string Table = "table";
    public const string Raw = "raw";

    public static readonly string[] All = { Json, Table, Raw };
}
