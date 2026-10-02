#!/usr/bin/env bash
# curl -fsSL https://raw.githubusercontent.com/timdem228/styleos-linux/main/install.sh | bash
# Installs the `styleos` command (Python) and then runs `styleos install`:
# git + .NET 8 SDK, clone timdem228/styleos-linux, dotnet publish. Works on Termux and regular Linux.
#
# Everything lives inside main(): bash reads the whole function before running it, so a
# command that reads stdin (apt/dpkg questions) can't swallow the rest of a piped script.
set -euo pipefail

main() {
  local PKG_URL="git+https://github.com/timdem228/styleos-linux.git@main#subdirectory=python"
  local ROOT="${STYLEOS_INSTALL_ROOT:-$HOME/.styleos-install}"
  local APT_OPTS=(-o Dpkg::Options::=--force-confdef -o Dpkg::Options::=--force-confold)
  say() { printf '\033[1;36m:: \033[0m%s\n' "$*"; }
  export DEBIAN_FRONTEND=noninteractive

  if [ -n "${TERMUX_VERSION:-}" ] || [[ "${PREFIX:-}" == *com.termux* ]]; then
    say "Termux: installing python + git"
    pkg update -y "${APT_OPTS[@]}" </dev/null || true
    pkg install -y "${APT_OPTS[@]}" python git </dev/null
    pip install --upgrade "$PKG_URL" </dev/null
  else
    local SUDO=""
    if [ "$(id -u)" -ne 0 ]; then
      if command -v sudo >/dev/null; then SUDO="sudo"; elif command -v doas >/dev/null; then SUDO="doas"; fi
    fi
    if ! command -v python3 >/dev/null || ! command -v git >/dev/null || ! python3 -c 'import venv, ensurepip' 2>/dev/null; then
      say "Installing python3 + git"
      if command -v apt-get >/dev/null; then
        $SUDO apt-get update && $SUDO env DEBIAN_FRONTEND=noninteractive apt-get install -y "${APT_OPTS[@]}" python3 python3-venv git
      elif command -v dnf >/dev/null; then $SUDO dnf install -y python3 git
      elif command -v pacman >/dev/null; then $SUDO pacman -S --needed --noconfirm python git
      elif command -v zypper >/dev/null; then $SUDO zypper --non-interactive install python3 git
      elif command -v apk >/dev/null; then $SUDO apk add python3 py3-pip git bash
      else echo "install python3 and git yourself, then re-run" >&2; exit 1; fi
    fi
    say "Installing the styleos command into $ROOT/venv"
    mkdir -p "$ROOT" "$HOME/.local/bin"
    python3 -m venv "$ROOT/venv"
    "$ROOT/venv/bin/pip" install --quiet --upgrade pip
    "$ROOT/venv/bin/pip" install --upgrade "$PKG_URL"
    ln -sf "$ROOT/venv/bin/styleos" "$HOME/.local/bin/styleos"
    case ":$PATH:" in
      *":$HOME/.local/bin:"*) ;;
      *)
        local rc
        for rc in "$HOME/.bashrc" "$HOME/.zshrc"; do
          { [ "$rc" = "$HOME/.bashrc" ] || [ -f "$rc" ]; } || continue
          grep -qs 'HOME/.local/bin' "$rc" || echo 'export PATH="$HOME/.local/bin:$PATH"' >> "$rc"
        done
        export PATH="$HOME/.local/bin:$PATH";;
    esac
  fi

  styleos install "$@" </dev/null
  say "Done. Start StyleOS with: styleos run"
}

main "$@"
