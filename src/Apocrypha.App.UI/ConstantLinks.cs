namespace Apocrypha.App.UI;

public static class ConstantLinks
{
    // Apocrypha: all app-identity links point at the fork. Upstream's Discord/forums/status
    // links were removed with the rebrand (KIRO-HANDOFF §23.4); community channels return
    // here when Apocrypha has its own.
    public static readonly Uri GitHubUri = new("https://github.com/Jeagermeister/Apocrypha");
}
