#!/usr/bin/env bash
# StyleOS Linux build - smoke tests. Every StyleOS command group gets exercised through
# the non-interactive --exec mode, plus one full boot + login + shell session over a pipe.
#
# Usage: bash tests/smoke.sh <path to the StyleOS binary | StyleOS.dll>
set -u

BIN="${1:-out/linux-x64/StyleOS}"
BIN="$(cd "$(dirname "$BIN")" && pwd)/$(basename "$BIN")"
if [[ "$BIN" == *.dll ]]; then RUN=(dotnet "$BIN"); else RUN=("$BIN"); fi

export STYLEOS_HOME="$(mktemp -d)"
WORK="$(mktemp -d)"
cd "$WORK" || exit 1

PASS=0; FAIL=0; FAILED=()

report() { # name ok(0|1) details
  if [[ "$2" == 0 ]]; then PASS=$((PASS+1)); printf 'PASS  %s\n' "$1"
  else FAIL=$((FAIL+1)); FAILED+=("$1"); printf 'FAIL  %s\n%s\n' "$1" "$3"; fi
}

# t NAME EXPECTED_SUBSTRING COMMAND [STDIN]
t() {
  local name="$1" expect="$2" cmd="$3" input="${4:-}" out code
  out="$(printf '%b' "$input" | timeout 120 "${RUN[@]}" -c "$cmd" 2>&1)"; code=$?
  if [[ "$out" == *"$expect"* ]]; then report "$name" 0
  else report "$name" 1 "    cmd:      $cmd
    expected: $(printf '%q' "$expect")
    exit:     $code
    output:   $(printf '%s' "$out" | head -c 900)"; fi
}

# tc NAME EXPECTED_EXIT_CODE COMMAND
tc() {
  local name="$1" want="$2" cmd="$3" out code
  out="$(timeout 60 "${RUN[@]}" -c "$cmd" </dev/null 2>&1)"; code=$?
  if [[ "$code" == "$want" ]]; then report "$name" 0
  else report "$name" 1 "    cmd: $cmd
    wanted exit $want, got $code
    output: $(printf '%s' "$out" | head -c 400)"; fi
}

echo "== StyleOS smoke tests: ${RUN[*]}"
echo "== data dir: $STYLEOS_HOME   work dir: $WORK"

# ---- command line
out="$("${RUN[@]}" --version 2>&1)"; if [[ "$out" == *"1.1.7"* ]]; then report "--version" 0; else report "--version" 1 "    got: $out"; fi
out="$("${RUN[@]}" --help 2>&1)"; if [[ "$out" == *"-c"* ]]; then report "--help" 0; else report "--help" 1 "    got: $out"; fi

# ---- shell
t  "echo"               "hello world"        'echo hello world'
t  "variables"          "tim"                'export NAME=tim; echo $NAME'
t  "\$? after false"    "1"                  'false; echo $?'
t  "&& chain"           "ok"                 'true && echo ok'
t  "|| chain"           "fallback"           'false || echo fallback'
t  "pipe + sort -r"     $'3\n2\n1'           'seq 3 | sort -r'
t  "redirect >"         "hi"                 'echo hi > f.txt; cat f.txt'
t  "redirect >>"        "2"                  'echo a > w.txt; echo b >> w.txt; wc -l w.txt'
t  "redirect <"         "3"                  'seq 3 > n.txt; wc -l < n.txt'
t  "alias ll"           "x1"                 'touch x1; ll'
t  "which (builtin)"    "/usr/bin/ls"        'which ls'
t  "which (host)"       "host system"        'which bash'
t  "command not found"  "command not found"  'definitelynotacommand'
tc "exit code 127"      127                  'definitelynotacommand'
tc "exit code false"    1                    'false'
tc "exit code true"     0                    'true'
t  "help"               "commands available" 'help'
t  "man"                "SYNOPSIS"           'man ls'
t  "yes"                "yes:"               'yes | head -n 1'

