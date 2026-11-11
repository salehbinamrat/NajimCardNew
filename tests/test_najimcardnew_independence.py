"""NajimCardNew must install and store data independently of the original NajmCard."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


class IndependentProductContract(unittest.TestCase):
    def test_distinct_windows_installer_identity(self):
        iss = read("installer/NajmCard.iss")
        self.assertIn("AppId={{CF6AAB62-DA00-49AF-B3D2-1DD83FF90143}", iss)
        self.assertIn("AppName=Najm Card New", iss)
        self.assertIn(r"DefaultDirName={localappdata}\Programs\NajmCardNew", iss)
        self.assertIn("DefaultGroupName=Najm Card New", iss)
        self.assertNotIn("8498D687-CB0C-4984-A140-8432B88394D0", iss)

    def test_new_archives_and_installers_for_both_architectures(self):
        package = read("scripts/package.ps1")
        ci = read(".github/workflows/windows-ci.yml")
        for name in (
            'NajimCardNew-v$Version-$Arch-Portable.zip',
            'NajimCardNew-v$Version-$Arch-Installer',
            'NajimCardNew-v$Version-$Arch-Installer.exe',
        ):
            self.assertIn(name, package)
        self.assertIn("arch: [x86, x64]", ci)
        self.assertIn("needs: [windows-core, windows-release]", ci)
        self.assertIn("NajimCardNew-v0.1.0-", ci)

    def test_independent_user_data_and_licensing(self):
        paths = [
            "src/NajmCard.Desktop/Services/VoucherStore.cs",
            "src/NajmCard.Desktop/Services/SavedRouterConnectionStore.cs",
            "src/NajmCard.Desktop/Services/Licensing/LocalTrialService.cs",
            "src/NajmCard.Desktop/Services/Licensing/PaidLicenseClient.cs",
        ]
        for path in paths:
            with self.subTest(path=path):
                self.assertIn('"NajmCardNew"', read(path))
        self.assertIn('"najmcardnew.db"', read(paths[0]))
        self.assertIn(r"Software\NajmCardNew\Licensing", read(paths[2]))
        self.assertNotIn(r"Software\NajmCard\Licensing", read(paths[2]))

    def test_original_routeros_code_remains_shared_in_functionality(self):
        # The copy deliberately keeps the existing router-service types and printing flow.
        self.assertIn("class RouterOsClient", read("src/NajmCard.Core/Services/RouterOsClient.cs"))
        self.assertIn("class ResilientRouterSession", read("src/NajmCard.Core/Services/ResilientRouterSession.cs"))


if __name__ == "__main__":
    unittest.main()
