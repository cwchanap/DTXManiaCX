#!/usr/bin/env bash
set -euo pipefail
export LC_ALL=C

usage() {
    printf 'usage: benchmark-critical-path.sh prepare-seed GAME_DIR CORPUS RESULT_ROOT\n' >&2
    printf '       benchmark-critical-path.sh matrix GAME_DIR CORPUS RESULT_ROOT\n' >&2
    exit 2
}

fail() {
    printf 'HPA-192 runner error: %s\n' "$*" >&2
    exit 1
}

sha256_file() {
    shasum -a 256 "$1" | awk '{ print $1 }'
}

[[ "$#" -eq 4 ]] || usage
command_name="$1"
[[ "$command_name" == prepare-seed || "$command_name" == matrix ]] || usage

canonical_directory() {
    local path="$1"
    local description="$2"

    [[ -d "$path" ]] || fail "$description directory is missing: $path"
    (
        cd "$path"
        pwd -P
    )
}

canonical_file() {
    local path="$1"
    local description="$2"
    local canonical

    [[ -f "$path" ]] || fail "$description file is missing: $path"
    if ! canonical="$(
        perl -MCwd=realpath -e '
            my $canonical = realpath($ARGV[0]);
            exit 1 unless defined $canonical;
            print $canonical;
        ' "$path"
    )"; then
        fail "$description file could not be canonicalized: $path"
    fi
    [[ -f "$canonical" ]] ||
        fail "$description canonical file is missing: $canonical"
    printf '%s\n' "$canonical"
}

paths_overlap() {
    local first="$1"
    local second="$2"

    [[ "$first" == "$second" ||
       "$first" == "$second/"* ||
       "$second" == "$first/"* ]]
}

path_contains() {
    local parent="$1"
    local child="$2"

    [[ "$child" == "$parent" || "$child" == "$parent/"* ]]
}

script_path="$(canonical_file "${BASH_SOURCE[0]}" runner)"
repo_root="$(canonical_directory "$(dirname "$script_path")/../.." repository)"
game_dir="$(canonical_directory "$2" game)"
corpus="$(canonical_directory "$3" corpus)"
result_root="$(canonical_directory "$4" result-root)"
system_root="$(canonical_directory "$repo_root/System" System)"
committed_corpus_manifest="$(
    canonical_file \
        "$repo_root/docs/performance/HPA-192-corpus-manifest.tsv" \
        corpus-manifest
)"
summarizer="$(
    canonical_file "$repo_root/tools/hpa192/summarize-critical-path.sh" summarizer
)"
game_dll="$(canonical_file "$game_dir/DTXMania.Game.Mac.dll" game-binary)"
fixed_inputs="$result_root/fixed-inputs.txt"

validate_path_relationships() {
    path_contains "$game_dir" "$game_dll" ||
        fail "game binary resolves outside GAME_DIR"
    [[ "$game_dir" == "$result_root/build" ]] ||
        fail "GAME_DIR must be the exact immutable RESULT_ROOT/build child"
    if paths_overlap "$result_root" "$corpus"; then
        fail "RESULT_ROOT overlaps the frozen corpus"
    fi
    if paths_overlap "$result_root" "$system_root"; then
        fail "RESULT_ROOT overlaps the repository System tree"
    fi
    if paths_overlap "$result_root" "$game_dir" &&
       [[ "$game_dir" != "$result_root/build" ]]; then
        fail "GAME_DIR overlaps RESULT_ROOT outside the exact build child"
    fi
}

validate_result_root_phase_layout() {
    local entry
    local name
    local expected_type

    while IFS= read -r -d '' entry; do
        name="${entry##*/}"
        [[ ! -L "$entry" ]] ||
            fail "result-root phase entry may not be a symlink: $name"
        expected_type=
        case "$name" in
            build)
                [[ "$game_dir" == "$result_root/build" &&
                   "$entry" == "$game_dir" ]] ||
                    fail "build is not the exact immutable GAME_DIR child"
                expected_type='directory'
                ;;
            fixed-inputs.txt|environment.txt)
                expected_type='file'
                ;;
            empty-songs|expected-chart-paths|configs|seed)
                [[ "$command_name" == matrix ]] ||
                    fail "unexpected prepare-seed result-root entry: $name"
                expected_type='directory'
                ;;
            empty-manifest.tsv|system-manifest.tsv|corpus-manifest.tsv|fixed-identities.txt)
                [[ "$command_name" == matrix ]] ||
                    fail "unexpected prepare-seed result-root entry: $name"
                expected_type='file'
                ;;
            *)
                fail "unexpected $command_name result-root entry: $name"
                ;;
        esac

        case "$expected_type" in
            directory)
                [[ -d "$entry" ]] ||
                    fail "result-root phase entry is not a directory: $name"
                ;;
            file)
                [[ -f "$entry" ]] ||
                    fail "result-root phase entry is not a file: $name"
                ;;
        esac
    done < <(find "$result_root" -mindepth 1 -maxdepth 1 -print0)
}

validate_path_relationships
validate_result_root_phase_layout

fixed_source_commit=
fixed_game_sha256=

validate_fixed_inputs() {
    local line_count
    local current_source_commit
    local observed_game_sha256

    [[ -f "$fixed_inputs" && ! -L "$fixed_inputs" ]] ||
        fail "Task 10 fixed-inputs.txt is missing or aliased"
    line_count="$(awk 'END { print NR + 0 }' "$fixed_inputs")"
    [[ "$line_count" -eq 2 ]] ||
        fail "Task 10 fixed-inputs.txt must contain exactly two lines"

    if ! current_source_commit="$(
        git -C "$repo_root" rev-parse HEAD 2>/dev/null
    )"; then
        fail "current source commit could not be resolved"
    fi
    observed_game_sha256="$(sha256_file "$game_dll")"
    if ! cmp -s "$fixed_inputs" <(
        printf 'source_commit\t%s\ngame_sha256\t%s\n' \
            "$current_source_commit" \
            "$observed_game_sha256"
    ); then
        fail "Task 10 fixed-inputs.txt is not the exact canonical control"
    fi

    fixed_source_commit="$current_source_commit"
    fixed_game_sha256="$observed_game_sha256"
}

validate_fixed_inputs