# ---- files
t  "mkdir/cd/pwd"       "/d1"                'mkdir d1; cd d1; pwd'
t  "touch/ls"           "a.txt"              'touch a.txt; ls'
t  "cp/mv/cat"          "zzz"                'echo zzz > s.txt; cp s.txt t.txt; mv t.txt u.txt; cat u.txt'
t  "rm"                 "No such file"       'touch gone.txt; rm gone.txt; cat gone.txt'
t  "ls -l real mode"    "-rw"                'touch p1.txt; ls -l p1.txt'
t  "chmod +x"           "-rwx"               'touch run.sh; chmod +x run.sh; ls -l run.sh'
t  "chmod octal"        "0640"               'touch p2.txt; chmod 640 p2.txt; stat p2.txt'
t  "chmod symbolic"     "0600"               'touch p3.txt; chmod 644 p3.txt; chmod go-r p3.txt; stat p3.txt'
t  "ln -s"              "ll.txt ->"          'echo l > lt.txt; ln -s lt.txt ll.txt; ls -l ll.txt'
t  "find -name"         "n1.cs"              'mkdir fd; touch fd/n1.cs; find . -name *.cs'
t  "tree"               "1 files"            'mkdir tr; touch tr/a; tree tr'
t  "file (text)"        "ASCII text"         'echo x > ft.txt; file ft.txt'
t  "file (ELF)"         "ELF"                'file /bin/ls'
t  "basename"           "c.txt"              'basename /a/b/c.txt'
t  "dirname"            "/a/b"               'dirname /a/b/c.txt'

# ---- text
t  "head"               $'1\n2\n3'           'seq 10 | head -n 3'
t  "tail"               $'9\n10'             'seq 10 | tail -n 2'
t  "grep"               "15"                 'seq 20 | grep 5'
t  "grep -v"            $'1\n3'              'seq 3 | grep -v 2'
t  "sed"                "heLLo"              "echo hello | sed 's/l/L/g'"
t  "tr"                 "xyz"                'echo abc | tr abc xyz'
t  "cut"                "b"                  'echo a,b,c | cut -d , -f 2'
t  "uniq -c"            "2 a"                'echo a > u.txt; echo a >> u.txt; echo b >> u.txt; uniq -c u.txt'
t  "rev"                "cba"                'echo abc | rev'
t  "nl"                 "line"               'echo line | nl'
t  "diff"               "b"                  'echo a > d1.txt; echo b > d2.txt; diff d1.txt d2.txt'
t  "tee"                "teeout"             'echo teeout | tee te.txt; cat te.txt'
t  "xxd"                "68"                 'echo -n hi > h.bin; xxd h.bin'

# ---- hashes and archives
t  "sha256sum"          "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad" 'echo -n abc | sha256sum'
t  "md5sum"             "900150983cd24fb0d6963f7d28e17f72" 'echo -n abc | md5sum'
t  "base64"             "aGk="               'echo -n hi | base64'
t  "base64 -d"          "hi"                 'echo aGk= | base64 -d'
t  "zip/unzip"          "qqq"                'echo qqq > z1.txt; zip arc.zip z1.txt; rm z1.txt; unzip arc.zip; cat z1.txt'
t  "gzip/gunzip"        "ggg"                'echo ggg > g.txt; gzip g.txt; gunzip g.txt.gz; cat g.txt'
t  "tar -czf/-xzf"      "ttt"                'mkdir tdir; echo ttt > tdir/f.txt; tar -czf a.tar.gz tdir; rm -r tdir; tar -xzf a.tar.gz; cat tdir/f.txt'
"${RUN[@]}" -c 'mkdir hx; echo hosttar > hx/h.txt; tar -czf host.tar.gz hx' >/dev/null 2>&1
if tar -tzf host.tar.gz 2>/dev/null | grep -q 'hx/h.txt'; then report "tar interop: host tar reads StyleOS archive" 0
else report "tar interop: host tar reads StyleOS archive" 1 "    $(tar -tzf host.tar.gz 2>&1 | head -5)"; fi
tar -czf fromhost.tar.gz hx 2>/dev/null
t  "tar interop: StyleOS reads host archive" "hosttar" 'rm -r hx; tar -xzf fromhost.tar.gz; cat hx/h.txt'

# ---- misc
t  "calc"               "14"                 'calc 2+3*4'
t  "expr"               "2.5"                'expr 10/4'
t  "seq"                $'1\n2\n3'           'seq 3'
t  "date +%Y"           "$(date +%Y)"        'date +%Y'
t  "cal"                "Mo Tu We"           'cal'
t  "cowsay"             "< moo >"            'cowsay moo'
t  "banner"             "H I"                'banner hi'
t  "theme"              "Colors:"            'theme'
t  "version"            "Style OS 1.1.7"     'version'
t  "authors"            "timdem228"          'authors'
t  "sleep"              "slept"              'sleep 0.2; echo slept'
t  "env"                "HOME="              'env'

