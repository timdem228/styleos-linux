"""styleos: install, build, update and run StyleOS from source on Linux and Termux."""
import argparse
import datetime
import json
import os
import platform
import re
import shlex
import shutil
import subprocess
import sys
import urllib.request

from . import __version__

DEFAULT_REPO = "https://github.com/timdem228/styleos-linux.git"
DEFAULT_BRANCH = "main"
DOTNET_CHANNEL = "8.0"
DOTNET_MIN_MAJOR = 8
DOTNET_INSTALL_URL = "https://dot.net/v1/dotnet-install.sh"


class InstallError(Exception):
    pass


def _color(code, text):
    if not sys.stdout.isatty() or os.environ.get("NO_COLOR"):
        return text
    return "\033[%sm%s\033[0m" % (code, text)


def step(msg):
    print(_color("1;36", ":: ") + msg, flush=True)


def ok(msg):
    print(_color("32", "   " + msg), flush=True)


def warn(msg):
    print(_color("33", "!! " + msg), file=sys.stderr, flush=True)


class Ctx:
    def __init__(self, root=None, repo=None, branch=None, dry_run=False):
        root = root or os.environ.get("STYLEOS_INSTALL_ROOT") or os.path.join(os.path.expanduser("~"), ".styleos-install")
        self.root = os.path.abspath(os.path.expanduser(root))
        self.src = os.path.join(self.root, "src")
        self.app = os.path.join(self.root, "app")
        self.local_dotnet = os.path.join(self.root, "dotnet")
        self.state_file = os.path.join(self.root, "state.json")
        self.dry_run = dry_run
        state = self.load_state()
        self.repo = repo or state.get("repo") or os.environ.get("STYLEOS_REPO") or DEFAULT_REPO
        self.branch = branch or state.get("branch") or os.environ.get("STYLEOS_BRANCH") or DEFAULT_BRANCH

    def load_state(self):
        try:
            with open(self.state_file, encoding="utf-8") as f:
                return json.load(f)
        except (OSError, ValueError):
            return {}

    def save_state(self, **values):
        if self.dry_run:
            return
        state = self.load_state()
        state.update(values)
        os.makedirs(self.root, exist_ok=True)
        tmp = self.state_file + ".tmp"
        with open(tmp, "w", encoding="utf-8") as f:
            json.dump(state, f, indent=2)
        os.replace(tmp, self.state_file)

    def run(self, cmd, cwd=None, env=None, check=True):
        print(_color("2", "   $ " + " ".join(shlex.quote(c) for c in cmd)), flush=True)
        if self.dry_run:
            return 0
        try:
            r = subprocess.run(cmd, cwd=cwd, env=env)
        except FileNotFoundError:
            if check:
                raise InstallError("command not found: " + cmd[0])
            return 127
        if check and r.returncode != 0:
            raise InstallError("command failed (%d): %s" % (r.returncode, " ".join(cmd)))
        return r.returncode


def is_termux():
    return bool(os.environ.get("TERMUX_VERSION")) or "com.termux" in os.environ.get("PREFIX", "")


def which(name):
    return shutil.which(name)


def sudo_prefix():
    if hasattr(os, "geteuid") and os.geteuid() == 0:
        return []
    if which("sudo"):
        return ["sudo"]
    if which("doas"):
        return ["doas"]
    return []


PKG_MANAGERS = [
    ("apt-get", ["apt-get", "install", "-y"], {"git": "git", "curl": "curl", "certs": "ca-certificates", "icu": "libicu-dev"}),
    ("dnf", ["dnf", "install", "-y"], {"git": "git", "curl": "curl", "certs": "ca-certificates", "icu": "libicu"}),
    ("yum", ["yum", "install", "-y"], {"git": "git", "curl": "curl", "certs": "ca-certificates", "icu": "libicu"}),
    ("pacman", ["pacman", "-S", "--needed", "--noconfirm"], {"git": "git", "curl": "curl", "certs": "ca-certificates", "icu": "icu"}),
    ("zypper", ["zypper", "--non-interactive", "install"], {"git": "git", "curl": "curl", "certs": "ca-certificates", "icu": "libicu"}),
    ("apk", ["apk", "add"], {"git": "git", "curl": "curl", "certs": "ca-certificates", "icu": "icu-libs"}),
]


