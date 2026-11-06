# Releasing and code signing

Status: research notes, November 2026. Nothing here has been set up yet.

## 1. Releasing today (unsigned)

The release pipeline is already in place (`.github/workflows/release.yml`):

1. Make sure `main` is pushed and the CI build is green.
2. Set the version in `Directory.Build.props` (it's `1.0.0` now).
3. Tag and push:
   ```powershell
   git tag v1.0.0
   git push origin v1.0.0
   ```
4. GitHub Actions then runs the tests, publishes the self-contained app, builds `DeoLedMatrixManager-Setup-1.0.0.exe` with Inno Setup, and creates a GitHub Release with generated notes.
5. Check the release page and edit the notes. Tick "pre-release" while it's 0.x.

**What unsigned means for users:**

| Windows setting | What the user sees |
|---|---|
| SmartScreen (everyone) | "Windows protected your PC". Clicking **More info → Run anyway** works. |
| Smart App Control **On** | The installer and the app's DLLs are blocked outright, with no way to override (the same 0x800711C7 error we hit in development). |
| Company PCs | Often blocked by policy. |

## 2. What has to be signed

- **The app's own files in the publish folder:** `DeoLedMatrixManager.exe`, `DeoLedMatrixManager.dll`, `LedMatrix.Core.dll`. Smart App Control checks each DLL, not just the .exe.
- **The .NET runtime files:** the self-contained publish copies them in, but Microsoft has already signed them, so leave them alone.
- **The installer:** `DeoLedMatrixManager-Setup-x.y.z.exe`.
- **The uninstaller:** Inno Setup creates it while it compiles the installer, so signing the finished setup .exe doesn't cover it. Inno's `SignTool` setting plus `SignedUninstaller=yes` signs it during compilation.
- **Timestamps:** every signature gets an RFC 3161 timestamp, so it stays valid after the certificate expires.

## 3. Options

| Option | Cost | Who can use it | Publisher shown | Notes |
|---|---|---|---|---|
| **Azure Artifact Signing** (formerly Trusted Signing) | About $9.99/month (Basic: 5,000 signatures/month) | Individuals in the **US and Canada** only | Your legal name | No hardware token. Official GitHub Action. Identity check takes 1–20 business days. |
| **SignPath Foundation** | Free for OSS | Open-source projects that meet its terms (OSI license, no proprietary code, built by CI from the public repo, MFA for the team) | **"SignPath Foundation"** | Each release needs a manual approval on SignPath. The website needs a "code signing policy" section. |
| OV certificate (DigiCert, Sectigo, Certum…) | About $150–300/year | Anyone | Your name | Since June 2023 the key must be on a hardware token or HSM, which makes CI signing awkward. |
| EV certificate | $400+/year | Companies | Company name | Since 2024 no longer skips SmartScreen; no advantage for us. |
| Self-signed | Free | — | — | Only for testing; useless for the public. |
| Microsoft Store (MSIX) | Free (individual registration has been free since 2025) | Anyone | Your name | The Store signs the package for you. Needs MSIX packaging, and startup has to move from the Run key to the MSIX StartupTask. A later project. |

A signature alone doesn't make SmartScreen trust a new certificate straight away. Warnings fade as downloads build reputation. Smart App Control accepts files signed by a certificate from a CA in Microsoft's Trusted Root Program unless Microsoft has flagged them, and Artifact Signing and SignPath both qualify.

## 4. Recommendation

1. **Now:** release v1.0.0 unsigned, so people can try it.
2. **Next:** set up **Azure Artifact Signing** if you live in the US or Canada. It's the cheapest way to have *your* name as the publisher, it needs no hardware, and it plugs straight into the existing release workflow. If you'd rather not pay, apply to **SignPath Foundation**. The trade-offs are the publisher name and a manual approval for each release.
3. **Microsoft Store:** in progress. See [STORE.md](STORE.md). The Store signs its copy, so Store installs work even with Smart App Control on.
4. **Later, optional:** a winget manifest for `winget install`.

## 5. Artifact Signing setup checklist

**One time, done by you in the Azure portal:**

1. Create an Azure account (pay-as-you-go). The billing account must be **Account type: Individual**, and its legal name and sold-to address must **exactly match your government ID**. People on Microsoft Q&A report validations failing over small address differences.
2. Register the `Microsoft.CodeSigning` resource provider, then create an **Artifact Signing account** (Basic tier) in a region such as East US. That region's endpoint is `https://eus.codesigning.azure.net/`.
3. Give yourself the **Artifact Signing Identity Verifier** role. Then go to **Identity validations → New identity → Public → Individual** and finish the verification from the link in your email. The ID document must have been issued in the last 12 months and must not expire within 2 months.
4. When the validation shows **Completed**, create a **Public Trust certificate profile**.
5. Create an **Entra app registration** for GitHub Actions:
   - Add a federated credential (OIDC) for the repo `Deovandski/Deo-s-Framework16-LED-Matrix-Manager`. Scope it to tags, or to an environment named `release`.
   - Give it the **Artifact Signing Certificate Profile Signer** role on the certificate profile.
6. Add the GitHub repository secrets `AZURE_CLIENT_ID`, `AZURE_TENANT_ID` and `AZURE_SUBSCRIPTION_ID`. Note your account name and profile name too.

**Changes to `release.yml` once the checklist is done:**

- Give the job `permissions: id-token: write` and add an `azure/login` step using OIDC.
- After `dotnet publish`, sign the three app files with `azure/artifact-signing-action`, passing `endpoint`, `signing-account-name`, `certificate-profile-name`, `files-folder`, `files-folder-filter`, `file-digest: SHA256`, `timestamp-rfc3161: http://timestamp.acs.microsoft.com` and `timestamp-digest: SHA256`.
- Build the installer with Inno's `SignTool` pointing at the same signing service, so both the installer and the uninstaller are signed.
- Check the result with `signtool verify /pa /v` before uploading the release.

## Sources

- Microsoft Learn, [Code signing options for Windows apps](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options)
- Microsoft Learn, [Quickstart: Set up Artifact Signing](https://learn.microsoft.com/en-us/azure/artifact-signing/quickstart)
- [Azure/artifact-signing-action](https://github.com/Azure/artifact-signing-action)
- [Artifact Signing pricing](https://azure.microsoft.com/pricing/details/artifact-signing/)
- [SignPath Foundation](https://signpath.org/) and its [terms](https://signpath.org/terms)
- Windows Developer Blog, [Free developer registration for individual developers on Microsoft Store](https://blogs.windows.com/windowsdeveloper/2025/09/10/free-developer-registration-for-individual-developers-on-microsoft-store/)
- Microsoft Q&A, [Individual identity validation fails](https://learn.microsoft.com/en-us/answers/questions/6039284/artifact-signing-public-individual-identity-valida)
