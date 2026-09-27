using System.Threading.Tasks;

namespace StyleOS
{
    // ============================================================================
    // NOT LIVE. This is scaffolding for a future module registry - a shared place
    // where StyleOS modules could be installed by name and published for others,
    // the way pacman and its own repo already work for packages. None of it is
    // wired into the shell yet; nothing here does anything if you call it.
    // ============================================================================

    /// <summary>
    /// Talks to the (future) StyleOS module registry: search, download by name, publish
    /// your own. See the "module" command for what actually works today - installing and
    /// running a module from a local folder.
    /// </summary>
    public static class ModuleRegistryClient
    {
        public static Task<string[]> Search(string query) =>
            throw new System.NotImplementedException("The module registry isn't live yet.");

        public static Task Download(string name, string destinationFolder) =>
            throw new System.NotImplementedException("The module registry isn't live yet.");

        public static Task Publish(string moduleFolder) =>
            throw new System.NotImplementedException("The module registry isn't live yet.");
    }
}
