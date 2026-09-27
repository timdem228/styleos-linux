using System;
using System.Security.Cryptography;
using System.Text;

namespace StyleOS
{
    public enum SignatureStatus
    {
        /// <summary>Signature present and valid, and the archive's hash matches it.</summary>
        Verified,
        /// <summary>No SHA256SUMS.txt / .sig published for this release - nothing to check.</summary>
        Unsigned,
        /// <summary>A signature or hash was published but did not check out. Treat as tampered.</summary>
        Invalid
    }

    /// <summary>
    /// Verifies a downloaded release archive against SHA256SUMS.txt + SHA256SUMS.txt.sig
    /// using the public half of a keypair StyleOS ships with. The matching private key
    /// never ships with the client - it stays on whoever actually cuts releases, used with
    /// the separate tools/ReleaseSigner tool to produce those two files before uploading a
    /// release to GitHub.
    ///
    /// Older or not-yet-signed releases simply won't have these two files, which reports as
    /// Unsigned rather than Invalid - that's what lets this be introduced without breaking
    /// every release published before signing started. Invalid is reserved for the case
    /// that actually matters: a signature or hash that doesn't check out, meaning the
    /// archive isn't what it claims to be.
    /// </summary>
    public static class ReleaseVerifier
    {
        // Public key only - safe to ship in source. Generate a real keypair with
        // tools/ReleaseSigner ('generate-keys') and paste its public.pem here; this
        // placeholder can't verify anything until that's done, so every release is
        // reported as Unsigned until a real key is in place.
        private const string PublicKeyPem = @"-----BEGIN PUBLIC KEY-----
MIIBojANBgkqhkiG9w0BAQEFAAOCAY8AMIIBigKCAYEAo8rCb8mQxwHvKGrDcG4E
nkTlPuLWX3TKYTIwtxS0K53TuoxfkMmLEtpld727QvYBs8vM7dkcoTn2CWs0Tx6Z
gY3abjkwgpxK+PHul8CyC4MXqGgVXX7pXQ/dReWMhkZOSj5uW/yWfpMi/fg0FdCd
CVEn0rE1dIAOY/MBAwX9azJPkppDMQ5UIdjCrA+p/X0JCMMD3sOezjdsDevYLUAh
MOLieO1Shb9JInBHqCb7A/JHMeJnQdnh+EOJ4IahXNc7ompXwHxLysnoFXs2d6Ci
3crRBpdIvmQt1BZ2TlMVbG4nINtqyPKRMEESKltThYlbienUlxVTWw06rZzDHP0R
iE0hm3G0GMYzF3kG8AIOVon/7lWeMdw5aOMZLaDgoK9vhKhdEWqmLpv2lFAZG1k3
Kc9WDhbpANkgKm3VD+py+adh08WqNtPoAnBdZ7z+0FhjA79VefFCjUIfvXeEhEaF
dwrQTlbunU21jb8a9f1+Rpgr6ogsrEpodVs3FFwoggetAgMBAAE=
-----END PUBLIC KEY-----";

        public static SignatureStatus Verify(byte[] archiveBytes, string archiveFileName, byte[] sumsBytes, byte[] signatureBytes)
        {
            if (sumsBytes == null || sumsBytes.Length == 0 || signatureBytes == null || signatureBytes.Length == 0)
                return SignatureStatus.Unsigned;

            try
            {
                using var rsa = RSA.Create();
                rsa.ImportFromPem(PublicKeyPem);

                bool signatureOk = rsa.VerifyData(sumsBytes, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                if (!signatureOk) return SignatureStatus.Invalid;

                string expectedHash = FindHash(Encoding.UTF8.GetString(sumsBytes), archiveFileName);
                if (expectedHash == null) return SignatureStatus.Invalid;

                string actualHash = Sha256Hex(archiveBytes);
                return string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase)
                    ? SignatureStatus.Verified
                    : SignatureStatus.Invalid;
            }
            catch
            {
                // A malformed key, a corrupt signature, anything unexpected - treat it as a
                // failed check, not a pass. Unsigned is only for "nothing was published".
                return SignatureStatus.Invalid;
            }
        }

        /// <summary>Reads the "sha256sum"-style format: "<hex>  <filename>" per line.</summary>
        private static string FindHash(string sumsText, string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return null;

            foreach (var rawLine in sumsText.Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.Length == 0) continue;

                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;

                string name = parts[1].TrimStart('*'); // sha256sum marks binary mode with a leading *
                if (string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase)) return parts[0];
            }
            return null;
        }

        private static string Sha256Hex(byte[] data)
        {
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(data);
            var sb = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
