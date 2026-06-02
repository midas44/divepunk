#!/usr/bin/env bash
# DIVEPUNK convenience runner — wraps the common Godot 4.6 commands.
# Usage: ./run.sh [command]   (run "./run.sh help" for the list)
#
# The Godot binary is auto-detected (godot-mono, then godot, then godot4). Override with:
#   GODOT=/path/to/godot ./run.sh play
# NOTE: this is a C# (.NET) project — it needs the Mono/.NET build of Godot (godot-mono),
# so the auto-detect prefers it. Run `./run.sh build` to compile C# before a headless run.

set -euo pipefail

PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$PROJECT_DIR"

MAIN_SCENE="res://scenes/main/Main.tscn"
BUILD_DIR="build"

# --- locate the Godot binary -------------------------------------------------
GODOT_BIN="${GODOT:-}"
if [[ -z "$GODOT_BIN" ]]; then
	if command -v godot-mono >/dev/null 2>&1; then
		GODOT_BIN="godot-mono"
	elif command -v godot >/dev/null 2>&1; then
		GODOT_BIN="godot"
	elif command -v godot4 >/dev/null 2>&1; then
		GODOT_BIN="godot4"
	else
		echo "error: no 'godot-mono', 'godot' or 'godot4' on PATH. Set GODOT=/path/to/godot." >&2
		exit 1
	fi
fi

usage() {
	cat <<EOF
DIVEPUNK runner — Godot: $GODOT_BIN

Usage: ./run.sh [command]

  play            Play the main scene ($MAIN_SCENE)   [default]
  editor          Open the project in the Godot editor
  build           Compile the C# solution (dotnet build -c Debug) — run before headless
  check           Headless smoke-test: import, run 180 frames, quit (exit 0 = OK)
  bake            Headless bake of the world -> res://world/world_main.res (run build first)
  modelgen        Headless generate the test glTF -> res://assets/models/test_tower.glb (run build first)
  import          Headless import only (regenerate .godot/)
  export-linux    Export release Linux  -> $BUILD_DIR/divepunk.x86_64
  export-android  Export release Android -> $BUILD_DIR/divepunk.apk
  version         Print the Godot version
  help            Show this message

Export presets must be configured in the editor first (Project > Export).
EOF
}

cmd="${1:-play}"
case "$cmd" in
	play)
		exec "$GODOT_BIN" --path . "$MAIN_SCENE"
		;;
	editor)
		exec "$GODOT_BIN" --editor --path .
		;;
	build)
		echo ":: building C# (dotnet build -c Debug)..."
		exec dotnet build -c Debug
		;;
	check)
		echo ":: importing..."
		"$GODOT_BIN" --headless --path . --import
		echo ":: booting main scene (180 frames)..."
		"$GODOT_BIN" --headless --path . "$MAIN_SCENE" --quit-after 180
		echo ":: boot OK (exit 0)"
		;;
	bake)
		echo ":: importing..."
		"$GODOT_BIN" --headless --path . --import
		echo ":: baking world -> res://world/world_main.res ..."
		"$GODOT_BIN" --headless --path . res://scenes/tools/Bake.tscn --quit-after 600
		echo ":: bake done"
		;;
	modelgen)
		echo ":: importing..."
		"$GODOT_BIN" --headless --path . --import
		echo ":: generating test model -> res://assets/models/test_tower.glb ..."
		"$GODOT_BIN" --headless --path . res://scenes/tools/ModelGen.tscn --quit-after 120
		echo ":: model gen done (the next --import will import the new .glb)"
		;;
	import)
		exec "$GODOT_BIN" --headless --path . --import
		;;
	export-linux)
		mkdir -p "$BUILD_DIR"
		exec "$GODOT_BIN" --headless --path . --export-release "Linux" "$BUILD_DIR/divepunk.x86_64"
		;;
	export-android)
		mkdir -p "$BUILD_DIR"
		exec "$GODOT_BIN" --headless --path . --export-release "Android" "$BUILD_DIR/divepunk.apk"
		;;
	version)
		exec "$GODOT_BIN" --version
		;;
	help|-h|--help)
		usage
		;;
	*)
		echo "error: unknown command '$cmd'" >&2
		echo >&2
		usage >&2
		exit 1
		;;
esac
