namespace StyleOS
{
    // ============================================================================
    // NOT LIVE. Scaffolding for a per-machine fingerprint, the kind a future
    // registry ban could key off of instead of just a username (so a banned
    // account can't just get a fresh name from the same machine). Nothing here
    // is wired into the shell yet, and worth saying plainly: a hardware id is
    // never a hard guarantee - it can change on a reinstall or be spoofed on a
    // VM, so this would only ever be one signal among others, not a lock.
    // ============================================================================

    public static class HardwareFingerprint
    {
        public static string Get() =>
            throw new System.NotImplementedException("Not wired up yet - see Registry/ for what's planned.");
    }
}
