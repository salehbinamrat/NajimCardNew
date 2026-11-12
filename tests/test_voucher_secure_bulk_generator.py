"""High-volume card creation must remain CSPRNG-strong with low per-character overhead."""
from pathlib import Path
import unittest

CODE=(Path(__file__).resolve().parents[1]/
      "src/NajmCard.Core/Services/VoucherGenerator.cs").read_text(encoding="utf-8")

class SecureBulkVoucherGenerationContracts(unittest.TestCase):
    def test_cryptographic_rng_is_filled_in_bounded_blocks_not_one_call_per_digit(self):
        self.assertIn("private readonly byte[] _buffer=new byte[4096];",CODE)
        self.assertIn("RandomNumberGenerator.Fill(_buffer);",CODE)
        self.assertIn("private sealed class SecureTokenSampler : IDisposable",CODE)
        self.assertIn("using var entropy=new SecureTokenSampler();",CODE)
        self.assertNotIn("RandomNumberGenerator.GetInt32(",CODE)

    def test_rejection_sampling_prevents_modulo_bias_and_reuses_for_passwords(self):
        self.assertIn("int acceptable=256-(256%alphabet.Length);",CODE)
        self.assertIn("if(value>=acceptable)continue;",CODE)
        self.assertIn("result[i++]=alphabet[value%alphabet.Length];",CODE)
        self.assertIn("DifferentPassword(p.PasswordCharacters,p.PasswordLength,username,entropy)",CODE)
        self.assertIn("CryptographicOperations.ZeroMemory(_buffer)",CODE)
        self.assertIn("StringComparer.OrdinalIgnoreCase",CODE)
        self.assertIn("if(!used.Add(username))continue;",CODE)

if __name__=="__main__": unittest.main()