lock_path="${TMPDIR:-/tmp}/hpa-192-benchmark-startup.lock"
lock_acquired=false
temporary_root=
game_pid=
game_pid_validated=false
game_pid_sample_state=not_sampled
game_pid_identity_state=not_started
game_exit_code=255
identity_stabilization_us=2000000

release_lock() {
    if [[ "$lock_acquired" == true &&
          "$(readlink "$lock_path" 2>/dev/null || true)" == "$$" ]]; then
        rm -f "$lock_path"
    fi
    lock_acquired=false
}

acquire_lock() {
    local owner_pid

    while true; do
        if ln -s "$$" "$lock_path" 2>/dev/null; then
            lock_acquired=true
            return 0
        fi

        owner_pid="$(readlink "$lock_path" 2>/dev/null || true)"
        if [[ "$owner_pid" =~ ^[0-9]+$ ]] &&
           kill -0 "$owner_pid" 2>/dev/null; then
            printf 'another HPA-192 benchmark invocation holds %s (PID %s)\n' \
                "$lock_path" "$owner_pid" >&2
            return 1
        fi

        if [[ "$(readlink "$lock_path" 2>/dev/null || true)" == "$owner_pid" ]]; then
            printf 'reclaiming stale HPA-192 benchmark lock %s (PID %s)\n' \
                "$lock_path" "${owner_pid:-unknown}" >&2
            rm -f "$lock_path"
        fi
    done
}

sample_game_pid_identity() {
    local actual_pid
    local command_line
    local process_sample
    local process_state

    game_pid_sample_state=invalid_pid
    if [[ ! "$game_pid" =~ ^[1-9][0-9]*$ || "$game_pid" -le 1 ]]; then
        return 1
    fi
    if ! process_sample="$(
        ps -p "$game_pid" -o pid= -o state= -o command= 2>/dev/null
    )"; then
        process_sample=
    fi
    if [[ -z "${process_sample//[[:space:]]/}" ]]; then
        if kill -0 "$game_pid" 2>/dev/null; then
            game_pid_sample_state=transient_ps
        else
            game_pid_sample_state=child_exited
        fi
        return 1
    fi

    read -r actual_pid process_state command_line <<<"$process_sample"
    if [[ "$actual_pid" != "$game_pid" ]]; then
        game_pid_sample_state=identity_mismatch
        return 1
    fi
    if [[ "$process_state" == *Z* ]]; then
        game_pid_sample_state=child_exited
        return 1
    fi
    if [[ "$command_line" != *"$game_dll"* ]]; then
        game_pid_sample_state=pre_exec
        return 1
    fi
    game_pid_sample_state=validated_live
}

wait_for_game() {
    if [[ -z "$game_pid" ]]; then
        return 0
    fi

    if wait "$game_pid"; then
        game_exit_code=0
    else
        game_exit_code=$?
    fi
    game_pid=
    game_pid_validated=false
}

terminate_validated_game() {
    local iteration

    [[ -n "$game_pid" ]] || return 0
    if ! sample_game_pid_identity; then
        if [[ "$game_pid_sample_state" == child_exited ]]; then
            wait_for_game
            return 0
        fi
        printf 'refusing to terminate unvalidated PID %s\n' "$game_pid" >&2
        return 1
    fi
    if [[ "$game_pid_validated" != true ]]; then
        printf 'refusing to terminate unvalidated PID %s\n' "$game_pid" >&2
        return 1
    fi

    kill -TERM "$game_pid"
    for ((iteration = 0; iteration < 40; iteration++)); do
        if ! sample_game_pid_identity; then
            if [[ "$game_pid_sample_state" == child_exited ]]; then
                wait_for_game
                return 0
            fi
        else
            game_pid_validated=true
        fi
        sleep 0.05
    done

    if [[ "$game_pid_validated" == true ]] &&
       sample_game_pid_identity; then
        kill -KILL "$game_pid"
    else
        printf 'refusing to force-kill changed PID %s\n' "$game_pid" >&2
        return 1
    fi
    wait_for_game
}

cleanup() {
    local exit_status=$?
    local temp_prefix="${TMPDIR:-/tmp}/hpa-192-critical-path."

    if [[ -n "$game_pid" ]]; then
        terminate_validated_game || true
    fi
    if [[ -n "$temporary_root" &&
          "$temporary_root" == "$temp_prefix"* &&
          "$temporary_root" != / &&
          -d "$temporary_root" ]]; then
        rm -rf -- "$temporary_root"
    fi
    release_lock
    trap - EXIT HUP INT TERM
    exit "$exit_status"
}

trap cleanup EXIT HUP INT TERM
acquire_lock || exit 1
temporary_root="$(mktemp -d "${TMPDIR:-/tmp}/hpa-192-critical-path.XXXXXX")"

generate_tree_manifest() {
    local root="$1"
    local output="$2"

    perl -MDigest::SHA -MFile::Find -e '
        use strict;
        use warnings;
        use bytes;

        my ($root) = @ARGV;
        my @rows;
        find(
            {
                no_chdir => 1,
                wanted => sub {
                    my $path = $File::Find::name;
                    return if $path eq $root;
                    die "unsupported tree entry: $path\n"
                        if -l $path || (!-d $path && !-f $path);
                    return if -d $path;

                    my $relative = substr($path, length($root) + 1);
                    die "unsafe tree path\n"
                        if index($relative, "\t") >= 0 ||
                           index($relative, "\n") >= 0;
                    open my $handle, "<:raw", $path
                        or die "cannot read $path: $!\n";
                    my $digest = Digest::SHA->new(256);
                    $digest->addfile($handle);
                    close $handle or die "cannot close $path: $!\n";
                    my $size = -s $path;
                    push @rows, [$relative, $size, $digest->hexdigest];
                },
            },
            $root
        );
        for my $row (sort { $a->[0] cmp $b->[0] } @rows) {
            print join("\t", @$row), "\n";
        }
    ' "$root" >"$output" ||
        fail "could not generate a canonical tree manifest"
}

require_empty_directory() {
    local path="$1"
    local entry

    [[ -d "$path" ]] || fail "empty-song directory is missing"
    entry="$(find "$path" -mindepth 1 -print -quit 2>/dev/null || true)"
    [[ -z "$entry" ]] || fail "empty-song directory is not empty"
}

