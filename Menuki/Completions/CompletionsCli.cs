using Menuki.Examples;

namespace Menuki.Completions;

/// <summary>
/// Emits shell tab-completion scripts on <c>menuki completions &lt;shell&gt;</c>. The scripts are
/// generated (not hand-maintained) so the completed subcommands and the list of bundled example
/// packs always match the binary. Homebrew installs them via
/// <c>generate_completions_from_executable</c>; users of other install methods can source the
/// output directly (see <c>menuki completions --help</c>).
/// </summary>
public static class CompletionsCli
{
    // Top-level subcommands the user can type, with a one-line description (zsh/fish show these).
    private static readonly (string Name, string Desc)[] Commands =
    {
        ("tour", "Guided, hands-on feature tour"),
        ("examples", "List or run a bundled example pack"),
        ("list", "Headless: catalog of runnable actions (JSON)"),
        ("exec", "Headless: run one action (JSON)"),
        ("validate", "Headless: check a config (JSON)"),
        ("mcp", "Run the MCP authoring server (stdio)"),
        ("completions", "Print a shell completion script"),
        ("help", "Show help"),
    };

    private static readonly (string Name, string Desc)[] Flags =
    {
        ("--config", "Run a menu config"),
        ("--action", "Action id (headless exec)"),
        ("--param", "Parameter k=v (headless exec)"),
        ("--version", "Print the version"),
        ("--help", "Show help"),
    };

    public static int Run(string[] args)
    {
        // args[0] == "completions"
        var shell = args.Length > 1 ? args[1].ToLowerInvariant() : "";
        switch (shell)
        {
            case "bash":
                Console.Write(Bash());
                return 0;
            case "zsh":
                Console.Write(Zsh());
                return 0;
            case "fish":
                Console.Write(Fish());
                return 0;
            case "":
            case "--help":
            case "-h":
                PrintUsage();
                return shell == "" ? 1 : 0;
            default:
                Console.Error.WriteLine($"Unknown shell '{shell}'. Supported: bash, zsh, fish.");
                return 1;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            menuki completions <bash|zsh|fish> - print a shell tab-completion script

            Install (pick your shell):
              bash:  menuki completions bash > /usr/local/etc/bash_completion.d/menuki
              zsh:   menuki completions zsh  > "${fpath[1]}/_menuki"
              fish:  menuki completions fish > ~/.config/fish/completions/menuki.fish

            Or source it directly from your shell rc, e.g. for bash:
              source <(menuki completions bash)

            (Homebrew installs these automatically - no manual step needed.)
            """);
    }

    private static string ExampleNames() =>
        string.Join(" ", ExampleCatalog.List().Select(e => e.Name));

    private static string CommandNames() => string.Join(" ", Commands.Select(c => c.Name));

    private static string FlagNames() => string.Join(" ", Flags.Select(f => f.Name));

    private static string Bash()
    {
        return $$"""
            # bash completion for menuki
            _menuki() {
                local cur prev
                cur="${COMP_WORDS[COMP_CWORD]}"
                prev="${COMP_WORDS[COMP_CWORD-1]}"

                local commands="{{CommandNames()}}"
                local flags="{{FlagNames()}}"
                local examples="{{ExampleNames()}}"

                case "$prev" in
                    --config)
                        COMPREPLY=( $(compgen -f -- "$cur") )
                        return 0
                        ;;
                    examples)
                        COMPREPLY=( $(compgen -W "$examples" -- "$cur") )
                        return 0
                        ;;
                    completions)
                        COMPREPLY=( $(compgen -W "bash zsh fish" -- "$cur") )
                        return 0
                        ;;
                esac

                if [[ "$cur" == -* ]]; then
                    COMPREPLY=( $(compgen -W "$flags" -- "$cur") )
                    return 0
                fi

                COMPREPLY=( $(compgen -W "$commands" -- "$cur") )
            }
            complete -F _menuki menuki

            """;
    }

    private static string Zsh()
    {
        var cmdLines = string.Join("\n        ",
            Commands.Select(c => $"'{c.Name}:{EscapeZsh(c.Desc)}'"));
        return $$"""
            #compdef menuki
            _menuki() {
                local -a commands
                commands=(
                    {{cmdLines}}
                )

                _arguments -C \
                    '--config[Run a menu config]:config file:_files -g "*.json"' \
                    '--action[Action id (headless exec)]:action:' \
                    '--param[Parameter k=v (headless exec)]:param:' \
                    '(--version -v)'{--version,-v}'[Print the version]' \
                    '(--help -h)'{--help,-h}'[Show help]' \
                    '1: :->command' \
                    '*:: :->args'

                case $state in
                    command)
                        _describe 'menuki command' commands
                        ;;
                    args)
                        case $words[1] in
                            examples)
                                _values 'example pack' {{ExampleNames()}}
                                ;;
                            completions)
                                _values 'shell' bash zsh fish
                                ;;
                        esac
                        ;;
                esac
            }
            _menuki "$@"

            """;
    }

    private static string Fish()
    {
        var lines = new List<string>
        {
            "# fish completion for menuki",
        };

        // Top-level subcommands: only when no subcommand has been typed yet.
        foreach (var (name, desc) in Commands)
            lines.Add($"complete -c menuki -f -n '__fish_use_subcommand' -a '{name}' -d '{EscapeFish(desc)}'");

        // Example names after `menuki examples`.
        lines.Add($"complete -c menuki -f -n '__fish_seen_subcommand_from examples' -a '{ExampleNames()}'");
        // Shells after `menuki completions`.
        lines.Add("complete -c menuki -f -n '__fish_seen_subcommand_from completions' -a 'bash zsh fish'");

        // Global flags.
        lines.Add("complete -c menuki -l config -r -d 'Run a menu config'");
        lines.Add("complete -c menuki -l action -x -d 'Action id (headless exec)'");
        lines.Add("complete -c menuki -l param -x -d 'Parameter k=v (headless exec)'");
        lines.Add("complete -c menuki -l version -s v -d 'Print the version'");
        lines.Add("complete -c menuki -l help -s h -d 'Show help'");

        return string.Join("\n", lines) + "\n";
    }

    private static string EscapeZsh(string s) => s.Replace("'", "''").Replace(":", "\\:");
    private static string EscapeFish(string s) => s.Replace("'", "\\'");
}
