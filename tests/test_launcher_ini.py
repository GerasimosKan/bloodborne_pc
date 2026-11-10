# SPDX-FileCopyrightText: Copyright 2026 bbport contributors
# SPDX-License-Identifier: GPL-2.0-or-later
"""The launcher's bbport.ini writes: what the game's menu changed on disk meanwhile is kept."""
from paths import ROOT
import importlib.util
import os
import sys
import tempfile
import types
import unittest
from pathlib import Path
from unittest import mock

# The launcher's window needs GTK; these tests use its functions only (as test_launcher_gpu.py).
gi = types.ModuleType("gi")
gi.require_version = lambda *_args: None
gi.repository = mock.MagicMock()
gi.repository.Adw.ApplicationWindow = type("ApplicationWindow", (), {})
gi.repository.Adw.Application = type("Application", (), {})
sys.modules.setdefault("gi", gi)
sys.modules.setdefault("gi.repository", gi.repository)
sys.path.insert(0, str(ROOT / "launcher"))
spec = importlib.util.spec_from_file_location("bbport_launcher", ROOT / "launcher/bbport_launcher.py")
launcher = importlib.util.module_from_spec(spec)
spec.loader.exec_module(launcher)


class LauncherIniTests(unittest.TestCase):
    def setUp(self):
        folder = tempfile.TemporaryDirectory()
        self.addCleanup(folder.cleanup)
        self.path = Path(folder.name) / "bbport.ini"
        env = mock.patch.dict(os.environ, {"BB_CONFIG": str(self.path)})
        env.start()
        self.addCleanup(env.stop)

    def test_game_changes_on_disk_survive_a_store(self):
        self.path.write_text("# mine\nupscaler=fsr4\nsharpness=0.5\nkey.jump=Space\n", encoding="utf-8")
        shown = {"upscaler": "fsr4", "sharpness": "0.50", "show_fps": "1"}
        # The game's menu rewrites the file while the launcher shows the old values.
        self.path.write_text("# mine\nupscaler=dlss\nsharpness=0.7\nkey.jump=Space\nmenu_pos=3\n",
                             encoding="utf-8")
        launcher.store_ini(shown, dict(shown, show_fps="0"))
        values, lines = launcher.load_ini()
        self.assertEqual((values["upscaler"], values["sharpness"], values["menu_pos"]), ("dlss", "0.7", "3"))
        self.assertEqual(values["show_fps"], "0")  # what the user changed in the launcher
        self.assertEqual(lines[0], "# mine")

    def test_bindings_and_missing_keys_are_written(self):
        self.path.write_text("key.jump=Space\nkey.roll=Q\n", encoding="utf-8")
        shown = {"anti_aliasing": "auto"}
        launcher.store_ini(shown, {**shown, "key.jump": None, "key.roll": "E"})
        values, _lines = launcher.load_ini()
        self.assertNotIn("key.jump", values)  # back to its default
        self.assertEqual((values["key.roll"], values["anti_aliasing"]), ("E", "auto"))


if __name__ == "__main__":
    unittest.main()