require_exact_directory_entries() {
    local path="$1"
    local description="$2"
    local entry
    local name
    local expected
    local allowed

    shift 2
    [[ -d "$path" && ! -L "$path" ]] ||
        fail "$description directory is missing or aliased"
    while IFS= read -r -d '' entry; do
        name="${entry##*/}"
        [[ ! -L "$entry" ]] ||
            fail "$description contains a symlink entry: $name"
        allowed=false
        for expected in "$@"; do
            if [[ "$name" == "$expected" ]]; then
                allowed=true
                break
            fi
        done
        [[ "$allowed" == true ]] ||
            fail "$description contains an unexpected entry: $name"
    done < <(find "$path" -mindepth 1 -maxdepth 1 -print0)

    for expected in "$@"; do
        [[ -e "$path/$expected" && ! -L "$path/$expected" ]] ||
            fail "$description is missing required entry: $expected"
    done
}

write_fixed_config() {
    local path="$1"
    local song_path="$2"

    {
        printf '%s\n' '[System]'
        printf 'SkinPath=%s\n' "$system_root"
        printf 'DTXPath=%s\n' "$song_path"
        printf '%s\n' '[Skin]'
        printf 'SystemSkinRoot=%s\n' "$system_root"
        printf '%s\n' '[Display]'
        printf '%s\n' \
            'ScreenWidth=1280' \
            'ScreenHeight=720' \
            'FullScreen=False' \
            'VSyncWait=False'
        printf '%s\n' '[Api]' 'EnableGameApi=False'
    } >"$path"
}

config_has_exact_api_disabled() {
    local path="$1"

    [[ -f "$path" ]] || return 1
    [[ "$(
        awk '
            $0 == "[Api]" {
                api_sections++
                in_api = 1
                next
            }
            /^\[/ {
                in_api = 0
            }
            in_api && /^EnableGameApi=/ {
                api_values++
                if ($0 == "EnableGameApi=False") {
                    exact_values++
                }
            }
            END {
                if (api_sections == 1 &&
                    api_values == 1 &&
                    exact_values == 1) {
                    print "yes"
                }
            }
        ' "$path"
    )" == yes ]]
}

write_expected_chart_paths() {
    local manifest="$1"
    local output="$2"

    awk -F '\t' -v root="$corpus" '
        tolower($1) ~ /\.(dtx|gda|g2d|bms|bme|bml)$/ {
            print root "/" $1
        }
    ' "$manifest" |
        LC_ALL=C sort >"$output"
}

current_corpus_manifest="$temporary_root/corpus-manifest.tsv"
current_system_manifest="$temporary_root/system-manifest.tsv"
current_expected_chart_paths="$temporary_root/corpus-chart-paths.txt"
current_empty_chart_paths="$temporary_root/empty-chart-paths.txt"
: >"$current_empty_chart_paths"

generate_tree_manifest "$corpus" "$current_corpus_manifest"
cmp -s "$committed_corpus_manifest" "$current_corpus_manifest" ||
    fail "corpus manifest differs from the committed manifest"

manifest_files="$(awk 'END { print NR + 0 }' "$current_corpus_manifest")"
supported_charts="$(
    awk -F '\t' '
        tolower($1) ~ /\.(dtx|gda|g2d|bms|bme|bml)$/ { count++ }
        END { print count + 0 }
    ' "$current_corpus_manifest"
)"
set_def_files="$(
    awk -F '\t' '
        $1 ~ /(^|\/)SET\.def$/ { count++ }
        END { print count + 0 }
    ' "$current_corpus_manifest"
)"
[[ "$manifest_files" -eq 592 ]] ||
    fail "corpus must contain exactly 592 files"
[[ "$supported_charts" -eq 100 ]] ||
    fail "corpus must contain exactly 100 supported charts"
[[ "$set_def_files" -eq 27 ]] ||
    fail "corpus must contain exactly 27 SET.def files"

generate_tree_manifest "$system_root" "$current_system_manifest"
write_expected_chart_paths \
    "$current_corpus_manifest" \
    "$current_expected_chart_paths"

game_sha256="$(sha256_file "$game_dll")"
[[ "$game_sha256" == "$fixed_game_sha256" ]] ||
    fail "game binary changed after Task 10 fixed-input validation"
runner_sha256="$(sha256_file "$script_path")"
summarizer_sha256="$(sha256_file "$summarizer")"
corpus_manifest_sha256="$(sha256_file "$committed_corpus_manifest")"
corpus_observed_sha256="$(sha256_file "$current_corpus_manifest")"
system_manifest_sha256="$(sha256_file "$current_system_manifest")"

empty_songs="$result_root/empty-songs"
empty_manifest="$result_root/empty-manifest.tsv"
system_manifest="$result_root/system-manifest.tsv"
corpus_manifest="$result_root/corpus-manifest.tsv"
expected_paths_root="$result_root/expected-chart-paths"
expected_corpus_paths="$expected_paths_root/corpus.txt"
expected_empty_paths="$expected_paths_root/empty.txt"
configs_root="$result_root/configs"
config_a="$configs_root/A.Config.ini"
config_b="$configs_root/B.Config.ini"
config_c="$configs_root/C.Config.ini"
common_identity="$result_root/fixed-identities.txt"
seed_root="$result_root/seed"
seed_appdata="$seed_root/appdata"
seed_manifest="$seed_root/manifest.tsv"
seed_identity="$seed_root/identity.txt"
slots_root="$result_root/slots"
accepted_artifacts="$result_root/accepted-artifacts.txt"
decision_path="$result_root/decision.txt"
matrix_identity="$result_root/matrix-identity.txt"

empty_manifest_sha256=
config_a_sha256=
config_b_sha256=
config_c_sha256=
expected_corpus_paths_sha256=
expected_empty_paths_sha256=
seed_manifest_sha256=
seed_observed_sha256=

validate_matrix_control_layout() {
    [[ "$command_name" == matrix ]] || return 0

    require_exact_directory_entries \
        "$expected_paths_root" \
        expected-chart-paths \
        corpus.txt \
        empty.txt
    [[ -f "$expected_corpus_paths" && -f "$expected_empty_paths" ]] ||
        fail "expected-chart-path controls must be regular files"

    require_exact_directory_entries \
        "$configs_root" \
        configs \
        A.Config.ini \
        B.Config.ini \
        C.Config.ini
    [[ -f "$config_a" && -f "$config_b" && -f "$config_c" ]] ||
        fail "scenario configs must be regular files"

    require_exact_directory_entries \
        "$seed_root" \
        seed \
        appdata \
        manifest.tsv \
        identity.txt \
        setup.stdout.log \
        setup.stderr.log \
        setup-process.txt \
        setup-result.txt \
        setup-chart-paths.txt
    [[ -d "$seed_appdata" &&
       -f "$seed_manifest" &&
       -f "$seed_identity" &&
       -f "$seed_root/setup.stdout.log" &&
       -f "$seed_root/setup.stderr.log" &&
       -f "$seed_root/setup-process.txt" &&
       -f "$seed_root/setup-result.txt" &&
       -f "$seed_root/setup-chart-paths.txt" ]] ||
        fail "seed controls have unexpected types"
}

