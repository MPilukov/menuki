namespace Menuki.Completions;

/// <summary>
/// Emits a roff man page on <c>menuki man</c>. Generated from the binary (rather than a
/// committed <c>menuki.1</c>) so the version line always matches the installed build; Homebrew
/// pipes it into <c>man1.install</c>. View locally with: <c>menuki man | man /dev/stdin</c>.
/// </summary>
public static class ManPage
{
    public static int Run(string version)
    {
        Console.Write(Render(version));
        return 0;
    }

    private static string Render(string version) => $$"""
        .TH MENUKI 1 "" "menuki {{version}}" "User Commands"
        .SH NAME
        menuki \- interactive terminal menus and runbooks from a single JSON file
        .SH SYNOPSIS
        .B menuki
        [\fICOMMAND\fR] [\fB--config\fR \fIpath\fR] [\fIOPTIONS\fR]
        .SH DESCRIPTION
        .B menuki
        turns a single JSON file into an interactive terminal menu, usable by humans, scripts
        and AI agents. Menu items run shell commands, prompt for typed and validated inputs,
        open submenus, chain steps in sequence or parallel, and more. The same config can be
        driven interactively (a TUI) or headlessly (JSON in, JSON out) for automation.
        .PP
        Run with no arguments to open the welcome screen. Pass
        .B --config
        to open a specific menu file.
        .SH COMMANDS
        .TP
        .B tour
        Guided, hands-on feature tour.
        .TP
        .B examples \fR[\fIname\fR]
        List the bundled example packs, or run one. Append
        .B --save \fR[\fIpath\fR]
        to copy its JSON to a file, or
        .B --print
        to write its JSON to stdout.
        .TP
        .B list --config \fIpath\fR
        Headless: print the catalog of runnable actions as JSON.
        .TP
        .B exec --config \fIpath\fR --action \fIid\fR \fR[\fB--param\fR \fIk=v\fR ...]
        Headless: run one action and print the result as JSON.
        .TP
        .B validate --config \fIpath\fR
        Headless: check a config and print any issues as JSON.
        .TP
        .B mcp
        Run the Model Context Protocol authoring server over stdio.
        .TP
        .B completions \fR<\fIbash\fR|\fIzsh\fR|\fIfish\fR>
        Print a shell tab-completion script.
        .TP
        .B man
        Print this manual page (roff).
        .SH OPTIONS
        .TP
        .B --config \fIpath\fR
        Run the menu defined by the JSON config at \fIpath\fR.
        .TP
        .B -v\fR, \fB--version
        Print the version and exit.
        .TP
        .B -h\fR, \fB--help\fR, \fBhelp
        Show usage help and exit.
        .SH KEYS
        In the interactive menu: arrow keys or j/k to move, Enter to select, Esc or q to go
        back, T to cycle color themes, ? for item help, E to edit the JSON.
        .SH FILES
        .TP
        .I ~/.menuki/settings.json
        Saved appearance preferences (theme, selection marker). Created on first run.
        .TP
        .I ~/.menuki/examples/
        Where \fBexamples --save\fR writes materialized example packs.
        .SH ENVIRONMENT
        .TP
        .B MENUKI_HOME
        Override the \fI~/.menuki\fR directory used for settings and materialized examples.
        .SH EXAMPLES
        .TP
        Run the guided tour:
        .B menuki tour
        .TP
        Open a menu config:
        .B menuki --config ./ops.json
        .TP
        List actions for an agent to run:
        .B menuki list --config ./ops.json
        .SH SEE ALSO
        Project home and full documentation: https://github.com/MPilukov/menuki

        """;
}