def linux_pkg_manager():
    for binary, cmd, names in PKG_MANAGERS:
        if which(binary):
            return binary, cmd, names
    return None, None, None


def has_icu():
    for d in ("/usr/lib", "/usr/lib64", "/lib", "/usr/lib/x86_64-linux-gnu", "/usr/lib/aarch64-linux-gnu",
              "/lib/x86_64-linux-gnu", "/lib/aarch64-linux-gnu", os.path.join(os.environ.get("PREFIX", "/nonexistent"), "lib")):
        try:
            if any(n.startswith("libicuuc.so") for n in os.listdir(d)):
                return True
        except OSError:
            pass
    return False


def dotnet_candidates(ctx):
    seen = []
    root = os.environ.get("DOTNET_ROOT")
    for p in (os.path.join(root, "dotnet") if root else None,
              os.path.join(ctx.local_dotnet, "dotnet"),
              os.path.join(os.path.expanduser("~"), ".dotnet", "dotnet"),
              which("dotnet")):
        if p and p not in seen and os.path.isfile(p) and os.access(p, os.X_OK):
            seen.append(p)
    return seen


def sdk_major(dotnet):
    try:
        out = subprocess.run([dotnet, "--list-sdks"], stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
                             universal_newlines=True, timeout=60).stdout
    except (OSError, subprocess.SubprocessError):
        return 0
    majors = [int(m) for m in re.findall(r"^(\d+)\.\d+\.\d+", out, re.M)]
    return max(majors) if majors else 0


def find_dotnet(ctx):
    for d in dotnet_candidates(ctx):
        if sdk_major(d) >= DOTNET_MIN_MAJOR:
            return d
    return None


def dotnet_env(dotnet):
    env = dict(os.environ)
    if dotnet:
        real = os.path.realpath(dotnet)
        env["DOTNET_ROOT"] = os.path.dirname(real)
        env["PATH"] = os.path.dirname(real) + os.pathsep + env.get("PATH", "")
    env.setdefault("DOTNET_CLI_TELEMETRY_OPTOUT", "1")
    env.setdefault("DOTNET_NOLOGO", "1")
    env.setdefault("DOTNET_SKIP_FIRST_TIME_EXPERIENCE", "1")
    if not is_termux() and not has_icu():
        env.setdefault("DOTNET_SYSTEM_GLOBALIZATION_INVARIANT", "1")
    return env


def install_deps_termux(ctx, need_dotnet):
    want = [] if which("git") else ["git"]
    if need_dotnet:
        want.append("dotnet8.0")
    if not want:
        ok("git and .NET are already installed")
        return
    step("Installing Termux packages: " + " ".join(want))
    ctx.run(["pkg", "update", "-y"], check=False)
    if ctx.run(["pkg", "install", "-y"] + want, check=False) == 0:
        return
    if need_dotnet:
        warn("dotnet8.0 not found in the main repo, enabling the TUR repository")
        ctx.run(["pkg", "install", "-y", "tur-repo"], check=False)
        ctx.run(["pkg", "update", "-y"], check=False)
        if ctx.run(["pkg", "install", "-y"] + want, check=False) == 0:
            return
    raise InstallError(
        "could not install %s with pkg.\n"
        "   Try: pkg upgrade && pkg install tur-repo && pkg install %s\n"
        "   Or use a Debian container: pkg install proot-distro && proot-distro install debian"
        " && proot-distro login debian, then run the installer there." % (" ".join(want), " ".join(want)))