validate_matrix_control_layout

write_common_identity() {
    local path="$1"

    {
        printf 'source_commit\t%s\n' "$fixed_source_commit"
        printf 'game_dir\t%s\n' "$game_dir"
        printf 'corpus\t%s\n' "$corpus"
        printf 'result_root\t%s\n' "$result_root"
        printf 'system_root\t%s\n' "$system_root"
        printf 'game_sha256\t%s\n' "$game_sha256"
        printf 'runner_sha256\t%s\n' "$runner_sha256"
        printf 'summarizer_sha256\t%s\n' "$summarizer_sha256"
        printf 'corpus_manifest_sha256\t%s\n' "$corpus_manifest_sha256"
        printf 'corpus_observed_sha256\t%s\n' "$corpus_observed_sha256"
        printf 'system_manifest_sha256\t%s\n' "$system_manifest_sha256"
        printf 'empty_manifest_sha256\t%s\n' "$empty_manifest_sha256"
        printf 'config_a_sha256\t%s\n' "$config_a_sha256"
        printf 'config_b_sha256\t%s\n' "$config_b_sha256"
        printf 'config_c_sha256\t%s\n' "$config_c_sha256"
        printf 'expected_corpus_paths_sha256\t%s\n' \
            "$expected_corpus_paths_sha256"
        printf 'expected_empty_paths_sha256\t%s\n' \
            "$expected_empty_paths_sha256"
    } >"$path"
}

write_seed_identity() {
    local path="$1"

    {
        printf 'seed_manifest_sha256\t%s\n' "$seed_manifest_sha256"
        printf 'game_sha256\t%s\n' "$game_sha256"
        printf 'runner_sha256\t%s\n' "$runner_sha256"
        printf 'summarizer_sha256\t%s\n' "$summarizer_sha256"
        printf 'corpus_manifest_sha256\t%s\n' "$corpus_manifest_sha256"
        printf 'system_manifest_sha256\t%s\n' "$system_manifest_sha256"
        printf 'config_sha256\t%s\n' "$config_a_sha256"
        printf 'empty_manifest_sha256\t%s\n' "$empty_manifest_sha256"
        printf '%s\n' 'database_charts	100' 'database_songs	27'
    } >"$path"
}

prepare_common_outputs() {
    local owned_path

    for owned_path in \
        "$empty_songs" \
        "$empty_manifest" \
        "$system_manifest" \
        "$corpus_manifest" \
        "$expected_paths_root" \
        "$configs_root" \
        "$common_identity" \
        "$seed_root" \
        "$slots_root" \
        "$accepted_artifacts" \
        "$decision_path" \
        "$matrix_identity"
    do
        [[ ! -e "$owned_path" && ! -L "$owned_path" ]] ||
            fail "runner output namespace is dirty: $owned_path"
    done

    mkdir -p "$empty_songs" "$expected_paths_root" "$configs_root"
    require_empty_directory "$empty_songs"
    : >"$empty_manifest"
    empty_manifest_sha256="$(sha256_file "$empty_manifest")"

    write_fixed_config "$config_a" "$corpus"
    write_fixed_config "$config_b" "$empty_songs"
    cp "$config_a" "$config_c"
    config_a_sha256="$(sha256_file "$config_a")"
    config_b_sha256="$(sha256_file "$config_b")"
    config_c_sha256="$(sha256_file "$config_c")"
    [[ "$config_a_sha256" == "$config_c_sha256" ]] ||
        fail "Scenario A and C configs differ"
    config_has_exact_api_disabled "$config_a" ||
        fail "Scenario A config does not disable the Game API exactly"
    config_has_exact_api_disabled "$config_b" ||
        fail "Scenario B config does not disable the Game API exactly"
    config_has_exact_api_disabled "$config_c" ||
        fail "Scenario C config does not disable the Game API exactly"

    cp "$current_corpus_manifest" "$corpus_manifest"
    cp "$current_system_manifest" "$system_manifest"
    cp "$current_expected_chart_paths" "$expected_corpus_paths"
    cp "$current_empty_chart_paths" "$expected_empty_paths"
    expected_corpus_paths_sha256="$(sha256_file "$expected_corpus_paths")"
    expected_empty_paths_sha256="$(sha256_file "$expected_empty_paths")"
    [[ "$expected_empty_paths_sha256" == "$empty_manifest_sha256" ]] ||
        fail "empty chart paths and empty manifest identities differ"

    write_common_identity "$common_identity"
}

