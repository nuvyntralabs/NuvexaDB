#!/usr/bin/env python3
"""Gate NuvexaDB git commit / tag until the user answers the release question."""
import json
import os
import re
import subprocess
import sys


def load_input():
    try:
        raw = sys.stdin.read()
        if not raw.strip():
            return {}
        return json.loads(raw)
    except json.JSONDecodeError:
        return {}


def text(*values):
    return " ".join(v for v in values if isinstance(v, str) and v)


def git_toplevel(cwd):
    try:
        return subprocess.check_output(
            ["git", "rev-parse", "--show-toplevel"],
            cwd=cwd or None,
            text=True,
            stderr=subprocess.DEVNULL,
        ).strip()
    except (subprocess.CalledProcessError, FileNotFoundError, OSError):
        return cwd or ""


def looks_like_nuvexa(command, cwd):
    blob = text(command, cwd)
    if re.search(r"(^|/)NuvexaDB(/|$)", blob, re.IGNORECASE):
        return True
    if re.search(r"\bgit\b.*\s-C\s+[^\s]*NuvexaDB\b", command, re.IGNORECASE):
        return True
    top = git_toplevel(cwd)
    if os.path.basename(top).lower() == "nuvexadb":
        return True
    return False


def is_git_commit(command):
    return bool(re.search(r"\bgit\b(?:\s+\S+)*\s+commit\b", command))


def is_release_tag_command(command):
    if not re.search(r"\bgit\b", command):
        return False
    if re.search(r"\btag\b", command) and re.search(r"\bv\d", command):
        return True
    if re.search(r"\bpush\b", command) and (
        "--tags" in command or "refs/tags" in command or re.search(r"\bv\d", command)
    ):
        return True
    return False


def release_answer(command):
    match = re.search(r"\bNUVEXA_RELEASE=(yes|no)\b", command, re.IGNORECASE)
    if not match:
        return None
    return match.group(1).lower()


def emit(payload):
    sys.stdout.write(json.dumps(payload))
    sys.exit(0)


def main():
    data = load_input()
    command = text(
        data.get("command"),
        (data.get("tool_input") or {}).get("command") if isinstance(data.get("tool_input"), dict) else "",
    )
    cwd = text(
        data.get("cwd"),
        data.get("working_directory"),
        data.get("directory"),
    )

    if not looks_like_nuvexa(command, cwd):
        emit({"permission": "allow"})

    answer = release_answer(command)

    if is_release_tag_command(command):
        if answer == "yes":
            emit({"permission": "allow"})
        emit(
            {
                "permission": "ask",
                "user_message": (
                    "This publishes a NuvexaDB GitHub Release "
                    "(https://github.com/nuvyntralabs/NuvexaDB/releases). "
                    "Approve only if you already said this commit should be a release."
                ),
                "agent_message": (
                    "A NuvexaDB v* tag or tag push needs an explicit Yes to "
                    "'Should this commit make a GitHub Release?'"
                ),
            }
        )

    if is_git_commit(command):
        if answer == "no":
            emit({"permission": "allow"})
        if answer == "yes":
            emit(
                {
                    "permission": "allow",
                    "agent_message": (
                        "User confirmed a NuvexaDB release. After this commit succeeds, "
                        "create and push tag v<Version> from Directory.Build.props "
                        "with NUVEXA_RELEASE=yes."
                    ),
                }
            )
        emit(
            {
                "permission": "deny",
                "user_message": (
                    "NuvexaDB commit paused. The agent must ask whether this commit "
                    "should publish a GitHub Release."
                ),
                "agent_message": (
                    "Stop. Ask the user with AskQuestion: "
                    "'Should this NuvexaDB commit make a GitHub Release?' "
                    "Options: 'No — commit only' (default) or 'Yes — commit and release'. "
                    "Wait for the answer. Then retry the same git commit prefixed with "
                    "NUVEXA_RELEASE=no or NUVEXA_RELEASE=yes. "
                    "Do not tag unless they chose Yes."
                ),
            }
        )

    emit({"permission": "allow"})


if __name__ == "__main__":
    main()
