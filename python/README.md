# styleos-installer

```bash
pip install "git+https://github.com/timdem228/styleos-linux.git@main#subdirectory=python"
styleos install      # git + .NET 8 SDK, clone timdem228/styleos-linux, dotnet publish
styleos run          # start StyleOS
styleos run -c "fastfetch"   # arguments after `run` go to StyleOS
styleos update       # git pull + rebuild
styleos doctor       # what's installed / missing
styleos uninstall    # remove the build (--purge also removes StyleOS users/files, -y skips the question)
```

Options: `--dir` (default `~/.styleos-install`), `--dry-run`; install/update also take `--branch` and `--repo`
(`STYLEOS_BRANCH` / `STYLEOS_REPO` work too and win over the last install's settings).

`styleos run` exports `DOTNET_GCHeapHardLimit=C800000` (200 MB) before starting StyleOS on Termux,
which stops the .NET "GC heap initialization failed" crash on Android. Set your own `DOTNET_GCHeapHardLimit` to override it.