verify_common_outputs() {
    local observed_empty_manifest="$temporary_root/observed-empty.tsv"
    local expected_config_a="$temporary_root/expected-A.Config.ini"
    local expected_config_b="$temporary_root/expected-B.Config.ini"
    local expected_common_identity="$temporary_root/expected-fixed-identities.txt"

    [[ -d "$empty_songs" &&
       -f "$empty_manifest" &&
       -f "$system_manifest" &&
       -f "$corpus_manifest" &&
       -f "$expected_corpus_paths" &&
       -f "$expected_empty_paths" &&
       -f "$config_a" &&
       -f "$config_b" &&
       -f "$config_c" &&
       -f "$common_identity" ]] ||
        fail "fixed runner inputs are incomplete"

    require_empty_directory "$empty_songs"
    generate_tree_manifest "$empty_songs" "$observed_empty_manifest"
    [[ ! -s "$observed_empty_manifest" ]] ||
        fail "empty-song manifest is not zero bytes"
    cmp -s "$empty_manifest" "$observed_empty_manifest" ||
        fail "empty-song manifest bytes changed"
    cmp -s "$corpus_manifest" "$current_corpus_manifest" ||
        fail "recorded corpus manifest bytes changed"
    cmp -s "$system_manifest" "$current_system_manifest" ||
        fail "System manifest bytes changed"
    cmp -s "$expected_corpus_paths" "$current_expected_chart_paths" ||
        fail "expected corpus chart paths changed"
    cmp -s "$expected_empty_paths" "$current_empty_chart_paths" ||
        fail "expected empty chart paths changed"

    write_fixed_config "$expected_config_a" "$corpus"
    write_fixed_config "$expected_config_b" "$empty_songs"
    cmp -s "$config_a" "$expected_config_a" ||
        fail "Scenario A config bytes changed"
    cmp -s "$config_b" "$expected_config_b" ||
        fail "Scenario B config bytes changed"
    cmp -s "$config_c" "$expected_config_a" ||
        fail "Scenario C config bytes changed"
    config_has_exact_api_disabled "$config_a" ||
        fail "Scenario A config does not disable the Game API exactly"
    config_has_exact_api_disabled "$config_b" ||
        fail "Scenario B config does not disable the Game API exactly"
    config_has_exact_api_disabled "$config_c" ||
        fail "Scenario C config does not disable the Game API exactly"

    empty_manifest_sha256="$(sha256_file "$empty_manifest")"
    config_a_sha256="$(sha256_file "$config_a")"
    config_b_sha256="$(sha256_file "$config_b")"
    config_c_sha256="$(sha256_file "$config_c")"
    expected_corpus_paths_sha256="$(sha256_file "$expected_corpus_paths")"
    expected_empty_paths_sha256="$(sha256_file "$expected_empty_paths")"
    write_common_identity "$expected_common_identity"
    cmp -s "$common_identity" "$expected_common_identity" ||
        fail "fixed input identity bytes changed"
}

verify_live_fixed_inputs() {
    local live_corpus_manifest="$temporary_root/live-corpus-manifest.tsv"
    local live_system_manifest="$temporary_root/live-system-manifest.tsv"
    local live_empty_manifest="$temporary_root/live-empty-manifest.tsv"

    [[ "$(sha256_file "$game_dll")" == "$game_sha256" ]] ||
        fail "game binary bytes changed during the run"
    [[ "$(sha256_file "$script_path")" == "$runner_sha256" ]] ||
        fail "runner bytes changed during the run"
    [[ "$(sha256_file "$summarizer")" == "$summarizer_sha256" ]] ||
        fail "summarizer bytes changed during the run"

    generate_tree_manifest "$corpus" "$live_corpus_manifest"
    cmp -s "$committed_corpus_manifest" "$live_corpus_manifest" ||
        fail "corpus bytes changed during the run"
    cmp -s "$corpus_manifest" "$live_corpus_manifest" ||
        fail "recorded corpus bytes changed during the run"

    generate_tree_manifest "$system_root" "$live_system_manifest"
    cmp -s "$system_manifest" "$live_system_manifest" ||
        fail "System bytes changed during the run"

    require_empty_directory "$empty_songs"
    generate_tree_manifest "$empty_songs" "$live_empty_manifest"
    [[ ! -s "$live_empty_manifest" ]] ||
        fail "empty-song manifest gained content during the run"
    cmp -s "$empty_manifest" "$live_empty_manifest" ||
        fail "empty-song identity changed during the run"
}

verify_seed() {
    local observed_manifest="$temporary_root/observed-seed.tsv"
    local expected_identity="$temporary_root/expected-seed-identity.txt"

    [[ -d "$seed_appdata" && -f "$seed_manifest" && -f "$seed_identity" ]] ||
        fail "seed is incomplete"
    [[ ! -e "$seed_appdata/songs.db-wal" &&
       ! -e "$seed_appdata/songs.db-shm" ]] ||
        fail "seed database is not cleanly closed"
    generate_tree_manifest "$seed_appdata" "$observed_manifest"
    cmp -s "$seed_manifest" "$observed_manifest" ||
        fail "seed app-data bytes changed"
    seed_manifest_sha256="$(sha256_file "$seed_manifest")"
    seed_observed_sha256="$(sha256_file "$observed_manifest")"
    [[ "$seed_manifest_sha256" == "$seed_observed_sha256" ]] ||
        fail "seed manifest identity changed"
    write_seed_identity "$expected_identity"
    cmp -s "$seed_identity" "$expected_identity" ||
        fail "seed identity bytes changed"
}

unix_microseconds() {
    perl -MTime::HiRes=time -e 'printf "%.0f", time * 1000000'
}

monotonic_microseconds() {
    perl -MTime::HiRes=clock_gettime,CLOCK_MONOTONIC \
        -e 'printf "%.0f", clock_gettime(CLOCK_MONOTONIC) * 1000000'
}

launch_start_unix_us=0
launch_start_monotonic_us=0
observation_unix_us=0
observation_monotonic_us=0
timed_out=0
forced_cleanup=0
first_terminal_line=

append_process_metadata() {
    local process_metadata="$1"

    {
        printf 'identity_state\t%s\n' "$game_pid_identity_state"
        printf 'launch_start_unix_us\t%s\n' "$launch_start_unix_us"
        printf 'launch_start_monotonic_us\t%s\n' \
            "$launch_start_monotonic_us"
        printf 'observation_unix_us\t%s\n' "$observation_unix_us"
        printf 'observation_monotonic_us\t%s\n' "$observation_monotonic_us"
        printf 'exit_code\t%s\n' "$game_exit_code"
        printf 'timed_out\t%s\n' "$timed_out"
        printf 'forced_cleanup\t%s\n' "$forced_cleanup"
        printf 'first_terminal_line\t%s\n' "$first_terminal_line"
    } >>"$process_metadata"
}

stabilize_game_pid_identity() {
    local process_metadata="$1"
    local identity_deadline_monotonic_us
    local current_monotonic_us

    identity_deadline_monotonic_us=$((
        launch_start_monotonic_us + identity_stabilization_us
    ))
    while true; do
        if sample_game_pid_identity; then
            game_pid_validated=true
            game_pid_identity_state=validated_live
            return 0
        fi
        if [[ "$game_pid_sample_state" == child_exited ]]; then
            observation_unix_us="$(unix_microseconds)"
            observation_monotonic_us="$(monotonic_microseconds)"
            game_pid_identity_state=child_exited
            wait_for_game
            append_process_metadata "$process_metadata"
            fail \
                "launched game exited before PID identity stabilized: exit_code=$game_exit_code"
        fi

        current_monotonic_us="$(monotonic_microseconds)"
        if (( current_monotonic_us >= identity_deadline_monotonic_us )); then
            observation_unix_us="$(unix_microseconds)"
            observation_monotonic_us="$current_monotonic_us"
            game_pid_identity_state=unvalidated_timeout
            append_process_metadata "$process_metadata"
            fail \
                "launched PID identity did not stabilize within $identity_stabilization_us microseconds"
        fi
        sleep 0.05
    done
}

