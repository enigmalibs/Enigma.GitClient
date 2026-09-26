#!/usr/bin/env bash
#
# Removes what install.sh wrote, and nothing else.
#
# Your settings, your repositories list, your accounts and their tokens are not part of the
# installation: they live in ~/.config/Enigma.GitClient, and this script never touches them —
# nor any repository you worked on.

set -euo pipefail

readonly APP_ID='enigma-git-client'
readonly APP_EXE='Enigma.GitClient.App'
readonly ICON_SIZES=(16 32 48 64 128 256)

data_home=${XDG_DATA_HOME:-$HOME/.local/share}
bin_home=${XDG_BIN_HOME:-$HOME/.local/bin}
config_home=${XDG_CONFIG_HOME:-$HOME/.config}

die() {
    printf 'uninstall: %s\n' "$1" >&2
    exit 1
}

step() {
    printf '  %s\n' "$1"
}

usage() {
    cat <<'USAGE'
Usage: uninstall.sh [-h|--help]

Removes Enigma git client from the XDG directories install.sh wrote to:
the application, the launcher symlink, the desktop entry and the six icons.

Your settings, accounts and tokens in ~/.config/Enigma.GitClient are left exactly as they are.
USAGE
}

case "${1:-}" in
    '') ;;
    -h | --help)
        usage
        exit 0
        ;;
    *) die "unknown option '$1'. Try --help." ;;
esac

[[ ${EUID:-$(id -u)} -ne 0 ]] || die "do not run this as root; it removes files from your own home directory."

[[ -n ${data_home} ]] || die "XDG_DATA_HOME is set to an empty value."
[[ -n ${bin_home} ]] || die "XDG_BIN_HOME is set to an empty value."
[[ ${data_home} = /* ]] || die "XDG_DATA_HOME must be an absolute path (got '${data_home}')."
[[ ${bin_home} = /* ]] || die "XDG_BIN_HOME must be an absolute path (got '${bin_home}')."

readonly install_dir="${data_home}/${APP_ID}"
readonly desktop_dir="${data_home}/applications"
readonly desktop_file="${desktop_dir}/${APP_ID}.desktop"
readonly icon_root="${data_home}/icons/hicolor"
readonly symlink="${bin_home}/${APP_ID}"

# The one destructive path in this script, spelled out rather than trusted: it is built from
# data_home, so an empty or wrong XDG_DATA_HOME could otherwise point it anywhere.
[[ ${install_dir} = */${APP_ID} ]] || die "refusing to remove '${install_dir}': not an ${APP_ID} directory."

printf 'Removing Enigma git client\n\n'

removed=0

if [[ -d ${install_dir} ]]; then
    rm -rf -- "${install_dir}"
    step "Removed ${install_dir}"
    removed=$((removed + 1))
fi

# Only a symlink, and only one pointing into the install directory: a file of the user's own
# that happens to share the name is not ours to delete.
if [[ -L ${symlink} ]]; then
    target=$(readlink -- "${symlink}")

    if [[ ${target} = "${install_dir}/${APP_EXE}" ]]; then
        rm -f -- "${symlink}"
        step "Removed ${symlink}"
        removed=$((removed + 1))
    else
        printf '  Left %s alone: it points at %s, not at this installation.\n' "${symlink}" "${target}"
    fi
fi

if [[ -f ${desktop_file} ]]; then
    rm -f -- "${desktop_file}"
    step "Removed ${desktop_file}"
    removed=$((removed + 1))
fi

icons_removed=0
for size in "${ICON_SIZES[@]}"; do
    icon="${icon_root}/${size}x${size}/apps/${APP_ID}.png"

    if [[ -f ${icon} ]]; then
        rm -f -- "${icon}"
        icons_removed=$((icons_removed + 1))
    fi
done

if [[ ${icons_removed} -gt 0 ]]; then
    step "Removed ${icons_removed} icons from ${icon_root}"
    removed=$((removed + 1))
fi

command -v update-desktop-database > /dev/null 2>&1 && update-desktop-database -q "${desktop_dir}" 2>/dev/null || true
command -v gtk-update-icon-cache > /dev/null 2>&1 && gtk-update-icon-cache -q -t -f "${icon_root}" 2>/dev/null || true
command -v kbuildsycoca6 > /dev/null 2>&1 && kbuildsycoca6 --noincremental > /dev/null 2>&1 || true

if [[ ${removed} -eq 0 ]]; then
    printf '\nNothing to remove — Enigma git client is not installed under %s.\n' "${data_home}"
else
    printf '\nDone.\n'
fi

printf 'Your settings, accounts and tokens were left untouched, in %s/Enigma.GitClient.\n' "${config_home}"
