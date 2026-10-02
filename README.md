<p align="center">
  <img src="https://github.com/user-attachments/assets/cc340c4d-2057-41fe-b63c-220832767396" alt="StyleOS Logo" width="600">
</p>

<p align="center"><b>StyleOS for Linux &amp; Termux</b> · .NET 8 · x64 / arm64</p>

---
> [!WARNING]
> This project is still in development, it is still an open beta, many features have not been added yet  
> It's a console system: it runs inside your terminal, it doesn't replace your real OS.

---

Hey there! This is **StyleOS-Linux**, the Linux port of [StyleOS](https://github.com/timdem228/styleos-), a custom shell environment I originally made for Windows with C#. Same old-school terminal look, same modern stuff under the hood (image rendering, a package manager, a small Python scripting layer), but now it runs on regular Linux distros and on Android through **Termux**.

I'm still fixing stuff here and there, so if you find a bug, please let me know or use the `bugreport` tool.

## **Installation**

### One-liner (Linux)
```bash
curl -fsSL https://raw.githubusercontent.com/timdem228/styleos-linux/main/install.sh | bash
```
It installs python3 + git with your package manager (apt, dnf, yum, pacman, zypper, apk), puts the `styleos` command into `~/.styleos-install/venv` (linked to `~/.local/bin/styleos`), grabs the .NET 8 SDK if you don't have it and builds StyleOS from source. After that:
```bash
styleos run
```

### Termux (Android)
```bash
curl -fsSL https://raw.githubusercontent.com/timdem228/styleos-linux/termux/termux/install.sh | bash
styleos run
```
On Termux the installer uses `pkg install dotnet8.0` (and turns on `tur-repo` if the main repo doesn't have it). `styleos run` exports `DOTNET_GCHeapHardLimit=C800000` before starting, otherwise .NET crashes on Android with *"GC heap initialization failed"*. More in [README-TERMUX](https://github.com/timdem228/styleos-linux/blob/termux/README-TERMUX.md).

### By hand
```bash
pip install "git+https://github.com/timdem228/styleos-linux.git@main#subdirectory=python"
styleos install
styleos run
```

### The `styleos` command
| command | what it does |
|---|---|
| `styleos install` | git + .NET 8 SDK, clone this repo, `dotnet publish` |
| `styleos run` | start StyleOS (anything after `run` goes to StyleOS, e.g. `styleos run -c "fastfetch"`) |
| `styleos update` | pull the latest source and rebuild |
| `styleos doctor` | show what's installed and what's missing |
| `styleos uninstall` | remove the build (`--purge` also removes your StyleOS users/files) |

Install/update also take `--branch`, `--repo` and `--dir`.

### Prebuilt builds
Tagged versions get self-contained `styleos-linux-x64.tar.gz` / `styleos-linux-arm64.tar.gz` (no .NET needed) and a `styleos-portable.tar.gz` (needs a dotnet runtime, the one to use on Termux) on the [releases page](https://github.com/timdem228/styleos-linux/releases). Unpack anywhere and run `./StyleOS`.

## **Getting Started**

Here is the default login. I haven't set up a complex setup wizard yet, so just use these:

*   **Login:** `root`
*   **Password:** (Just press **Enter**, there is no password by default)

> **Note:** You can change your password once you're in by using the `passwd` command.

Your StyleOS users and files live in `$STYLEOS_HOME` (default `~/.local/share/styleos`), separate from the build, so reinstalling doesn't wipe them.

## **How to use it**

The system works mostly like a Linux terminal. If you're lost, just type `help` to see what you can do, or `man <command>` for details on any one of them. Pipes, redirects (`|`, `>`, `>>`, `<`) and command chaining (`&&`, `||`, `;`) all work too.

### **Basic Commands**
*   `ls` or `dir` - see whats in the folder
*   `cd <folder>` - move around
*   `cat <file>` - read a text file
*   `nano <file>` - open the text editor
*   `fastfetch` / `neofetch` - show off your system specs (real kernel, memory and disks from the host)
*   `clear` - if the screen gets too messy

### **System & Apps**
*   `pacman update` - updates StyleOS. If you installed with `styleos install` (or any git checkout) it does `git pull` + rebuild; if you run a release build it downloads the newest release for your machine (verifies the signature first, if one was published)
*   `pacman update beta` / `pacman update stable` - grab a release from a specific channel just this once
*   `pacman channel beta|stable` - remember a channel for good, so plain `pacman update` uses it from then on
*   `whatsnew` / `whatsnew <version>` - show what changed in a version, straight from its GitHub release
*   `modules` / `lsmod` - see what's "loaded" under the hood (module name, size, what depends on it)
*   `bugreport` - if the system crashes or acts weird, use this to send me logs
*   `calc` - do some quick math
*   `theme` - change the colors if you don't like the green/blue look

### **Linux extras**
The Linux build can also be used straight from your normal shell, without booting:

```bash
StyleOS -c "uname -a; ls | sort -r"   # run commands and exit with their status (--exec)
StyleOS -u bob -c whoami              # same, as another StyleOS user
StyleOS script.sos                    # run a file of StyleOS commands, one per line
StyleOS --skip-boot                   # straight to the login prompt
StyleOS --version | --help
```
With the installer it's the same thing through `styleos run`, e.g. `styleos run -c "fastfetch"`.

### **Python Modules**
StyleOS can run small Python scripts as "modules." It uses the host's `python3` (on Termux: `pkg install python`). First time only, set up the bridge:

```
modules install modules
```

That checks your machine for Python and pip and installs the `styleos` library locally (nothing gets touched outside of StyleOS's own folder). If you've got more than one Python version on your machine, you can pin one:

```
modules install modules 3.12
```

After that, any folder with a `setup.module` manifest can be installed and run:

```
module install <path to the module's folder>
module run <name>
module list
module remove <name>
```

Inside a module you just do `import styleos as s` and you get a small API: `s.println()`, `s.user()`, `s.cwd()`, `s.version()`, `s.data_dir()`, `s.read_file()`, `s.write_file()`. Graphical libraries (tkinter, PyQt, pygame, and the rest) are always blocked - StyleOS modules are console-only and can't pop open their own window.

A module's `setup.module` can also ask for permissions it needs beyond that - `network` for sockets/http, `process` for spawning other programs, `filesystem` for reaching outside its own data folder. `module install` shows you exactly what's being asked for before installing anything. Every module also gets killed if it runs longer than 60 seconds (or whatever `timeout_seconds` says), so a hung script can't hang the shell. None of this is a full sandbox - it's there to catch accidents and casual misuse, not a deliberately malicious script that's trying to get around it.

### **Signed updates**
If a release has a published signature, `pacman update` checks it before installing anything - a release that doesn't match its signature gets refused outright. Releases without a signature just get a plain warning and install like before. Source installs don't use releases at all, they follow the git branch.

### **The Debug Menu**
If you want to see what the system is doing while it boots up, mash the **F6** key right after you start StyleOS. It will open a Debug Menu where you can enable "Verbose" mode (you'll need the root password for this though). `--skip-boot` (or `STYLEOS_SKIP_BOOT=1`) skips both the F6 window and the boot animation.

## **Building it yourself**
```bash
git clone https://github.com/timdem228/styleos-linux.git && cd styleos-linux
dotnet publish styleos.csproj -c Release -o out -p:UseAppHost=false
dotnet out/StyleOS.dll
```
Needs the .NET 8 SDK. `bash tests/smoke.sh <path to StyleOS>` runs the smoke tests (CI runs them on every push to `main`).

## **Branches**
*   `main` - the Linux port + the Linux installer (`install.sh`, `python/`)
*   `termux` - the Termux installer (`termux/install.sh`, `README-TERMUX.md`)

Hope you like it! If you have ideas for new features, text is to me! admin@timd.site

> [!TIP]
> [my web](https://timd.site)  
> [StyleOS for Windows](https://github.com/timdem228/styleos-)  
> [NOT NOT nikwonder](https://github.com/nikwonder)
