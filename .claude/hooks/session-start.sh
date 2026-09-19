#!/usr/bin/env bash
# Watis World - SessionStart toolchain hook (inherited from Sail POOL-0, 2026-08-20).
#
# Why this exists: agents dispatched into cloud sessions kept reporting "no dotnet, no
# Godot" and routing around the gap. The durable fix is not documentation an agent might
# skip - it is making the toolchain present before any agent looks. This hook installs
# dotnet-sdk-8.0 from the main Ubuntu archives and fetches the Godot 4.7 mono headless
# Linux build, exporting SAIL_GODOT (the variable tests/_Common.ps1 Resolve-GodotExe
# already honours).
#
# HARD RULE: this hook FAILS SOFT. A missing tool is a loud one-line warning and exit 0.
# A hook that hard-fails a session is worse than the gap it fixes. Therefore no `set -e`.
set -uo pipefail

log() { printf 'watis-hook: %s\n' "$*"; }
warn() { printf 'watis-hook: WARNING: %s\n' "$*" >&2; }

# ---------------------------------------------------------------------------
# Cloud only. Local sessions are Windows worktrees with their own toolchain and
# PowerShell; apt has no meaning there. SAIL_HOOK_FORCE=1 overrides for testing.
# ---------------------------------------------------------------------------
if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ] && [ "${SAIL_HOOK_FORCE:-}" != "1" ]; then
  log "not a remote session (CLAUDE_CODE_REMOTE != true) - nothing to do."
  exit 0
fi

SUDO=""
if [ "$(id -u)" -ne 0 ]; then
  command -v sudo >/dev/null 2>&1 && SUDO="sudo"
fi

# ---------------------------------------------------------------------------
# 1. dotnet SDK 8.0 - apt, main archives only. PPAs are blocked in this container.
# ---------------------------------------------------------------------------
if dotnet --version >/dev/null 2>&1; then
  log "dotnet already present: $(dotnet --version 2>/dev/null)"
else
  log "installing dotnet-sdk-8.0 from the Ubuntu archives (this is the slow first run)..."
  export DEBIAN_FRONTEND=noninteractive
  if ! $SUDO apt-get install -y --no-install-recommends dotnet-sdk-8.0 >/tmp/watis-hook-apt.log 2>&1; then
    log "first apt-get install failed; refreshing package lists and retrying once..."
    $SUDO apt-get update >>/tmp/watis-hook-apt.log 2>&1
    $SUDO apt-get install -y --no-install-recommends dotnet-sdk-8.0 >>/tmp/watis-hook-apt.log 2>&1
  fi
  if dotnet --version >/dev/null 2>&1; then
    log "dotnet installed: $(dotnet --version 2>/dev/null)"
  else
    warn "dotnet-sdk-8.0 could not be installed (see /tmp/watis-hook-apt.log). BLOCKS: 'dotnet build', 'dotnet test tests/unit/SailNet.Tests.csproj', and every C# task in this repo."
  fi
fi

# Keep first-run build output quiet and telemetry off; harmless if dotnet is absent.
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

# ---------------------------------------------------------------------------
# 2. Godot 4.7 mono, headless Linux. Cached OUTSIDE the repo so it never appears
#    in `git status` and never gets committed.
#    Asset spelling matters: the extracted FOLDER uses underscores, the BINARY
#    inside it uses a dot before x86_64. Both spellings are load-bearing.
# ---------------------------------------------------------------------------
GODOT_VERSION="${GODOT_VERSION:-4.7-stable}"
GODOT_ASSET="${GODOT_ASSET:-Godot_v${GODOT_VERSION}_mono_linux_x86_64}"
GODOT_URL="https://github.com/godotengine/godot/releases/download/${GODOT_VERSION}/${GODOT_ASSET}.zip"
TOOLCHAIN_DIR="${WATIS_TOOLCHAIN_DIR:-$HOME/.watis-toolchain}"
GODOT_BIN="${TOOLCHAIN_DIR}/${GODOT_ASSET}/Godot_v${GODOT_VERSION}_mono_linux.x86_64"

if [ -x "$GODOT_BIN" ]; then
  log "Godot already present: $GODOT_BIN"
else
  mkdir -p "$TOOLCHAIN_DIR"
  # Probe before committing to a ~100MB download: an unpublished release is a 404,
  # not a reason to fail the session.
  if curl -fsIL -m 30 "$GODOT_URL" >/dev/null 2>&1; then
    log "downloading Godot ${GODOT_VERSION} mono (linux x86_64)..."
    if curl -fL -m 900 --retry 2 -o "${TOOLCHAIN_DIR}/godot.zip" "$GODOT_URL" >/dev/null 2>&1 \
       && unzip -q -o "${TOOLCHAIN_DIR}/godot.zip" -d "$TOOLCHAIN_DIR"; then
      rm -f "${TOOLCHAIN_DIR}/godot.zip"
      chmod +x "$GODOT_BIN" 2>/dev/null
      if [ -x "$GODOT_BIN" ]; then
        log "Godot unpacked: $GODOT_BIN"
      else
        warn "Godot archive unpacked but the expected binary is missing at $GODOT_BIN. BLOCKS: headless import and the scene suites."
      fi
    else
      rm -f "${TOOLCHAIN_DIR}/godot.zip"
      warn "Godot download or unpack failed for $GODOT_URL. BLOCKS: headless import and the scene suites. Cloud C# work via 'dotnet test' is unaffected."
    fi
  else
    warn "Godot ${GODOT_VERSION} asset not reachable at $GODOT_URL (404 or network). BLOCKS: headless import and the scene suites. Cloud C# work via 'dotnet test' is unaffected."
  fi
fi

if [ -x "$GODOT_BIN" ]; then
  export SAIL_GODOT="$GODOT_BIN"
  if [ -n "${CLAUDE_ENV_FILE:-}" ]; then
    printf 'export SAIL_GODOT=%s\n' "$GODOT_BIN" >> "$CLAUDE_ENV_FILE"
    printf 'export DOTNET_CLI_TELEMETRY_OPTOUT=1\n' >> "$CLAUDE_ENV_FILE"
    printf 'export DOTNET_NOLOGO=1\n' >> "$CLAUDE_ENV_FILE"
  fi
  log "SAIL_GODOT=$GODOT_BIN"
fi

# ---------------------------------------------------------------------------
# 3. The PowerShell truth, printed where an agent will actually hit it.
# ---------------------------------------------------------------------------
log "PowerShell is NOT available in this container and is not in the Ubuntu archives."
log "  * tests/Run-AllTests.ps1 CANNOT run in cloud. Do not improvise a substitute."
log "  * The cloud test path is: dotnet test tests/unit/SailNet.Tests.csproj"
log "  * The Godot scene suites are a LOCAL-agent step, not a cloud one."
log "  * Blender is not installable here - .blend authoring is a local packet."
log "done."
exit 0
