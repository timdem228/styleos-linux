# StyleOS on Termux (branch `termux`)

```bash
curl -fsSL https://raw.githubusercontent.com/timdem228/styleos-linux/termux/termux/install.sh | bash
```

or by hand:

```bash
pkg install python git
pip install "git+https://github.com/timdem228/styleos-linux.git@termux#subdirectory=python"
styleos install   # pkg install git dotnet8.0 (tur-repo fallback), clone, dotnet publish
styleos run       # start StyleOS
```

On regular Linux `styleos install` installs git/curl via your package manager and the .NET 8 SDK via the official dotnet-install.sh into `~/.styleos-install/dotnet`. Inside StyleOS, `pacman update` pulls and rebuilds the same checkout.