launch_and_observe() {
    local appdata="$1"
    local stdout_path="$2"
    local stderr_path="$3"
    local process_metadata="$4"
    local iteration
    local deadline_monotonic_us
    local current_monotonic_us

    : >"$stdout_path"
    : >"$stderr_path"
    observation_unix_us=0
    observation_monotonic_us=0
    timed_out=0
    forced_cleanup=0
    first_terminal_line=
    game_exit_code=255
    game_pid_validated=false
    game_pid_identity_state=stabilizing
    launch_start_unix_us="$(unix_microseconds)"
    launch_start_monotonic_us="$(monotonic_microseconds)"
    (
        cd "$game_dir"
        exec env \
            DTXMANIA_APPDATA_ROOT="$appdata" \
            HPA192_CRITICAL_PATH=1 \
            HPA192_EXIT_AFTER_CRITICAL_PATH=1 \
            dotnet "$game_dll"
    ) >"$stdout_path" 2>"$stderr_path" &
    game_pid=$!
    printf 'launched_pid\t%s\n' "$game_pid" >"$process_metadata"

    stabilize_game_pid_identity "$process_metadata"

    deadline_monotonic_us=$((launch_start_monotonic_us + 60000000))
    while true; do
        current_monotonic_us="$(monotonic_microseconds)"
        if (( current_monotonic_us >= deadline_monotonic_us )); then
            timed_out=1
            observation_unix_us="$(unix_microseconds)"
            observation_monotonic_us="$current_monotonic_us"
            break
        fi
        first_terminal_line="$(
            awk '
                /^HPA192_CRITICAL_PATH / ||
                /^HPA192_CRITICAL_PATH_FAILURE / {
                    print
                    exit
                }
            ' "$stdout_path"
        )"
        if [[ -n "$first_terminal_line" ]]; then
            observation_unix_us="$(unix_microseconds)"
            observation_monotonic_us="$(monotonic_microseconds)"
            if (( observation_monotonic_us >= deadline_monotonic_us )); then
                timed_out=1
            fi
            break
        fi
        if ! kill -0 "$game_pid" 2>/dev/null; then
            observation_unix_us="$(unix_microseconds)"
            observation_monotonic_us="$(monotonic_microseconds)"
            if (( observation_monotonic_us >= deadline_monotonic_us )); then
                timed_out=1
            fi
            break
        fi
        sleep 0.05
    done

    if [[ "$timed_out" == 1 ]]; then
        if kill -0 "$game_pid" 2>/dev/null; then
            forced_cleanup=1
            terminate_validated_game ||
                fail "timed-out process cleanup failed PID validation"
        fi
    elif [[ -n "$first_terminal_line" ]]; then
        for ((iteration = 0; iteration < 100; iteration++)); do
            if ! kill -0 "$game_pid" 2>/dev/null; then
                break
            fi
            sleep 0.05
        done
        if kill -0 "$game_pid" 2>/dev/null; then
            forced_cleanup=1
            terminate_validated_game ||
                fail "post-publication cleanup failed PID validation"
        fi
    fi

    wait_for_game
    append_process_metadata "$process_metadata"
}

database_charts=0
database_songs=0

inspect_closed_database() {
    local appdata="$1"
    local chart_paths="$2"
    local database="$appdata/songs.db"
    local integrity

    : >"$chart_paths"
    [[ -f "$database" ]] || return 1
    [[ ! -e "$database-wal" && ! -e "$database-shm" ]] || return 1
    integrity="$(sqlite3 -noheader "$database" 'PRAGMA integrity_check;')" ||
        return 1
    [[ "$integrity" == ok ]] || return 1
    database_charts="$(
        sqlite3 -noheader "$database" 'SELECT COUNT(*) FROM SongCharts;'
    )" || return 1
    database_songs="$(
        sqlite3 -noheader "$database" 'SELECT COUNT(*) FROM Songs;'
    )" || return 1
    [[ "$database_charts" =~ ^(0|[1-9][0-9]*)$ &&
       "$database_songs" =~ ^(0|[1-9][0-9]*)$ ]] ||
        return 1
    sqlite3 -noheader "$database" \
        'SELECT FilePath FROM SongCharts;' |
        LC_ALL=C sort >"$chart_paths" || return 1
    [[ ! -e "$database-wal" && ! -e "$database-shm" ]] || return 1
}

# HPA-190: the authoritative configuration store is the SQLite config
# database (<app-data>/config.db, ConfigEntries(Key, Value), user_version 1).
# The legacy Config.ini is only a first-launch bootstrap input, so active
# configuration (notably EnableGameApi) must be proven from the database.
# Echoes the EnableGameApi value (True/False); fails when the database is
# missing, still open, or not a valid v1 ConfigEntries store (fail closed).
config_database_api_enabled() {
    local appdata="$1"
    local database="$appdata/config.db"
    local version
    local value

    [[ -f "$database" ]] || return 1
    [[ ! -e "$database-wal" && ! -e "$database-shm" ]] || return 1
    version="$(sqlite3 -noheader "$database" 'PRAGMA user_version;')" ||
        return 1
    [[ "$version" == 1 ]] || return 1
    value="$(
        sqlite3 -noheader "$database" \
            "SELECT Value FROM ConfigEntries WHERE Key = 'EnableGameApi';"
    )" || return 1
    [[ "$value" == True || "$value" == False ]] || return 1
    # Recheck after the query: a connection left holding the database open
    # materializes -wal/-shm sidecars, and the caller treats those as a
    # still-running game (fail closed).
    [[ ! -e "$database-wal" && ! -e "$database-shm" ]] || return 1
    printf '%s\n' "$value"
}

extract_raw_product_lines() {
    awk '
        /^HPA192_STARTUP / ||
        /^HPA192_TIMING / ||
        /^HPA192_CRITICAL_PATH / ||
        /^HPA192_CRITICAL_PATH_FAILURE / {
            print
        }
    ' "$1"
}