# ---- system info (/proc + /sys instead of WMI)
t  "uname -a"           "StyleOS"            'uname -a'
t  "fastfetch: kernel"  "StyleOS Core"       'fastfetch'
t  "fastfetch: host os" "Host OS"            'fastfetch'
t  "fastfetch: memory"  "MiB /"              'fastfetch'
t  "free -h"            "Mem:"               'free -h'
t  "lscpu"              "Model name"         'lscpu'
t  "uptime"             "load average"       'uptime'
t  "ps -e"              "PID"                'ps -e'
t  "journalctl"         "EXEC"               'journalctl -n 20'
t  "modules (lsmod)"    "No modules loaded"  'modules'
t  "systemctl"          "styleos-core.service" 'systemctl list-units'

# ---- disks
t  "df -h"              "Mounted on"         'df -h'
t  "df has a real fs"   "%  /"               'df'
t  "mount"              " on / type"         'mount'
t  "lsblk"              "NAME"               'lsblk'

# ---- network
t  "ifconfig"           "lo"                 'ifconfig'
t  "nslookup"           "Address"            'nslookup localhost'
t  "ping"               "received"           'ping -c 1 127.0.0.1'
t  "curl -I"            "HTTP/"              'curl -I https://example.com'
t  "wget"               "Example Domain"     'wget -O ex.html https://example.com; cat ex.html'

# ---- users
t  "whoami"             "root"               'whoami'
t  "id"                 "uid=0(root)"        'id'
t  "useradd"            "bob"                'useradd bob; users' 'pw\n'
out="$(timeout 30 "${RUN[@]}" -u bob -c whoami 2>&1)"; if [[ "$out" == *bob* ]]; then report "--user bob" 0; else report "--user bob" 1 "    got: $out"; fi
t  "sudo (as root)"     "sudo-ok"            'sudo echo sudo-ok'
t  "su"                 "Switched to bob"    'su bob' 'pw\n'
t  "passwd"             "password updated"   'passwd bob' 'np\nnp\n'

# ---- packages and updates
t  "pacman help"        "pacman update"      'pacman help'
t  "pacman -Q"          "No packages installed" 'pacman -Q'
t  "pacman channel"     "Current update channel" 'pacman channel'
t  "whatsnew"           "sorry, there is no such feature" 'whatsnew 0.0.0'

# ---- interactive apps refuse pipes
t  "nano not in a pipe" "cannot be used in a pipe" 'nano x.txt | cat'

# ---- python modules
mkdir -p mod_hello mod_gui mod_dep
cat > mod_hello/setup.module <<'EOF'
{ "name": "hello", "version": "1.0.0", "description": "test module", "entry": "main.py" }
EOF
cat > mod_hello/main.py <<'EOF'
import styleos as s
s.println("hi from", s.user(), s.version())
s.write_file("note.txt", "saved")
s.println("read back:", s.read_file("note.txt"))
EOF
echo '{ "name": "gui", "entry": "main.py" }' > mod_gui/setup.module
echo 'import tkinter' > mod_gui/main.py
echo '{ "name": "dep", "entry": "main.py", "requires": ["six"] }' > mod_dep/setup.module
printf 'import six\nprint("six ok", six.__version__)\n' > mod_dep/main.py

t  "modules install modules" "library is ready" 'modules install modules'
t  "module install + run"    "hi from root 1.1.7" 'module install mod_hello; module run hello'
t  "module data dir"         "read back: saved" 'module run hello'
t  "module list"             "hello"          'module list'
t  "GUI libraries blocked"   "console-only"   'module install mod_gui; module run gui'
t  "pip deps (--target)"     "six ok"         'module install mod_dep; module run dep' 'y\n'
t  "module remove"           "removed"        'module remove hello'

# ---- a whole interactive session over a pipe: F6 window, boot, login, shell, shutdown
out="$(printf 'root\n\nuname -a\necho piped-session-ok\nshutdown\n' | timeout 120 "${RUN[@]}" 2>&1)"
if [[ "$out" == *"Welcome to Style OS"* && "$out" == *"piped-session-ok"* ]]; then report "interactive session (boot + login + shell)" 0
else report "interactive session (boot + login + shell)" 1 "    output tail: $(printf '%s' "$out" | tail -c 1500)"; fi

echo
echo "RESULT: $PASS passed, $FAIL failed"
if (( FAIL > 0 )); then printf 'failed: %s\n' "${FAILED[@]}"; exit 1; fi
