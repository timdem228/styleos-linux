# StyleOS on Termux (branch `termux`)

```bash
curl -fsSL https://raw.githubusercontent.com/timdem228/styleos-linux/termux/termux/install.sh | bash
```

or by hand:

```bash
pkg install python git
pip install "git+https://github.com/timdem228/styleos-linux.git@termux#subdirectory=python"
styleos install   # pkg install git dotnet8.0 (tur-repo fallback), clone, dotnet publish
styleos run       # export DOTNET_GCHeapHardLimit=C800000, then start StyleOS
```

`styleos run` always exports `DOTNET_GCHeapHardLimit=C800000` (200 MB) first: without a heap limit .NET on Android
dies at startup with "GC heap initialization failed". If you already have `DOTNET_GCHeapHardLimit` set, your value is used.

On regular Linux `styleos install` installs git/curl via your package manager and the .NET 8 SDK via the official dotnet-install.sh into `~/.styleos-install/dotnet` (the `main` branch has its own installer: `curl -fsSL https://raw.githubusercontent.com/timdem228/styleos-linux/main/install.sh | bash`). Inside StyleOS, `pacman update` pulls and rebuilds the same checkout.
