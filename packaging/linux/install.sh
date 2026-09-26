#!/usr/bin/env bash
#
# Installs Enigma git client (Enigma.GitClient) for the current user, into the XDG directories:
#
#   $XDG_DATA_HOME/enigma-git-client                        the application
#   $XDG_BIN_HOME/enigma-git-client                         a symlink to its launcher
#   $XDG_DATA_HOME/applications/enigma-git-client.desktop   the launcher entry
#   $XDG_DATA_HOME/icons/hicolor/<N>x<N>/apps/…png          the icon, six sizes
#
# No root, no sudo, nothing outside $HOME. Run it again to upgrade in place.

set -euo pipefail

readonly APP_ID='enigma-git-client'
readonly APP_EXE='Enigma.GitClient.App'
readonly ICON_SIZES=(16 32 48 64 128 256)

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
readonly script_dir
readonly repo_root="${script_dir}/../.."

data_home=${XDG_DATA_HOME:-$HOME/.local/share}
bin_home=${XDG_BIN_HOME:-$HOME/.local/bin}

from_dir=''
self_contained=true
rid=''

die() {
    printf 'install: %s\n' "$1" >&2
    exit 1
}

step() {
    printf '  %s\n' "$1"
}

usage() {
    cat <<'USAGE'
Usage: install.sh [options]

Installs Enigma git client for the current user. With no options it builds the
application from this repository, bundling the .NET runtime, and installs the result.

Options:
  --from DIR              Install an already-published directory instead of building one.
                          DIR must contain the Enigma.GitClient.App launcher.
  --framework-dependent   Build against an installed .NET 10 runtime instead of bundling
                          one: about 37 MB rather than 116 MB, but the machine then needs
                          the runtime somewhere the application can find it.
  --rid RID               Build for this runtime identifier (default: this machine's).
  -h, --help              Show this and exit.

Where things go (XDG_DATA_HOME and XDG_BIN_HOME are honoured):
  ~/.local/share/enigma-git-client                        the application
  ~/.local/bin/enigma-git-client                          a symlink to its launcher
  ~/.local/share/applications/enigma-git-client.desktop   the launcher entry
  ~/.local/share/icons/hicolor/.../enigma-git-client.png  the icon

Your settings, repositories list, accounts and tokens live in ~/.config/Enigma.GitClient
and are never touched. The application needs git 2.20 or newer on your PATH.
Remove everything again with uninstall.sh, in this same directory.
USAGE
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --from)
            [[ $# -ge 2 ]] || die "--from needs a directory."
            from_dir=$2
            shift 2
            ;;
        --framework-dependent)
            self_contained=false
            shift
            ;;
        --rid)
            [[ $# -ge 2 ]] || die "--rid needs a runtime identifier."
            rid=$2
            shift 2
            ;;
        -h | --help)
            usage
            exit 0
            ;;
        *)
            die "unknown option '$1'. Try --help."
            ;;
    esac
done

# Installing as root would write this user's application into root's home, or — with sudo -E —
# leave root-owned files in a user's. Neither is what anyone means by "install".
[[ ${EUID:-$(id -u)} -ne 0 ]] || die "do not run this as root; it installs into your own home directory."

[[ -n ${data_home} ]] || die "XDG_DATA_HOME is set to an empty value."
[[ -n ${bin_home} ]] || die "XDG_BIN_HOME is set to an empty value."
[[ ${data_home} = /* ]] || die "XDG_DATA_HOME must be an absolute path (got '${data_home}')."
[[ ${bin_home} = /* ]] || die "XDG_BIN_HOME must be an absolute path (got '${bin_home}')."

if [[ -n ${from_dir} ]]; then
    [[ -d ${from_dir} ]] || die "--from '${from_dir}' is not a directory."
    [[ -f "${from_dir}/${APP_EXE}" ]] || die "--from '${from_dir}' holds no ${APP_EXE} launcher."
else
    command -v dotnet > /dev/null 2>&1 || die "dotnet is not on PATH; install the .NET SDK, or use --from."
fi

readonly install_dir="${data_home}/${APP_ID}"
readonly desktop_dir="${data_home}/applications"
readonly icon_root="${data_home}/icons/hicolor"
readonly launcher="${install_dir}/${APP_EXE}"

if [[ -z ${rid} ]]; then
    case "$(uname -m)" in
        x86_64 | amd64) rid='linux-x64' ;;
        aarch64 | arm64) rid='linux-arm64' ;;
        *) die "cannot guess a runtime identifier for '$(uname -m)'; pass --rid yourself." ;;
    esac
fi
readonly rid

# The staging directory is a sibling of the install directory, so swapping it in is a rename on
# the same filesystem rather than a copy — a build that fails leaves the old install untouched.
staging="${data_home}/.${APP_ID}.staging.$$"
previous=''

cleanup() {
    rm -rf -- "${staging}"

    # A swap that got as far as moving the old install aside, then failed: put it back.
    if [[ -n ${previous} && -d ${previous} && ! -e ${install_dir} ]]; then
        mv -- "${previous}" "${install_dir}"
    fi

    [[ -z ${previous} ]] || rm -rf -- "${previous}"
}
trap cleanup EXIT

printf 'Installing Enigma git client\n\n'

# ---------------------------------------------------------------- 1. get the application

if [[ -n ${from_dir} ]]; then
    step "Copying from ${from_dir}"
    mkdir -p -- "${staging}"
    cp -a -- "${from_dir}/." "${staging}/"
else
    if [[ ${self_contained} = true ]]; then
        step "Building for ${rid}, with the .NET runtime bundled"
    else
        step "Building for ${rid}, against an installed .NET runtime"
    fi

    dotnet publish "${repo_root}/src/Enigma.GitClient.App" \
        --configuration Release \
        --runtime "${rid}" \
        --self-contained "${self_contained}" \
        --output "${staging}" \
        --verbosity quiet \
        --nologo

    [[ -f "${staging}/${APP_EXE}" ]] || die "the build produced no ${APP_EXE} launcher."
fi

chmod 0755 -- "${staging}/${APP_EXE}"

# ---------------------------------------------------------------- 2. swap it into place

mkdir -p -- "${data_home}"

if [[ -e ${install_dir} ]]; then
    [[ -d ${install_dir} ]] || die "'${install_dir}' exists and is not a directory; move it aside yourself."

    previous="${install_dir}.previous.$$"
    mv -- "${install_dir}" "${previous}"
    step "Replacing ${install_dir}"
else
    step "Installing into ${install_dir}"
fi

mv -- "${staging}" "${install_dir}"

# ---------------------------------------------------------------- 3. the launcher symlink

mkdir -p -- "${bin_home}"
ln -sfn -- "${launcher}" "${bin_home}/${APP_ID}"
step "Linked ${bin_home}/${APP_ID}"

# ---------------------------------------------------------------- 4. the icons

for size in "${ICON_SIZES[@]}"; do
    source_icon="${script_dir}/icons/${APP_ID}-${size}.png"
    [[ -f ${source_icon} ]] || die "missing icon '${source_icon}'."

    install -D -m 0644 -- "${source_icon}" "${icon_root}/${size}x${size}/apps/${APP_ID}.png"
done
step "Installed ${#ICON_SIZES[@]} icons under ${icon_root}"

# ---------------------------------------------------------------- 5. the desktop entry

template="${script_dir}/${APP_ID}.desktop.in"
[[ -f ${template} ]] || die "missing desktop-entry template '${template}'."

desktop_file="${desktop_dir}/${APP_ID}.desktop"
mkdir -p -- "${desktop_dir}"

# A framework-dependent build finds its runtime through DOTNET_ROOT, /etc/dotnet/install_location
# or /usr/share/dotnet — never through PATH. A runtime installed in $HOME (the dotnet-install
# script's default) is in none of those, so the application runs from a shell that has DOTNET_ROOT
# set and does nothing at all when clicked in the launcher, which starts it from a bare
# environment. Where that is the case, the entry carries the location with it.

exec_value="\"${launcher}\""

# Read from the installed tree rather than from the flag, because --from takes a directory
# somebody else published and says nothing about how: a self-contained publish carries the
# runtime beside the application, a framework-dependent one goes looking for it.
# An `&&` list is the whole statement here, so its failure would trip `set -e`: spelled as an
# `if` rather than the shorter form.
bundles_runtime=false
if [[ -f "${install_dir}/libcoreclr.so" ]]; then
    bundles_runtime=true
fi

if [[ ${bundles_runtime} != true ]] && [[ ! -d /usr/share/dotnet ]] && ! compgen -G '/etc/dotnet/install_location*' > /dev/null; then
    if dotnet_bin=$(command -v dotnet 2>/dev/null); then
        dotnet_root=$(dirname -- "$(readlink -f -- "${dotnet_bin}")")
        exec_value="env DOTNET_ROOT=\"${dotnet_root}\" ${exec_value}"
        step "Pinning DOTNET_ROOT=${dotnet_root} in the entry: the runtime is not where a launcher would look"
    else
        printf '  Warning: this is a framework-dependent build and no .NET runtime was found in a\n'
        printf '           standard location. The launcher entry may do nothing when clicked.\n'
    fi
fi

# Substituted in bash rather than with sed: an installation path is arbitrary text, and there is
# no sed delimiter that a path cannot contain. The launcher is quoted because the Exec key is
# word-split, so a home directory with a space in it would otherwise become two arguments.
while IFS= read -r line; do
    printf '%s\n' "${line//@EXEC@/${exec_value}}"
done < "${template}" > "${desktop_file}"

chmod 0644 -- "${desktop_file}"
step "Wrote ${desktop_file}"

# ---------------------------------------------------------------- 6. tell the desktop about it

# All three are conveniences: the caches rebuild by themselves eventually, and a machine without
# them is not a failed install.
command -v update-desktop-database > /dev/null 2>&1 && update-desktop-database -q "${desktop_dir}" 2>/dev/null || true
command -v gtk-update-icon-cache > /dev/null 2>&1 && gtk-update-icon-cache -q -t -f "${icon_root}" 2>/dev/null || true
command -v kbuildsycoca6 > /dev/null 2>&1 && kbuildsycoca6 --noincremental > /dev/null 2>&1 || true

printf '\nDone. "Enigma git client" is in your application launcher.\n'

case ":${PATH}:" in
    *":${bin_home}:"*) printf 'Run it from a terminal with: %s, or %s <repository> to open one.\n' "${APP_ID}" "${APP_ID}" ;;
    *) printf 'Note: %s is not on your PATH, so the %s command will not be found there.\n' "${bin_home}" "${APP_ID}" ;;
esac

# The client drives the real git, so a machine without one gets an application that can do nothing.
# Said here, where it can still be fixed before the first start, rather than only by the application.
if ! command -v git > /dev/null 2>&1; then
    printf 'Note: git is not on your PATH. The client needs git 2.20 or newer; install it before the first start.\n'
fi
