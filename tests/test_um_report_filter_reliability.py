"""Report filters must not invent active sessions or transaction revenue."""
from pathlib import Path
import unittest

ROOT=Path(__file__).resolve().parents[1]
VIEWS=ROOT/"src/NajmCard.Desktop/Views"
SERVICES=ROOT/"src/NajmCard.Core/Services"

class UserManagerReportReliabilityContracts(unittest.TestCase):
    def test_report_uses_scoped_active_status_not_all_historical_sessions(self):
        code=(VIEWS/"UserManagerReportsView.cs").read_text(encoding="utf-8")
        self.assertIn("UserManagerReportFilters.CountActiveSessions(dated)",code)
        self.assertIn('scope.Known==0?"غير متاح"',code)
        self.assertNotIn("var known=sessions.Rows.Count(x=>x.IsActive.HasValue)",code)

    def test_profile_filter_excludes_unattributable_historical_session_totals(self):
        code=(VIEWS/"UserManagerReportsView.cs").read_text(encoding="utf-8")
        self.assertIn('!allPackages?"غير متاح حسب الباقة"',code)
        self.assertIn('if(allPackages)\n                DrawBars(traffic',code)
        self.assertIn('ResetChart(traffic,',code)

    def test_approved_payments_are_filtered_by_actual_payment_profile(self):
        code=(VIEWS/"UserManagerReportsView.cs").read_text(encoding="utf-8")
        core=(SERVICES/"UserManagerReportFilters.cs").read_text(encoding="utf-8")
        self.assertIn("UserManagerReportFilters.SelectApprovedPayments(",code)
        self.assertIn("paymentScope!.IsComplete",code)
        self.assertIn("payment.Profile.Trim()",core)
        self.assertIn("HasUnknownProfile",core)
        self.assertIn("HasUndatedPayments",core)
        self.assertIn("UserManagerRevenueMath.TryTotalSingleCurrency(",code)

if __name__=="__main__":
    unittest.main()
