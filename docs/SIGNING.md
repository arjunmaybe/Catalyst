# Windows code signing (prerequisites, no secrets committed)

Status for the first public beta: **unsigned**. The beta installer builds
and installs without a certificate; Windows SmartScreen will warn on
download/launch until the binary builds reputation or a certificate is
configured. That warning is expected — tell testers not to bypass it on
machines they care about, and use a clean VM.

No certificates, passwords, thumbprints, or account IDs are committed to
this repository. Only secret *names* and config *keys* are documented
here. Do not invent values.

## 1. What signing changes

- Signed: installer + app exe carry an Authenticode signature; with
  enough reputation SmartScreen stops warning; required for Microsoft
  Store listing.
- Unsigned (current beta): fully installable, but browsers/SmartScreen
  flag it ("Unknown publisher"). This does not mean the binary is
  malicious — it means Windows cannot tie it to a known publisher yet.

Since 2024, EV certificates no longer grant instant SmartScreen bypass;
EV and OV build reputation the same way. Budget for a warning period on
the first signed releases either way.

## 2. Prerequisites (you must provide these outside the repo)

1. A **code-signing** certificate from a Microsoft-trusted CA (SSL certs
   do not work), OR an Azure Trusted Signing account (recommended for
   new setups — see Section 5).
2. For PFX flow: the certificate exported as `.pfx` plus its export
   password. Create it with, e.g.:
   `openssl pkcs12 -export -in cert.cer -inkey private-key.key -out certificate.pfx`
3. `tauri.conf.json` signing fields (already present as placeholders):
   `bundle.windows.certificateThumbprint`, `bundle.windows.digestAlgorithm`
   (usually `sha256`), `bundle.windows.timestampUrl` (your CA's RFC 3161
   server), or `bundle.windows.signCommand` for custom tooling.
4. Windows SDK `signtool.exe` on any machine that signs locally.

If you hold an OV certificate issued **before 2023-06-01**, Tauri's
native thumbprint flow applies directly (import the PFX into
`Cert:\CurrentUser\My`, set the three fields above, run `tauri build`).
For OV/EV certificates issued **after** that date, follow your CA's
current hardware-token/cloud-HSM instructions instead of the legacy PFX
import, then wire the result through `signCommand`.

## 3. Configuring Tauri to sign (when you are ready)

Local (single machine that holds the cert):

```json
"windows": {
  "certificateThumbprint": "A1B1… (from certmgr.msc → Personal → Details → Thumbprint)",
  "digestAlgorithm": "sha256",
  "timestampUrl": "http://timestamp.comodoca.com (or your CA's server)"
}
```

Then `npx tauri build` should log `Successfully signed: …`.

Custom tooling (Azure Key Vault via `relic`, cross-platform signing,
post-2023 tokens):

```json
"windows": {
  "signCommand": "relic sign --file %1 --key azure --config relic.conf"
}
```

Azure Trusted Signing (recommended for new certificates):

```json
"windows": {
  "signCommand": "artifact-signing-cli -e https://<region>.codesigning.azure.net -a <Account> -c <Profile> -d Catalyst %1"
}
```

Keep exactly one mechanism active. The beta ships with all four keys
`null`, which means "do not sign" and keeps unsigned builds working.

## 4. GitHub Actions wiring (secret names only)

`.github/workflows/release.yml` already contains an optional step that
imports a PFX when these repository secrets exist; otherwise it skips:

- `WINDOWS_CERTIFICATE` — base64-encoded `.pfx`
  (`certutil -encode certificate.pfx base64cert.txt`).
- `WINDOWS_CERTIFICATE_PASSWORD` — the PFX export password.

For Azure flows, add (names only; values stay in GitHub Secrets):

- `AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET`, `AZURE_TENANT_ID`
  (Key Vault / Trusted Signing identity).

Give the workflow `contents: write` (already set) so `tauri-action` can
create the draft release. Never log secret values; the workflow only
references `${{ secrets.… }}`.

## 5. Recommended path for Catalyst

1. Ship the first beta **unsigned** as a draft pre-release (current
   plan) and complete `docs/BETA-CHECKLIST.md`.
2. In parallel, open an Azure Trusted Signing account + certificate
   profile (no new PFX handling, works from hosted runners).
3. Test-sign a throwaway build, verify (Section 6), then set
   `bundle.windows.signCommand` to the Trusted Signing command and add
   the three `AZURE_*` secrets.
4. Keep signing every subsequent release with the same identity so
   SmartScreen reputation accumulates. Optionally submit the installer to
   Microsoft Defender Submission (`wdsi/filesubmission`) for review.

## 6. Verifying a signature

```powershell
Get-AuthenticodeSignature "src-tauri/target/release/bundle/nsis/Catalyst_*_x64-setup.exe" | Format-List *
& "C:\Program Files (x86)\Windows Kits\10\bin\10.0.22621.0\x64\signtool.exe" verify /pa /v "…setup.exe"
```

Expect `Valid` / `Successfully verified`. An unsigned beta reports
`NotSigned` / `No signature found` — that is the pre-signing baseline,
not a build failure.

References: `https://tauri.app/distribute/sign/windows/` (OV/PFX, Azure
Key Vault, custom `signCommand`, Trusted Signing),
`https://tauri.app/distribute/pipelines/github/` (`tauri-action`).
