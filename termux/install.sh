#!/usr/bin/env bash
# curl -fsSL https://raw.githubusercontent.com/timdem228/styleos-linux/termux/termux/install.sh | bash
set -euo pipefail
PKG_URL="git+https://github.com/timdem228/styleos-linux.git@termux#subdirectory=python"
ROOT="${STYLEOS_INSTALL_ROOT:-$HOME/.styleos-install}"
say() { printf '\033[1;36m:: \033[0m%s\n' "$*"; }

if [ -n "${TERMUX_VERSION:-}" ] || [[ "${PREFIX:-}" == *com.termux* ]]; then
  say "Termux: installing python + git"
  pkg update -y || true
  pkg install -y python git
  pip install --upgrade "$PKG_URL"
else
  SUDO=""; [ "$(id -u)" -ne 0 ] && command -v sudo >/dev/null && SUDO="sudo"
  if ! command -v python3 >/dev/null || ! command -v git >/dev/null || ! python3 -c 'import venv, ensurepip' 2>/dev/null; then
    say "Installing python3 + git"
    if command -v apt-get >/dev/null; then $SUDO apt-get update && $SUDO apt-get install -y python3 python3-venv git
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
  case ":$PATH:" in *":$HOME/.local/bin:"*) ;; *)
    echo 'export PATH="$HOME/.local/bin:$PATH"' >> "$HOME/.bashrc"
    export PATH="$HOME/.local/bin:$PATH";;
  esac
fi

styleos install "$@"
say "Done. Start StyleOS with: styleos run"