def install_deps_linux(ctx, need_dotnet):
    missing = []
    if not which("git"):
        missing.append("git")
    if need_dotnet and not (which("curl") or which("wget")):
        missing += ["curl", "certs"]
    if need_dotnet and not has_icu():
        missing.append("icu")
    if not missing:
        ok("system packages are already there")
        return
    binary, cmd, names = linux_pkg_manager()
    if not binary:
        if "git" in missing:
            raise InstallError("git is missing and no supported package manager was found - install git and retry")
        warn("no package manager found, skipping: " + ", ".join(missing))
        return
    pkgs = [names[m] for m in missing]
    step("Installing system packages with %s: %s" % (binary, " ".join(pkgs)))
    prefix = sudo_prefix()
    if binary == "apt-get":
        ctx.run(prefix + ["apt-get", "update"], check=False)
    rc = ctx.run(prefix + cmd + pkgs, check=False)
    if rc != 0 and "icu" in missing and len(pkgs) > 1:
        ctx.run(prefix + cmd + [names[m] for m in missing if m != "icu"], check="git" in missing)
    elif rc != 0 and "git" in missing:
        raise InstallError("could not install git with " + binary)


def install_dotnet_linux(ctx):
    step("Installing the .NET %s SDK into %s" % (DOTNET_CHANNEL, ctx.local_dotnet))
    script = os.path.join(ctx.root, "dotnet-install.sh")
    if not ctx.dry_run:
        os.makedirs(ctx.root, exist_ok=True)
        try:
            with urllib.request.urlopen(DOTNET_INSTALL_URL, timeout=60) as r, open(script, "wb") as f:
                f.write(r.read())
        except Exception as e:
            if which("curl"):
                ctx.run(["curl", "-fsSL", DOTNET_INSTALL_URL, "-o", script])
            elif which("wget"):
                ctx.run(["wget", "-qO", script, DOTNET_INSTALL_URL])
            else:
                raise InstallError("could not download dotnet-install.sh: %s" % e)
    ctx.run(["bash", script, "--channel", DOTNET_CHANNEL, "--install-dir", ctx.local_dotnet])
    return os.path.join(ctx.local_dotnet, "dotnet")


def ensure_toolchain(ctx):
    dotnet = find_dotnet(ctx)
    step("Checking build tools (%s)" % ("Termux" if is_termux() else platform.system() + " " + platform.machine()))
    if dotnet:
        ok(".NET SDK %d found: %s" % (sdk_major(dotnet), dotnet))
    if is_termux():
        install_deps_termux(ctx, need_dotnet=dotnet is None)
    else:
        install_deps_linux(ctx, need_dotnet=dotnet is None)
        if dotnet is None:
            dotnet = install_dotnet_linux(ctx)
    if dotnet is None:
        dotnet = find_dotnet(ctx)
    if dotnet is None and ctx.dry_run:
        return "dotnet"
    if dotnet is None:
        raise InstallError(".NET %d SDK is still not available after installing it" % DOTNET_MIN_MAJOR)
    if not ctx.dry_run and not which("git"):
        raise InstallError("git is still not available")
    return dotnet


def fetch_source(ctx):
    if os.path.isdir(os.path.join(ctx.src, ".git")):
        step("Updating source (%s, branch %s)" % (ctx.repo, ctx.branch))
        ctx.run(["git", "-C", ctx.src, "remote", "set-url", "origin", ctx.repo])
        ctx.run(["git", "-C", ctx.src, "fetch", "origin", "+refs/heads/%s:refs/remotes/origin/%s" % (ctx.branch, ctx.branch)])
        ctx.run(["git", "-C", ctx.src, "checkout", "-q", "-B", ctx.branch, "origin/" + ctx.branch])
        ctx.run(["git", "-C", ctx.src, "reset", "-q", "--hard", "origin/" + ctx.branch])
    else:
        step("Downloading source from " + ctx.repo)
        if not ctx.dry_run and os.path.exists(ctx.src):
            shutil.rmtree(ctx.src)
        if not ctx.dry_run:
            os.makedirs(ctx.root, exist_ok=True)
        ctx.run(["git", "clone", "--branch", ctx.branch, ctx.repo, ctx.src])
    if ctx.dry_run:
        return "dry-run"
    return subprocess.run(["git", "-C", ctx.src, "rev-parse", "--short", "HEAD"], stdout=subprocess.PIPE,
                          universal_newlines=True).stdout.strip()


