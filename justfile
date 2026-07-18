# DTXManiaCX task runner. Run `just` (no args) to list recipes.
# Install: https://github.com/casey/just  (brew install just)

mac_project     := "DTXMania.Game/DTXMania.Game.Mac.csproj"
windows_project := "DTXMania.Game/DTXMania.Game.Windows.csproj"
mac_test        := "DTXMania.Test/DTXMania.Test.Mac.csproj"
windows_test    := "DTXMania.Test/DTXMania.Test.csproj"
e2e_project     := "DTXMania.E2E/DTXMania.E2E.csproj"
mcp_project     := "MCP/MCP.csproj"

# List available recipes (default)
default:
    @just --list

# ── CX Neon skin (local install for config switcher) ──────────

# Symlink System/CXNeon into app-data System/ so the in-game Skin dropdown
# lists "CXNeon". Regenerating Graphics/ under the worktree is live.
# Optional: just install-cx-neon activate=true  to also set SkinPath in Config.ini
install-cx-neon activate="false":
    #!/usr/bin/env bash
    set -euo pipefail
    repo_skin="$(pwd)/System/CXNeon"
    if [[ ! -d "$repo_skin/Graphics" ]]; then
      echo "error: $repo_skin/Graphics missing — run skingen first" >&2
      exit 1
    fi
    if [[ "{{ os() }}" == "windows" ]]; then
      app_system="${LOCALAPPDATA}/DTXManiaCX/System"
    else
      app_system="${HOME}/Library/Application Support/DTXManiaCX/System"
    fi
    mkdir -p "$app_system"
    target="$app_system/CXNeon"
    if [[ -e "$target" && ! -L "$target" ]]; then
      echo "error: $target exists and is not a symlink — move it aside first" >&2
      exit 1
    fi
    rm -f "$target"
    ln -sfn "$repo_skin" "$target"
    echo "installed: $target -> $repo_skin"
    if [[ "{{ activate }}" == "true" ]]; then
      config="$(dirname "$app_system")/Config.ini"
      skin_path="$target/"
      # Escape \, &, and | so Windows paths survive sed replacement unchanged.
      esc_skin_path=$(printf '%s' "$skin_path" | sed -e 's/\\/\\\\/g' -e 's/&/\\&/g' -e 's/|/\\|/g')
      if [[ -f "$config" ]]; then
        if grep -q '^SkinPath=' "$config"; then
          # portable in-place edit
          tmp="$(mktemp)"
          sed "s|^SkinPath=.*|SkinPath=${esc_skin_path}|" "$config" > "$tmp" && mv "$tmp" "$config"
        else
          printf '\nSkinPath=%s\n' "$skin_path" >> "$config"
        fi
        if grep -q '^LastUsedSkin=' "$config"; then
          tmp="$(mktemp)"
          sed "s|^LastUsedSkin=.*|LastUsedSkin=CXNeon|" "$config" > "$tmp" && mv "$tmp" "$config"
        else
          printf '\nLastUsedSkin=CXNeon\n' >> "$config"
        fi
        echo "activated: SkinPath=$skin_path in $config"
      else
        echo "warning: $config not found — launch the game once to create it, then re-run with activate=true" >&2
      fi
    fi

# ── Run the game ──────────────────────────────────────────────

# Run the game on the current platform (macOS/Linux -> Mac, Windows -> Windows)
run:
    dotnet run --project {{ if os() == "windows" { windows_project } else { mac_project } }}

# Run the Mac game (explicit)
mac:
    dotnet run --project {{ mac_project }}

# Run the Windows game
windows:
    dotnet run --project {{ windows_project }}

# ── Build ─────────────────────────────────────────────────────

# Build the game on the current platform
build:
    dotnet build {{ if os() == "windows" { windows_project } else { mac_project } }}

# Build the Mac game
build-mac:
    dotnet build {{ mac_project }}

# Build the Windows game
build-windows:
    dotnet build {{ windows_project }}

# ── Test ──────────────────────────────────────────────────────

# Run tests on the current platform
test:
    dotnet test {{ if os() == "windows" { windows_test } else { mac_test } }}

# Run the Mac test suite (Mac-safe subset)
test-mac:
    dotnet test {{ mac_test }}

# Run the full Windows test suite
test-windows:
    dotnet test {{ windows_test }}

# Run tests filtered by an xUnit trait, e.g. just test-trait Category=Audio
test-trait filter:
    dotnet test {{ mac_test }} --filter "{{ filter }}"

# Run the Mac tests with code coverage (results in ./TestResults)
test-cov:
    dotnet test {{ mac_test }} --collect:"XPlat Code Coverage" --settings coverlet.runsettings --results-directory ./TestResults

# ── E2E (the E2E project targets net8.0-windows7.0, so Windows-only) ──

# E2E support tests (in-process harness, no game launch)
e2e-support:
    dotnet test {{ e2e_project }} --filter "Category=E2E-Support"

# Full gameplay E2E smoke (launches the game out-of-process; Windows only).
# Override the game project, e.g. just e2e DTXMania.Game/DTXMania.Game.Windows.csproj
e2e game_project=windows_project:
    DTXMANIA_E2E_GAME_PROJECT={{ game_project }} dotnet test {{ e2e_project }} --filter "Category=E2E"

# ── MCP server ────────────────────────────────────────────────

# Run the MCP server
mcp:
    dotnet run --project {{ mcp_project }}

# Run the MCP server in test mode (no MCP client)
mcp-test:
    dotnet run --project {{ mcp_project }} -- --test

# ── Misc ──────────────────────────────────────────────────────

# Restore all NuGet packages (both game projects)
restore:
    dotnet restore {{ mac_project }} && dotnet restore {{ windows_project }}

# Clean build outputs (both game projects)
clean:
    dotnet clean {{ mac_project }} && dotnet clean {{ windows_project }}

# Format code per .editorconfig
format:
    dotnet format
