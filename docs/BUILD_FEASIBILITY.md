# APK Build Feasibility Check

**Date:** 2026-10-04
**Locked baseline:** `4d04441b16c17f89f652796c573097b353897236` (M3)
**Scope of this document:** can a *real* Android APK be produced from this baseline inside the
environment this project is currently being worked in? Nothing else. No gameplay was touched and
M4 was not started.

**Verdict: an APK cannot be built in this environment. No APK was produced, and no placeholder
APK file was created.** The build is blocked by missing, non-installable Unity + Android toolchain
and by the absence of a Unity licence — not by anything in the repository.

---

## 1. Baseline verification

| Check | Result | Evidence |
|---|---|---|
| Local `HEAD` | ✅ `4d04441b16c17f89f652796c573097b353897236` | `git rev-parse HEAD` |
| Local tree hash | `32746cbcd1cda05a991b45489acf6a4c3826ad34` | `git rev-parse HEAD^{tree}` |
| Remote ref holding the baseline | ✅ `refs/heads/arena/01a10203-project-aether` | `git ls-remote origin` |
| Remote tree hash | `32746cbcd1cda05a991b45489acf6a4c3826ad34` (identical) | `git rev-parse origin/arena/01a10203-project-aether^{tree}` |
| Working tree clean | ✅ 0 changed paths | `git status --porcelain` |
| Local checkout == pushed baseline | ✅ empty diff | `git diff HEAD origin/arena/01a10203-project-aether` |
| Commit timestamp | `2026-10-04 10:34:22 +0000`; repo `pushedAt` `2026-10-04T10:34:25Z` | `git show -s`, `gh repo view` |
| Declared editor version | ✅ `6000.3.24f1` rev `4e7b9b5b6244` | `ProjectSettings/ProjectVersion.txt` |
| Repository size at baseline | 210 files, 58 C#, 0.68 MB of blobs | `git ls-tree -r -l 4d04441` |

**Baseline note (unrelated to the build, recorded for accuracy):** `origin/main` points at
`561f3cf` (`Initial commit`) — an *unrelated root commit* that is neither an ancestor nor a
descendant of the baseline. The M3 baseline `4d04441` is itself a root commit and lives on
`origin/arena/01a10203-project-aether`. "M3 is complete and pushed" is therefore **verified on the
Arena branch**, and is **not** on `main`. Nothing was changed on `main` or on the M3 baseline
branch during this check.

### What *does* run here

The repository's own static gates execute and pass, which confirms the baseline is intact and
self-consistent (this says nothing about Unity compilation):

```
$ python3 tools/verify/verify.py
  ok    meta: 110 asset(s) each have a .meta, no empty folders
  ok    guid: 110 unique asset guid(s); 10 asset(s) scanned
  ok    syntax: brackets balance in 58 file(s)
  ok    asmdef: 5 assembly definition(s), references and cycles OK
  ok    asset: 5 serialized asset(s) reference fields that exist
  ok    symbol: resolved 1050 member reference(s) on project types
  ok    lint: no deprecated Unity 6 API usage found
files analysed : 58   types modelled : 100   failures : 0   warnings : 0
VERIFICATION PASSED
```

---

## 2. Build environment inspection

| Requirement | Present in this environment? | Detail |
|---|---|---|
| Unity Editor `6000.3.24f1` | ❌ | No Unity/UnityHub binary anywhere; no install directory |
| Android Build Support module | ❌ | Not installed |
| Android SDK (`sdkmanager`, `aapt2`, `zipalign`, `apksigner`) | ❌ | None found on disk |
| Android NDK | ❌ | None found on disk |
| JDK | ❌ | No `java`/`javac`; no `/usr/lib/jvm`; no `openjdk` package |
| Gradle | ❌ | Not present |
| `adb` | ❌ | Not present |
| Unity licence (`.ulf`, serial, or Unity account) | ❌ | No licence file anywhere; no Unity credentials in the environment |
| Build automation in repo (batchmode scripts, CI workflows) | ❌ | No `.github/` directory, 0 workflows, no `-batchmode`/`-executeMethod` scripts |
| Android project config in repo | ✅ | `applicationIdentifier` = `com.DefaultCompany.ProjectAether`, `AndroidMinSdkVersion` 25, `AndroidTargetSdkVersion` 0 (→ latest), `AndroidTargetArchitectures` 3 (ARMv7+ARM64); **no keystore configured** |
| Build inputs in repo | ✅ | `EditorBuildSettings` lists one scene: `Assets/Aether/Scenes/Boot.unity` |
| Host resources | ⚠️ | 2 vCPU, **3.8 GiB RAM, 0 B swap**, 20 GB free disk (21 GB total), Debian 12 x86_64 |

