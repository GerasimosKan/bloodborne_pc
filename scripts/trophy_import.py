# SPDX-FileCopyrightText: Copyright 2026 bbport contributors
# SPDX-License-Identifier: GPL-2.0-or-later
"""Brings the trophies of the older trophy code into this one, once: run.py calls it on every Play.

The older code (GerasimosKan's 635e442 / 7369ab8) kept <user>/trophies.txt, one "id unix_time name"
line per unlock. This port keeps <user>/trophies.jsonl (src/runtime_trophy.c: the times, the
launcher's Trophies page) and <user>/trophy/<NP communication ID>/unlocked.txt
(gpu/shim/bbport_trophies.cpp: what was already announced, the platinum). Ids missing from either
are appended; trophies.txt is only read, never changed.

Usage: trophy_import.py --user <user folder>"""

import json
import sys
import time
from pathlib import Path

TITLE_ID = "CUSA03173"            # TROPHY_STORE_DEFAULT_TITLE (src/runtime_trophy.h)
NP_ID = "NPWR05818_00"            # Bloodborne's NP communication ID


def old_unlocks(path):
    """{id: unix time} of trophies.txt (the earliest line of each id; 0 when the time is missing)."""
    found = {}
    for line in path.read_text(encoding="utf-8", errors="replace").splitlines():
        parts = line.split()
        try:
            trophy_id = int(parts[0])
            when = int(parts[1]) if len(parts) > 1 else 0
        except (IndexError, ValueError):
            continue
        if 0 <= trophy_id <= 255 and (trophy_id not in found or 0 < when < found[trophy_id]):
            found[trophy_id] = when
    return found


def jsonl_ids(path):
    ids = set()
    try:
        lines = path.read_text(encoding="utf-8", errors="replace").splitlines()
    except OSError:
        return ids
    for line in lines:
        try:
            ids.add(int(json.loads(line)["trophy_id"]))
        except (ValueError, KeyError, TypeError):
            continue
    return ids


def run(user_dir):
    """Imports what is missing; returns the number of ids added to trophies.jsonl."""
    user = Path(user_dir)
    old = user / "trophies.txt"
    if not old.is_file():
        return 0
    unlocks = old_unlocks(old)
    fallback = int(old.stat().st_mtime)
    store = user / "trophies.jsonl"
    have = jsonl_ids(store)
    new = sorted(set(unlocks) - have)
    if new:
        with store.open("a", encoding="utf-8", newline="\n") as f:
            for trophy_id in new:
                stamp = time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime(unlocks[trophy_id] or fallback))
                f.write(json.dumps({"v": 1, "title_id": TITLE_ID, "trophy_id": trophy_id, "unlocked_at": stamp},
                                   separators=(",", ":")) + "\n")
    folder = user / "trophy" / NP_ID
    listed = folder / "unlocked.txt"
    announced = set()
    if listed.is_file():
        for line in listed.read_text(encoding="utf-8", errors="replace").splitlines():
            if line.strip().isdigit():
                announced.add(int(line))
    missing = sorted(set(unlocks) - announced)
    if missing:
        folder.mkdir(parents=True, exist_ok=True)
        text = listed.read_text(encoding="utf-8", errors="replace") if listed.is_file() else \
            "# the trophies unlocked here, one id a line\n"
        if text and not text.endswith("\n"):
            text += "\n"
        listed.write_text(text + "".join(f"{i}\n" for i in missing), encoding="utf-8", newline="\n")
    if new or missing:
        print(f"Trophies: {len(new)} imported from {old.name}")
    return len(new)


if __name__ == "__main__":
    args = sys.argv[1:]
    if len(args) != 2 or args[0] != "--user":
        sys.exit(__doc__)
    try:
        run(args[1])
    except OSError as error:  # never stops the game from starting
        print(f"Trophies: {error}")