def find_project(ctx):
    preferred = os.path.join(ctx.src, "styleos.csproj")
    if ctx.dry_run or os.path.isfile(preferred):
        return preferred
    for name in sorted(os.listdir(ctx.src)):
        if name.endswith(".csproj"):
            return os.path.join(ctx.src, name)
    raise InstallError("no .csproj found in " + ctx.src + " - is this the StyleOS repository/branch?")


def build(ctx, dotnet):
    project = find_project(ctx)
    staging = ctx.app + ".new"
    step("Building StyleOS (dotnet publish, first build takes a few minutes)")
    if not ctx.dry_run and os.path.exists(staging):
        shutil.rmtree(staging)
    ctx.run([dotnet, "publish", project, "-c", "Release", "-o", staging, "-p:UseAppHost=false", "-nologo"],
            cwd=ctx.src, env=dotnet_env(dotnet))
    if ctx.dry_run:
        return
    if not os.path.isfile(os.path.join(staging, "StyleOS.dll")):
        raise InstallError("build finished but StyleOS.dll is missing in " + staging)
    old = ctx.app + ".old"
    if os.path.exists(old):
        shutil.rmtree(old)
    if os.path.exists(ctx.app):
        os.rename(ctx.app, old)
    os.rename(staging, ctx.app)
    shutil.rmtree(old, ignore_errors=True)
    ok("built into " + ctx.app)


def cmd_install(ctx, args):
    dotnet = ensure_toolchain(ctx)
    commit = fetch_source(ctx)
    build(ctx, dotnet)
    ctx.save_state(repo=ctx.repo, branch=ctx.branch, commit=commit, dotnet=dotnet,
                   installer=__version__, built_at=datetime.datetime.now().isoformat(timespec="seconds"))
    step(_color("1;32", "StyleOS is installed (%s@%s). Start it with: styleos run" % (ctx.branch, commit)))
    if not which("styleos"):
        warn("'styleos' is not on PATH yet - add ~/.local/bin to PATH or run: python3 -m styleos_installer run")
    return 0


def cmd_update(ctx, args):
    if not os.path.isdir(os.path.join(ctx.src, ".git")):
        warn("StyleOS is not installed yet, running a full install")
    return cmd_install(ctx, args)


def cmd_run(ctx, args):
    dll = os.path.join(ctx.app, "StyleOS.dll")
    if not os.path.isfile(dll) and not ctx.dry_run:
        warn("StyleOS is not built yet - run: styleos install")
        return 1
    dotnet = ctx.load_state().get("dotnet")
    if not dotnet or not os.path.isfile(dotnet):
        dotnet = find_dotnet(ctx) or (dotnet_candidates(ctx) or [None])[0]
    if not dotnet:
        warn("dotnet was not found - run: styleos install")
        return 1
    extra = list(args.args)
    if extra[:1] == ["--"]:
        extra = extra[1:]
    env = dotnet_env(dotnet)
    env["STYLEOS_SOURCE_DIR"] = ctx.src
    env["STYLEOS_INSTALL_DIR"] = ctx.app
    env["STYLEOS_INSTALLER"] = "python"
    cmd = [dotnet, dll] + extra
    if ctx.dry_run:
        print("   $ " + " ".join(shlex.quote(c) for c in cmd))
        return 0
    sys.stdout.flush()
    os.execve(cmd[0], cmd, env)


def cmd_doctor(ctx, args):
    state = ctx.load_state()
    dotnet = find_dotnet(ctx)
    rows = [
        ("installer", __version__),
        ("platform", "Termux" if is_termux() else "%s %s" % (platform.system(), platform.machine())),
        ("python", sys.version.split()[0]),
        ("git", which("git") or "MISSING"),
        (".NET SDK", "%s (%d)" % (dotnet, sdk_major(dotnet)) if dotnet else "MISSING (styleos install gets it)"),
        ("libicu", "yes" if has_icu() else "no (invariant mode)"),
        ("root", ctx.root),
        ("source", "%s  %s@%s" % (ctx.src, state.get("branch", "-"), state.get("commit", "-"))
         if os.path.isdir(ctx.src) else "not downloaded"),
        ("build", ctx.app if os.path.isfile(os.path.join(ctx.app, "StyleOS.dll")) else "not built"),
        ("repo", ctx.repo),
    ]
    for k, v in rows:
        print("%-10s %s" % (k, v))
    return 0 if dotnet and os.path.isfile(os.path.join(ctx.app, "StyleOS.dll")) else 1