prepare_seed() {
    local setup_appdata
    local setup_stdout
    local setup_stderr
    local setup_process
    local setup_raw
    local setup_chart_paths

    prepare_common_outputs
    mkdir -p "$seed_root"
    setup_appdata="$seed_root/setup-appdata"
    setup_stdout="$seed_root/setup.stdout.log"
    setup_stderr="$seed_root/setup.stderr.log"
    setup_process="$seed_root/setup-process.txt"
    setup_raw="$seed_root/setup-result.txt"
    setup_chart_paths="$seed_root/setup-chart-paths.txt"
    mkdir -p "$setup_appdata"
    cp "$config_a" "$setup_appdata/Config.ini"
    cmp -s "$config_a" "$setup_appdata/Config.ini" ||
        fail "seed prelaunch config bytes differ"
    config_has_exact_api_disabled "$setup_appdata/Config.ini" ||
        fail "seed prelaunch config enables the Game API"

    launch_and_observe \
        "$setup_appdata" \
        "$setup_stdout" \
        "$setup_stderr" \
        "$setup_process"
    extract_raw_product_lines "$setup_stdout" >"$setup_raw"
    verify_live_fixed_inputs

    [[ "$first_terminal_line" == HPA192_CRITICAL_PATH\ * &&
       "$timed_out" == 0 &&
       "$forced_cleanup" == 0 &&
       "$game_exit_code" == 0 ]] ||
        fail "excluded seed setup did not publish and self-exit cleanly"
    cmp -s "$config_a" "$setup_appdata/Config.ini" ||
        fail "seed setup changed the fixed config"
    config_has_exact_api_disabled "$setup_appdata/Config.ini" ||
        fail "seed setup enabled the Game API"
    database_charts=0
    database_songs=0
    inspect_closed_database "$setup_appdata" "$setup_chart_paths" ||
        fail "seed database is missing, open, or invalid"
    [[ "$database_charts" -eq 100 && "$database_songs" -eq 27 ]] ||
        fail "seed database counts are not 100 charts and 27 songs"
    cmp -s "$expected_corpus_paths" "$setup_chart_paths" ||
        fail "seed database chart paths differ from the corpus"
    # HPA-190: the seed's authoritative config database must exist and hold
    # the Game API disabled — the INI checks above only prove the untouched
    # bootstrap input, not the active configuration.
    [[ "$(config_database_api_enabled "$setup_appdata")" == False ]] ||
        fail "seed setup left the Game API enabled (or missing) in config.db"

    mv "$setup_appdata" "$seed_appdata"
    generate_tree_manifest "$seed_appdata" "$seed_manifest"
    seed_manifest_sha256="$(sha256_file "$seed_manifest")"
    seed_observed_sha256="$seed_manifest_sha256"
    write_seed_identity "$seed_identity"
    verify_common_outputs
    verify_live_fixed_inputs
    verify_seed
    printf 'HPA192_CRITICAL_PATH_SEED status=ready charts=100 songs=27 seed_manifest_sha256=%s\n' \
        "$seed_manifest_sha256"
}