---

## 3. Blockers (with the exact evidence)

### B1 — The Unity distribution hosts are unreachable from this sandbox

Network egress is allowlisted. GitHub, the npm registry and PyPI are reachable; every host needed to
obtain Unity or Android build tooling is reset at the TLS handshake.

| Host | Result | Meaning |
|---|---|---|
| `github.com` | HTTP 200 | control — reachable |
| `registry.npmjs.org` | HTTP 200 | control — reachable |
| `pypi.org` / `files.pythonhosted.org` | HTTP 200 / 404 (host up) | control — reachable |
| `download.unity3d.com` (Editor 6000.3.24f1, rev `4e7b9b5b6244`) | `OpenSSL SSL_connect: SSL_ERROR_SYSCALL`, HTTP 000, **0 bytes**, no file created | Unity Editor unobtainable |
| `packages.unity.com` | SSL reset (HTTP 000) | Package resolution for first import unobtainable |
| `services.api.unity.com` | SSL reset (HTTP 000) | Unity release API unobtainable |
| `license.unity3d.com` | SSL reset (HTTP 000) | Licence activation unobtainable |
| `unity.com`, `beta.unity3d.com` | SSL reset (HTTP 000) | Unity Hub/download pages unobtainable |
| `dl.google.com` (Android `repository2-3.xml`, `commandlinetools-*.zip`) | SSL reset (HTTP 000) | Android SDK/NDK unobtainable |
| `deb.debian.org` (`apt update`) | Connection failed (HTTP 000) | `apt` cannot install anything |

DNS resolves correctly for all of these (e.g. `download.unity3d.com → 23.53.122.x`), and TCP connects
succeed — the failure is a TLS layer reset, i.e. deliberate egress filtering, not a typo, a DNS
problem or a transient outage. Three consecutive rounds, interleaved with working control hosts,
reproduced it identically every time.

Concrete attempt, run and recorded:

```
$ curl -L --max-time 25 -o /tmp/unity_editor_probe.bin \
    https://download.unity3d.com/download_unity/4e7b9b5b6244/LinuxEditorInstaller/Unity.tar.xz
curl: (35) OpenSSL SSL_connect: SSL_ERROR_SYSCALL in connection to download.unity3d.com:443
result: HTTP 000 | downloaded 0 bytes
local file: NOT CREATED
```

No alternative distribution channel exists: `unity-editor` and `unity3d-editor` are **404 on npm**,
`apt-cache` has no Unity package (and the Debian mirrors are blocked anyway), and fetching a Unity
Editor from an unofficial third-party mirror is not an acceptable route for this project and was not
attempted.

### B2 — No Unity licence, and no way to activate one

Even if a Unity Editor binary were present, **Unity 6 requires licence activation** for batch-mode
builds, and this environment has no licence file (`*.ulf`), no serial, and no Unity account
credentials. Activation endpoints (`license.unity3d.com`) are additionally unreachable (B1). A build
would fail at startup for licensing, before compiling a single script.

### B3 — Android SDK/NDK absent and unobtainable

An Android APK requires the SDK (`aapt2`, `d8`, `zipalign`, `apksigner`) and, for the project's Unity
default Android scripting backend, the **IL2CPP toolchain + NDK**. None of these are installed, the
only legitimate source (`dl.google.com`) is blocked, and the third-party npm package named
`android-sdk` (version `1.0.0`) is not the Android SDK. Gradle itself is also absent.

### B4 — JDK: obtainable in principle, but not sufficient

The one missing piece that *can* be fetched from a reachable index is a JDK — `jdk4py` ships a real
OpenJDK build as a PyPI wheel (35,411,575 bytes downloaded successfully in this session). This does
**not** unblock anything: an APK still needs the Unity Editor (B1), a licence (B2) and the Android
SDK/NDK (B3). It is recorded only for completeness of the inventory.

### B5 — Host resources are below the practical floor for a Unity/IL2CPP Android build