def cmd_uninstall(ctx, args):
    targets = [ctx.root]
    if args.purge:
        data = os.environ.get("STYLEOS_HOME") or os.path.join(
            os.environ.get("XDG_DATA_HOME") or os.path.join(os.path.expanduser("~"), ".local", "share"), "styleos")
        targets.append(data)
    if not args.yes:
        answer = input("Remove %s? [y/N] " % ", ".join(targets)).strip().lower()
        if answer not in ("y", "yes", "д", "да"):
            print("cancelled")
            return 1
    for t in targets:
        if os.path.exists(t):
            step("Removing " + t)
            if not ctx.dry_run:
                shutil.rmtree(t)
    if not args.purge:
        ok("your StyleOS users and files were kept (use --purge to remove them too)")
    return 0


def build_parser():
    p = argparse.ArgumentParser(prog="styleos", description="Install, build and run StyleOS on Linux / Termux.")
    p.add_argument("--version", action="version", version="styleos-installer " + __version__)
    p.add_argument("--dir", help="install root (default ~/.styleos-install or $STYLEOS_INSTALL_ROOT)")
    p.add_argument("--dry-run", action="store_true", help="print the commands without running them")
    sub = p.add_subparsers(dest="command")
    for name, helptext in (("install", "download build tools + source and build StyleOS"),
                           ("update", "pull the latest source and rebuild")):
        s = sub.add_parser(name, help=helptext)
        s.add_argument("--branch", help="git branch to build (default main)")
        s.add_argument("--repo", help="git URL or local path (default %s)" % DEFAULT_REPO)
        s.add_argument("--dir", dest="sub_dir", help=argparse.SUPPRESS)
        s.add_argument("--dry-run", dest="sub_dry", action="store_true", help=argparse.SUPPRESS)
    r = sub.add_parser("run", help="start StyleOS (extra arguments go to StyleOS)")
    r.add_argument("args", nargs=argparse.REMAINDER)
    sub.add_parser("doctor", help="show what is installed")
    u = sub.add_parser("uninstall", help="remove the StyleOS build")
    u.add_argument("-y", "--yes", action="store_true")
    u.add_argument("--purge", action="store_true", help="also delete StyleOS users/files")
    return p


def main(argv=None):
    parser = build_parser()
    argv = list(sys.argv[1:] if argv is None else argv)
    # `styleos run -c "ls"`: everything after "run" belongs to StyleOS
    passthrough = None
    i = 0
    while i < len(argv):
        tok = argv[i]
        if tok == "--dir":
            i += 2
            continue
        if tok.startswith("-"):
            i += 1
            continue
        if tok == "run":
            passthrough = argv[i + 1:]
            argv = argv[:i + 1]
        break
    args = parser.parse_args(argv)
    if passthrough is not None:
        args.args = passthrough
    if not args.command:
        parser.print_help()
        return 0
    root = getattr(args, "sub_dir", None) or args.dir
    dry = args.dry_run or getattr(args, "sub_dry", False)
    ctx = Ctx(root=root, repo=getattr(args, "repo", None), branch=getattr(args, "branch", None), dry_run=dry)
    handlers = {"install": cmd_install, "update": cmd_update, "run": cmd_run,
                "doctor": cmd_doctor, "uninstall": cmd_uninstall}
    try:
        return handlers[args.command](ctx, args) or 0
    except InstallError as e:
        warn(str(e))
        return 1
    except KeyboardInterrupt:
        warn("interrupted")
        return 130


if __name__ == "__main__":
    sys.exit(main())
