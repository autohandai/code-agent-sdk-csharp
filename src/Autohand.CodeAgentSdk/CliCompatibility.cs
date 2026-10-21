namespace Autohand.CodeAgentSdk;

internal static class CliCompatibility
{
    private static readonly HashSet<string> Unsupported = ["--agents-md","--no-agents-md","--agents-md-create","--agents-md-auto-update","--agents-md-path","--auto-save-interval","--compression-threshold","--summarization-threshold","--skills","--skill-sources","--install-missing-skills","--permission-mode","--permission-allow-list","--permission-deny-list"];
    private static readonly HashSet<string> Switches = ["--bare","--offline","--yes","--y","--dry-run","--debug","--auto-commit","-c","--unrestricted","--restricted","--plan","--answer-only","--setup-only","--no-idle-logout","--auto-skill","--learn","--learn-update","--project","--permissions","--settings","--traces-on","--traces-off","--login","--logout","--patch","--acp","--sdk-resume-last","--tmux","--auto-mode","--no-worktree","--interactive-on-complete","--setup","--about","--feedback","--cc","--context-compact","--no-cc","--no-context-compact","--ephemeral"];
    internal static void Validate(IReadOnlyList<string> args)
    {
        for(var index=0; index<args.Count; index++)
        {
            var argument=args[index];
            if(Unsupported.Contains(argument)) throw new AutohandSdkException(argument + " is unsupported by the current CLI; use a supported CLI option or runtime RPC instead");
            if(!Switches.Contains(argument)) index++;
        }
    }
}
