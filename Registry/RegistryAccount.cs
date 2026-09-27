using System.Threading.Tasks;

namespace StyleOS
{
    // ============================================================================
    // NOT LIVE. Scaffolding for registry accounts (register / log in / log out),
    // so that publishing a module can eventually be tied to someone, instead of
    // being anonymous. Nothing here is wired into the shell yet.
    // ============================================================================

    /// <summary>An account on the (future) module registry.</summary>
    public class RegistryProfile
    {
        public string Username;
        public string Token;
    }

    public static class RegistryAccount
    {
        public static Task<RegistryProfile> Register(string username, string password) =>
            throw new System.NotImplementedException("Registry accounts aren't live yet.");

        public static Task<RegistryProfile> Login(string username, string password) =>
            throw new System.NotImplementedException("Registry accounts aren't live yet.");

        public static void Logout() =>
            throw new System.NotImplementedException("Registry accounts aren't live yet.");
    }
}
