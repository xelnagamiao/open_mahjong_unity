#!/bin/sh
set -eu
umask 022

usage() {
    cat <<'EOF'
Usage: sh install.sh [--plan|--install|--check|--preview|--enable] [--root EXISTING_STAGING_DIR]
  --plan     Show targets; no writes (default).
  --install  Install and verify files; do not start or enable the timer.
  --check    Compare installed files to this checkout.
  --preview  Run the installed cleaner as postgres, without --apply.
  --enable   Verify, preview, then enable and start the persistent timer.
             A missed scheduled run may execute immediately.
  --root     Test file installation only; never contact systemd or the live PG.
EOF
}
die() { printf '%s\n' "$*" >&2; exit 1; }
mode=${1:---plan}
case "$mode" in --help|-h) usage; exit 0;; --plan|--install|--check|--preview|--enable) :;; *) usage >&2; exit 2;; esac
if [ "$#" -gt 0 ]; then shift; fi
prefix=
if [ "$#" -ne 0 ]; then
    [ "$#" -eq 2 ] && [ "$1" = --root ] || die 'Expected --root EXISTING_STAGING_DIR'
    case "$2" in /*) :;; *) die 'Staging directory must be absolute';; esac
    prefix=$(CDPATH= cd -P -- "$2" && pwd -P) || die 'Staging directory does not exist'
    [ "$prefix" != / ] && [ "$prefix" = "$2" ] || die 'Use a real, canonical staging directory; never /'
    case "$mode" in --preview|--enable) die 'Staging mode cannot run or enable cleanup';; esac
fi
src=$(CDPATH= cd -P -- "$(dirname -- "$0")" && pwd -P)
name=open-mahjong-pg-log-retention
lib=$prefix/usr/local/libexec/open-mahjong
units=$prefix/etc/systemd/system
py=prune_pg_logs.py
if [ "$mode" = --plan ]; then
    printf '%s\n' "$lib/$py" "$units/$name.service" "$units/$name.timer" 'No files changed. Timer not started.'
    exit 0
fi
for file in "$py" "$name.service" "$name.timer"; do
    [ -f "$src/$file" ] && [ ! -L "$src/$file" ] || die "Missing or indirect source: $file"
done
[ "$(readlink -m -- "$lib")" = "$lib" ] && [ "$(readlink -m -- "$units")" = "$units" ] ||
    die 'Indirect installation path'
dest() { case "$1" in "$py") printf '%s\n' "$lib/$1";; *) printf '%s\n' "$units/$1";; esac; }
matches() {
    path=$(dest "$1")
    [ -f "$path" ] && [ ! -L "$path" ] && cmp -s -- "$src/$1" "$path" || return 1
    if [ -z "$prefix" ]; then [ "$(stat -c '%u:%g:%a' -- "$path")" = 0:0:644 ] || return 1; fi
}
check_all() {
    for file in "$py" "$name.service" "$name.timer"; do matches "$file" || die "Not installed as expected: $file"; done
}
if [ "$mode" = --check ]; then check_all; printf '%s\n' 'Installed files match.'; exit 0; fi
if [ -z "$prefix" ]; then
    [ "$(id -u)" -eq 0 ] && [ "$(uname -s)" = Linux ] || die 'Host changes require a Linux administrator'
fi
if [ "$mode" = --install ]; then
    if [ -z "$prefix" ]; then
        /usr/bin/python3 -IB -c 'import sys; p=sys.argv[1]; compile(open(p,"rb").read(),p,"exec")' "$src/$py"
        systemd-analyze verify "$src/$name.service" "$src/$name.timer"
        if systemctl is-active --quiet "$name.timer" || systemctl is-active --quiet "$name.service"; then
            check_all
            printf '%s\n' 'Installed files already match. Active schedule unchanged.'
            exit 0
        fi
    fi
    mkdir -p -- "$lib" "$units"
    if [ -z "$prefix" ]; then
        # postgres may execute the cleaner, but must never be able to replace it.
        for dir in "$lib" "$(dirname -- "$lib")" /usr/local /usr "$units" /etc/systemd /etc; do
            [ "$(stat -c %u -- "$dir")" = 0 ] && [ -z "$(find "$dir" -maxdepth 0 -perm /022 -print)" ] || die "Unsafe owner/mode: $dir"
        done
    fi
    temp=
    trap 'if [ -n "$temp" ]; then rm -f -- "$temp"; fi' EXIT
    trap 'exit 1' HUP INT TERM
    for file in "$py" "$name.service" "$name.timer"; do
        path=$(dest "$file")
        if [ -L "$path" ] || { [ -e "$path" ] && [ ! -f "$path" ]; }; then die "Unsafe destination: $path"; fi
        if matches "$file"; then continue; fi
        temp=$(mktemp -- "$path.tmp.XXXXXXXX")
        if [ -n "$prefix" ]; then install -m 0644 -- "$src/$file" "$temp"
        else install -o root -g root -m 0644 -- "$src/$file" "$temp"; fi
        cmp -s -- "$src/$file" "$temp" || die "Copy failed: $file"
        mv -fT -- "$temp" "$path"; temp=
    done
    check_all
    if [ -z "$prefix" ]; then systemctl daemon-reload; fi
    printf '%s\n' 'Installed and verified. This action did not enable the timer or run cleanup.'
    exit 0
fi
check_all
for type in service timer; do
    [ "$(systemctl show -p FragmentPath --value "$name.$type")" = "$units/$name.$type" ] &&
        [ -z "$(systemctl show -p DropInPaths --value "$name.$type")" ] || die "Unexpected unit or override: $name.$type"
done
/usr/sbin/runuser -u postgres -- /usr/bin/python3 -IB "$lib/$py" --log-dir /www/server/pgsql/logs --data-dir /www/server/pgsql/data
if [ "$mode" = --enable ]; then systemctl enable --now "$name.timer"; systemctl is-active "$name.timer"; fi
