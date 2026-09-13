#!/usr/bin/env bash
# Copy a published Android .so into jniLibs/<abi>/libnuvexa.so
# Usage: stage-jni-lib.sh <native-dir> <jniLibs-dir> [abi]
set -euo pipefail

native_dir="${1:?native-dir}"
jni_root="${2:?jniLibs-dir}"
abi="${3:-arm64-v8a}"

lib=""
for name in libnuvexa.so nuvexa.so; do
  if [[ -f "$native_dir/$name" ]]; then
    lib="$native_dir/$name"
    break
  fi
done
if [[ -z "$lib" ]]; then
  echo "No Android .so in $native_dir" >&2
  ls -la "$native_dir" >&2 || true
  exit 1
fi

dest="$jni_root/$abi"
mkdir -p "$dest"
cp "$lib" "$dest/libnuvexa.so"
echo "Staged $dest/libnuvexa.so"
