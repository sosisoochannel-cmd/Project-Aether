# Getting an APK out of GitHub Actions

`/.github/workflows/android-build.yml` builds an Android APK for this project on GitHub's own
runners. No PC, no Unity installation and no Android SDK are needed on anyone's machine: the
editor, the Android build support, the SDK/NDK and the JDK all exist inside the job for its
duration and then disappear.

**Status: no APK has been produced yet.** The plumbing has been exercised once and behaved
correctly, but the credentials below did not exist at that moment, so the build was skipped by
design. Nothing in this repository should claim otherwise until a real run uploads an artifact.

## The one manual step

Add a Unity account's credentials as repository secrets:

1. Open the repository on GitHub → **Settings** → **Secrets and variables** → **Actions**.
2. **New repository secret**, twice:
   - `UNITY_EMAIL` — the Unity account email.
   - `UNITY_PASSWORD` — its password.

That is the whole setup. This is also the whole of the licensing: with only these two present,
GameCI's CLI resolves the strategy itself (`file -> serial -> floating -> personal`) and activates
a real, account-bound Unity **Personal** seat inside the container, returning it when the job
ends. No `.ulf`, no serial key, no license file in the repository, and no bypass of any kind. The
`.ulf`-based instructions still present in GameCI's own documentation describe the older manual
route and are not required for this workflow.

Secrets are stored by GitHub and injected into the job environment only; they never appear in the
repository, in the workflow file, or in the logs. The workflow never prints them — it only reports
whether they exist.

## Running it and getting the file

- Trigger: push to `main` or `arena/01a10203-project-aether`, or **Actions** → **Android APK** →
  **Run workflow**. Pushes that only touch documentation are ignored, so a README edit does not
  spend fifteen minutes of build time.
- Download: **Actions** → the run → **Artifacts** → `Aether-dev-apk`. The artifact arrives as a
  zip containing `Aether-dev.apk`; the run summary also lists its size and SHA-256.
- The APK is debug-signed, which is the correct signing for a test build. A keystore for release
  signing is out of scope and must never be committed.

## Reading the outcome correctly

A run that reports **success** while the `Unity 6000.3.24f1 -> Aether-dev.apk` job is listed as
**skipped** built nothing: the `preflight` job found the credentials missing and deliberately
refused to spend a Unity container on a build that could not license itself. A run that produces
an APK is one where that job actually ran and the **Upload APK** step succeeded.

If the build job does run and fails, the real error is in the **Build APK** step's log. The two
failures worth knowing about up front:

- **A missing container image.** GameCI's measured compatibility matrix currently tops out below
  this project's Unity version, so whether a prebuilt image exists for `6000.3.24f1` is not
  verified from here. If the job cannot pull one, the fix is to install the editor inside the job
  instead (`unityhub --headless install --version 6000.3.24f1 --changeset 4e7b9b5b6244 --module
  android`, then build with the resulting local Editor). That fallback is not implemented yet — it
  is the plan B if the first real run needs it.
- **License activation refused.** If the Unity account has two-factor authentication enabled, this
  credentials-only activation path may not be enough; none of the current GameCI documentation
  covers 2FA, so this is a known unknown rather than a known failure.

## Cost

GitHub Actions is free for public repositories; this one is private and metered, which is still
well inside the free tier: 2,000 Linux minutes per month for a personal account, against roughly
10–30 minutes per Android build (the first real run will give the true number). The job also
caches Unity's `Library` folder, so consecutive builds import far less. Nothing here needs a paid
service. A run that is cancelled mid-build can leave the Personal seat bound to a machine that no
longer exists, so the workflow lets a newer push queue behind an older run instead of killing it;
a leaked seat is released by hand at id.unity.com.