run_attempt() {
    local scenario="$1"
    local slot="$2"
    local attempt_number="$3"
    local attempt_root="$4"
    local appdata="$attempt_root/appdata"
    local stdout_path="$attempt_root/stdout.log"
    local stderr_path="$attempt_root/stderr.log"
    local process_metadata="$attempt_root/process.txt"
    local chart_paths="$attempt_root/chart-paths.txt"
    local database_inspection="$attempt_root/database-inspection.txt"
    local result_path="$attempt_root/result.txt"
    local validation_path="$attempt_root/validation.txt"
    local validation_stderr="$attempt_root/validation.stderr.log"
    local scenario_config
    local expected_chart_paths
    local config_sha256
    local config_observed_sha256
    local empty_observed_sha256
    local chart_paths_sha256
    local expected_chart_paths_sha256
    local game_api_enabled
    local config_api_value
    local config_database_inspection="$attempt_root/config-database.txt"
    local observed_seed_manifest="$temporary_root/attempt-seed-$slot-$attempt_number.tsv"
    local observed_empty_manifest="$temporary_root/attempt-empty-$slot-$attempt_number.tsv"
    local clone_manifest="$temporary_root/clone-$slot-$attempt_number.tsv"
    local validation_output
    local validation_status

    verify_common_outputs
    verify_live_fixed_inputs
    verify_seed
    [[ ! -e "$attempt_root" && ! -L "$attempt_root" ]] ||
        fail "attempt directory already exists: $attempt_root"
    mkdir -p "$appdata"

    case "$scenario" in
        A)
            scenario_config="$config_a"
            expected_chart_paths="$expected_corpus_paths"
            ;;
        B)
            scenario_config="$config_b"
            expected_chart_paths="$expected_empty_paths"
            ;;
        C)
            scenario_config="$config_c"
            expected_chart_paths="$expected_corpus_paths"
            cp -R "$seed_appdata"/. "$appdata"
            generate_tree_manifest "$appdata" "$clone_manifest"
            cmp -s "$seed_manifest" "$clone_manifest" ||
                fail "Scenario C prelaunch clone differs from the seed"
            ;;
        *)
            fail "unsupported scenario: $scenario"
            ;;
    esac

    # HPA-190: the copied INI is the recorded bootstrap input (imported only
    # on a cold app-data in scenarios A/B; inert for the warm scenario C clone
    # whose config.db is authoritative). The active configuration is proven
    # from the database after the run, not from these bytes.
    cp "$scenario_config" "$appdata/Config.ini"
    cmp -s "$scenario_config" "$appdata/Config.ini" ||
        fail "attempt prelaunch config bytes differ"
    config_has_exact_api_disabled "$appdata/Config.ini" ||
        fail "attempt prelaunch config enables the Game API"
    config_sha256="$(sha256_file "$scenario_config")"

    launch_and_observe \
        "$appdata" \
        "$stdout_path" \
        "$stderr_path" \
        "$process_metadata"

    verify_common_outputs
    verify_live_fixed_inputs
    verify_seed
    generate_tree_manifest "$seed_appdata" "$observed_seed_manifest"
    seed_observed_sha256="$(sha256_file "$observed_seed_manifest")"
    generate_tree_manifest "$empty_songs" "$observed_empty_manifest"
    empty_observed_sha256="$(sha256_file "$observed_empty_manifest")"

    if [[ -f "$appdata/Config.ini" ]]; then
        config_observed_sha256="$(sha256_file "$appdata/Config.ini")"
    else
        config_observed_sha256="$empty_manifest_sha256"
    fi
    # HPA-190: prove the active configuration from the authoritative config
    # database. Fail closed — a missing, open, invalid, or API-enabled
    # database is recorded as enabled so the summarizer rejects the attempt
    # (it cannot be proven that the Game API was disabled).
    config_api_value=
    if ! config_api_value="$(config_database_api_enabled "$appdata")"; then
        config_api_value=
    fi
    if [[ "$config_api_value" == False ]]; then
        game_api_enabled=0
        printf '%s\n' \
            'status=closed enable_game_api=False' \
            >"$config_database_inspection"
    else
        game_api_enabled=1
        printf 'status=rejected reason=missing_open_invalid_or_enabled_config_database enable_game_api=%s\n' \
            "${config_api_value:-unknown}" >"$config_database_inspection"
    fi

    database_charts=0
    database_songs=0
    if inspect_closed_database "$appdata" "$chart_paths"; then
        printf 'status=closed charts=%s songs=%s\n' \
            "$database_charts" "$database_songs" >"$database_inspection"
    else
        database_charts=0
        database_songs=0
        printf '%s\n' 'HPA192_DATABASE_INSPECTION_FAILED' >"$chart_paths"
        printf '%s\n' \
            'status=rejected reason=missing_open_or_invalid_database' \
            >"$database_inspection"
    fi
    chart_paths_sha256="$(sha256_file "$chart_paths")"
    expected_chart_paths_sha256="$(sha256_file "$expected_chart_paths")"

    {
        printf 'HPA192_ATTEMPT'
        printf ' scenario=%s' "$scenario"
        printf ' slot=%s' "$slot"
        printf ' attempt=%s' "$attempt_number"
        printf ' launch_start_unix_us=%s' "$launch_start_unix_us"
        printf ' launch_start_monotonic_us=%s' \
            "$launch_start_monotonic_us"
        printf ' observation_unix_us=%s' "$observation_unix_us"
        printf ' observation_monotonic_us=%s' \
            "$observation_monotonic_us"
        printf ' exit_code=%s' "$game_exit_code"
        printf ' timed_out=%s' "$timed_out"
        printf ' forced_cleanup=%s' "$forced_cleanup"
        printf ' game_api_enabled=%s' "$game_api_enabled"
        printf ' database_charts=%s' "$database_charts"
        printf ' database_songs=%s' "$database_songs"
        printf ' game_sha256=%s' "$game_sha256"
        printf ' runner_sha256=%s' "$runner_sha256"
        printf ' summarizer_sha256=%s' "$summarizer_sha256"
        printf ' corpus_manifest_sha256=%s' "$corpus_manifest_sha256"
        printf ' corpus_observed_sha256=%s' "$corpus_observed_sha256"
        printf ' system_manifest_sha256=%s' "$system_manifest_sha256"
        printf ' config_sha256=%s' "$config_sha256"
        printf ' config_observed_sha256=%s' "$config_observed_sha256"
        printf ' empty_manifest_sha256=%s' "$empty_manifest_sha256"
        printf ' empty_observed_sha256=%s' "$empty_observed_sha256"
        printf ' seed_manifest_sha256=%s' "$seed_manifest_sha256"
        printf ' seed_observed_sha256=%s' "$seed_observed_sha256"
        printf ' chart_paths_sha256=%s' "$chart_paths_sha256"
        printf ' expected_chart_paths_sha256=%s\n' \
            "$expected_chart_paths_sha256"
        extract_raw_product_lines "$stdout_path"
    } >"$result_path"

    if validation_output="$(
        bash "$summarizer" --validate-attempt "$result_path" \
            2>"$validation_stderr"
    )"; then
        validation_status=0
    else
        validation_status=$?
    fi
    printf '%s\n' "$validation_output" >"$validation_path"
    if [[ "$validation_status" -eq 0 ]] &&
       grep -Eq \
           "^HPA192_CRITICAL_PATH_ATTEMPT status=accepted scenario=$scenario slot=$slot attempt=$attempt_number " \
           "$validation_path"; then
        return 0
    fi
    return 1
}

run_matrix() {
    local -a scenarios=(A B C B C A C A B A C B C B A)
    local index
    local slot
    local scenario
    local slot_name
    local slot_root
    local attempt_number
    local attempt_root
    local accepted
    local result_path
    local output_path

    verify_common_outputs
    verify_seed
    for output_path in \
        "$slots_root" \
        "$accepted_artifacts" \
        "$decision_path" \
        "$matrix_identity"
    do
        [[ ! -e "$output_path" && ! -L "$output_path" ]] ||
            fail "matrix output namespace is dirty: $output_path"
    done

    mkdir -p "$slots_root"
    : >"$accepted_artifacts"
    cp "$common_identity" "$matrix_identity"

    for ((index = 0; index < ${#scenarios[@]}; index++)); do
        slot=$((index + 1))
        scenario="${scenarios[$index]}"
        slot_name="$(printf '%02d-%s' "$slot" "$scenario")"
        slot_root="$slots_root/$slot_name"
        mkdir -p "$slot_root"
        accepted=false

        for attempt_number in 1 2 3; do
            attempt_root="$slot_root/attempt-$attempt_number"
            if run_attempt \
                "$scenario" \
                "$slot" \
                "$attempt_number" \
                "$attempt_root"; then
                result_path="$attempt_root/result.txt"
                canonical_file "$result_path" accepted-artifact \
                    >>"$accepted_artifacts"
                accepted=true
                break
            fi
        done

        if [[ "$accepted" != true ]]; then
            printf 'HPA192_CRITICAL_PATH_DECISION decision=stop reason=diagnostic_harness slot=%s scenario=%s\n' \
                "$slot" "$scenario" |
                tee "$decision_path"
            return 1
        fi
    done

    [[ "$(awk 'END { print NR + 0 }' "$accepted_artifacts")" -eq 15 ]] ||
        fail "matrix did not retain exactly 15 accepted artifacts"
    printf 'HPA192_CRITICAL_PATH_MATRIX status=complete accepted=15\n'
}

case "$command_name" in
    prepare-seed)
        prepare_seed
        ;;
    matrix)
        run_matrix
        ;;
esac
