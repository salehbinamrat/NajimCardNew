"""Release layout and shortcut UI regressions for organized Portable builds."""
from pathlib import Path
import unittest
ROOT=Path(__file__).resolve().parents[1]
class PortableLayoutTests(unittest.TestCase):
    def test_publish_remains_self_contained_and_not_single_file(self):
        build=(ROOT/"scripts/package.ps1").read_text(encoding="utf-8")
        self.assertIn("--self-contained true",build)
        self.assertIn("-p:PublishTrimmed=false",build)
        self.assertNotIn("-p:PublishSingleFile=true",build)
        self.assertIn("Join-Path $portableDir 'App'",build)
        self.assertIn('Join-Path $portableDir "NajmCard.exe"',build)
        self.assertIn("Compress-Archive -Path (Join-Path $portableDir '*')",build)
        self.assertIn('"/DSourceDir=$portableDir"',build)
        self.assertIn("Test-PortableLayout",build)
        self.assertIn("--verify-layout",build)

    def test_native_launcher_is_arch_specific_and_forwards_arguments(self):
        build=(ROOT/"scripts/package.ps1").read_text(encoding="utf-8")
        launcher=(ROOT/"scripts/portable-launcher.cpp").read_text(encoding="utf-8")
        self.assertIn("vcvarsall.bat",build)
        self.assertIn("rc.exe",build)
        self.assertIn("cl.exe",build)
        self.assertIn("/MT",build)
        self.assertIn("/SUBSYSTEM:WINDOWS",build)
        self.assertIn("Assets/NajmCard.ico",build.replace("\\","/"))
        self.assertIn('L"\\\\App"',launcher)
        self.assertIn('L"\\\\NajmCard.exe"',launcher)
        self.assertIn("CreateProcessW",launcher)
        self.assertIn("GetExitCodeProcess",launcher)
        self.assertIn("GetModuleFileNameW",launcher)

    def test_shortcut_is_opt_in_for_installer_and_available_in_app_settings(self):
        iss=(ROOT/"installer/NajmCard.iss").read_text(encoding="utf-8")
        settings=(ROOT/"src/NajmCard.Desktop/Views/SettingsView.cs").read_text(encoding="utf-8")
        service=(ROOT/"src/NajmCard.Desktop/Services/DesktopShortcutService.cs").read_text(encoding="utf-8")
        self.assertIn('Flags: unchecked',iss)
        self.assertIn('Tasks: desktopicon',iss)
        self.assertIn('Filename: "{app}\\NajmCard.exe"',iss)
        self.assertIn("إنشاء اختصار",settings)
        self.assertIn("DesktopShortcutService",settings)
        self.assertIn("WScript.Shell",service)
        self.assertIn("TargetPath",service)
        self.assertIn("IconLocation",service)

    def test_settings_theme_is_not_purple_and_shortcuts_do_not_run_automatically(self):
        code=(ROOT/"src/NajmCard.Desktop/Views/SettingsView.cs").read_text(encoding="utf-8")
        self.assertNotIn("#6557A4",code)
        self.assertIn("PageUi.Surface",code)
        self.assertIn("shortcut.Click",code)
        self.assertNotIn("CreateOrUpdateShortcut();",code)

    def test_sqlite_wal_mode_initialized_once_and_full_durability_is_retained(self):
        code=(ROOT/"src/NajmCard.Desktop/Services/VoucherStore.cs").read_text(encoding="utf-8")
        self.assertIn("Open(configureWal:true)",code)
        self.assertIn("Open(bool configureWal=false)",code)
        self.assertIn("PRAGMA synchronous=FULL",code)
        self.assertIn("PRAGMA journal_mode=WAL",code)
        self.assertIn("if(configureWal)",code)
if __name__=="__main__":
    unittest.main()
