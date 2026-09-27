# styleos-installer

```bash
pip install "git+https://github.com/timdem228/styleos-linux.git@termux#subdirectory=python"
styleos install      # git + .NET 8 SDK, clone timdem228/styleos-linux, dotnet publish
styleos run          # start StyleOS
styleos run -c "fastfetch"   # arguments after `run` go to StyleOS
styleos update       # git pull + rebuild
styleos doctor       # what's installed / missing
styleos uninstall    # remove the build (--purge also removes StyleOS users/files)
```

Options: `--dir` (default `~/.styleos-install`), `--dry-run`; install/update also take `--branch` and `--repo`.