2 vCPU, **3.8 GiB RAM with no swap**, and 20 GB free disk. The Unity 6 Linux Editor alone is several
GB compressed and roughly 10 GB installed, Android Build Support adds a few GB more, and first import
adds a `Library/` — all into ~20 GB. IL2CPP then needs multiple GB of RAM during C++ compilation
(Unity's own guidance for IL2CPP build machines is 16 GB RAM, i.e. ~4× what exists here, with a
recommended >= 25 GB of free disk for an Android project). Exact archive sizes could **not** be
measured, because B1 makes the downloads impossible; the figures above are estimates from Unity
documentation, not observed values. So even in a hypothetical fully-open network, a build on this
host would be at high risk of failing on disk exhaustion or OOM, and slow (2 cores).

### B6 — No CI pipeline exists to shift the build off this sandbox

The repository has **zero workflows** and no `.github/` directory, i.e. nothing currently schedules a
build anywhere. With the GitHub connection available in this session, the Actions administration
endpoints return `403 Resource not accessible by integration`, so whether Actions is even enabled for
this private repository **could not be verified**, and secrets (a Unity licence would be required,
since Unity refuses to activate a Personal licence in CI) could not be read or configured from here.

---

## 4. Can a real APK be delivered as a downloadable artifact?

**Capability: partly possible in principle; irrelevant right now, because no APK can be produced.**

- Workspace artifacts: files created here persist and can be handed over — so *if* a build produced
  `*.apk`, delivering it would be straightforward. Nothing to deliver: no APK exists.
- GitHub Releases: `gh` is authenticated and reads work; **creating a release was deliberately not
  tested** (it would be a real side effect on the user's repository for a hypothetical payload), so
  release-asset upload is recorded as **NOT VERIFIED**. A successful push implies `Contents: write`,
  which release creation requires, but that is an inference, not a measurement.
- GitHub Actions artifacts: unusable as a delivery path until B6 is resolved.

---

## 5. Status of previously unverified claims

Nothing in this check changed them; they remain exactly as they were:

| Item | Status |
|---|---|
| Unity import of this project | **NOT VERIFIED** |
| C# compilation by a real compiler | **NOT VERIFIED** |
| Play Mode run | **NOT VERIFIED** |
| Android build / APK | **NOT VERIFIED** — and currently **BLOCKED** by B1–B3 |
| IL2CPP or device run | **NOT VERIFIED** |
| Static gates (`tools/verify/verify.py`) | ✅ **VERIFIED** — executed in this session on the baseline, 0 failures, 0 warnings (output in §1) |

No APK, no stub, no placeholder, and no artefact of any kind was fabricated. The only file added by
this task is this document.

---

## 6. What would actually unblock an APK (options, none of them executed here)

1. **A machine with Unity Hub and a licence** (developer workstation, or a cloud VM where the Unity
   CDN is not filtered): install **Unity `6000.3.24f1`** with *Android Build Support* (incl. OpenJDK
   and Android SDK & NDK Tools), open the project, let first import resolve packages from
   `packages.unity.com`, then **File → Build Profiles → Android → Build**. This is also the first
   real test of Unity import and C# compilation — the two gates no static tool can cover.
2. **CI with a Unity licence** (GameCI-style `unity-builder` image): requires a Unity
   Personal/Pro serial or licence file as a repository secret, plus Actions enabled. Requires the
   GitHub connection to be re-authorised with permission to manage Actions, or the workflow and
   secret to be added through the web UI.
3. **A self-hosted runner** on a licensed machine: strongest option for reproducibility, but it
   still needs option 1's toolchain underneath.

All three need a **Unity licence** and **network access to Unity's and Google's hosts** — the two
conditions this environment cannot satisfy. Until then, any APK claim for this project is
unfounded, and the honest status of the Android milestone is *blocked, not attempted*.

### Android build prerequisites worth pre-empting (found while inspecting, not M4 work)

- `AndroidKeystoreName`/`AndroidKeyaliasName` are empty: the only achievable output today would be a
  **debug-signed** APK. A release APK needs a keystore, which is not in the repository (correctly —
  keystores must never be committed) and does not exist anywhere yet.
- `AndroidTargetSdkVersion: 0` means "highest installed", so the target API level of any future APK
  is decided by the SDK that gets installed, not by the repository.
- `companyName: DefaultCompany` and `applicationIdentifier: com.DefaultCompany.ProjectAether` are
  Unity placeholders and would ship as-is in a first APK.
