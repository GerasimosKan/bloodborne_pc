# SPDX-FileCopyrightText: Copyright 2026 bbport contributors
# SPDX-License-Identifier: GPL-2.0-or-later
"""scripts/trophy_import.py on synthetic files: the older code's trophies.txt reaches trophies.jsonl
and unlocked.txt once, without duplicates, and is left as it was."""
import sys
import tempfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "scripts"))
sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "launcher"))
import trophy_import  # noqa: E402
import trophy_list  # noqa: E402

OLD = "14 1760123456 Father Gascoigne\n21 1760000000 Cleric Beast\nbad line\n21 1760200000 Cleric Beast\n"


def main():
    with tempfile.TemporaryDirectory() as tmp:
        user = Path(tmp)
        assert trophy_import.run(user) == 0  # nothing old: nothing done
        assert not (user / "trophies.jsonl").exists()

        (user / "trophies.txt").write_text(OLD, encoding="utf-8")
        (user / "trophies.jsonl").write_text(
            '{"v":1,"title_id":"CUSA03173","trophy_id":14,"unlocked_at":"2026-10-11T00:00:00Z"}\n', encoding="utf-8")
        assert trophy_import.run(user) == 1  # 14 is already there
        assert trophy_import.run(user) == 0  # every Play after: nothing again
        assert (user / "trophies.txt").read_text(encoding="utf-8") == OLD

        times = trophy_list.unlock_times(user)
        assert times == {14: "2026-10-11T00:00:00Z", 21: "2025-10-09T08:53:20Z"}  # 21's earliest line
        listed = (user / "trophy" / "NPWR05818_00" / "unlocked.txt").read_text(encoding="utf-8")
        assert listed.splitlines()[1:] == ["14", "21"]
        page = trophy_list.load(user)
        assert page["unlocked"] == 2 and [t["id"] for t in page["trophies"]] == [14, 21]
    print("trophy-import: ok")


if __name__ == "__main__":
    main()
