# Getting an APK out of GitHub Actions

`/.github/workflows/android-build.yml` builds an Android APK for this project on GitHub's own
runners. No PC, no Unity installation and no Android SDK are needed on anyone's machine: the
editor, the Android build support, the SDK/NDK and the JDK all exist inside the job for its
duration and then disappear.

**Status: no APK has been produced yet.** What a real run has proven so far, in order:

1. The workflow registers and the credentials gate refuses to start a Unity build without them.
2. A `unityci/editor:ubuntu-6000.3.24f1-android-3` image **exists** for this project's editor
   version, is pulled, and unpacks to 19.1 GB on disk — that was open for a while, since GameCI's
   measured matrix stops below `6000.3`, and it is now settled by a run that got as far as running
   Unity's licensing client inside the container. Note what that means for the failure below: the
   editor itself never started, so nothing has been imported or compiled yet.
3. The runner's disk no longer blocks the build (see below).
4. **Unity then refused the account credentials, and that is where it stands.** It is an account-side
   problem, not a pipeline one, and it is described at the bottom of this file.

Nothing in this repository should claim a successful build until an artifact actually appears.

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

## Publishing: the APK becomes a GitHub Release

An artifact lives inside a workflow run and expires; a release asset is a stable link that a phone can
open. When a build produces an APK, the `release` job publishes it as a GitHub Release, using the
same tag and asset pattern this project's release links already use:

- tag: `apk-<short sha>` (e.g. `apk-206195e`)
- asset: `Aether_0.1.0_<short sha>.apk`
- asset: `SHA256SUMS.txt`
- direct link: `https://github.com/sosisoochannel-cmd/Project-Aether/releases/download/apk-<sha7>/Aether_0.1.0_<sha7>.apk`

That job runs **only if the build job reported an APK**, so a failed build cannot publish a release.
It re-hashes the downloaded artifact and refuses to publish if the bytes differ from what the build
hashed in its own workspace. Re-running a build for the same commit replaces the assets instead of
failing. `contents: write` is granted to the release job alone — the build job stays read-only.

Note that this repository is private, so opening that link requires being signed in to GitHub.

**Not verified:** the release path itself has never run, because no APK has ever been produced. Its
script logic (asset naming, checksum file, hash comparison, create-versus-update) is exercised
locally against a stubbed `gh`, but the first real release will be the first real test of it.

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

If the build job does run and fails, the real error is in the **Build APK** step's log, and the
run's annotations carry the headline facts (disk before/after, the image used, the APK's size and
hash if there is one) without needing the log at all.

## Runner disk space (solved, and why it was ever a problem)

The first licensed run pulled the image and died unpacking it:

```
docker: failed to register layer: write .../AndroidPlayer/Variations/il2cpp/.../libunity.so:
no space left on device
```

Not a pipeline mistake, and not something a smaller image would have fixed. GitHub hands a **private**
repository a 2-core runner with a 75 GB disk of which only **~14 GB is free**; a public repository
gets 150 GB with ~90 GB free. That is GitHub's own explanation, in
[actions/runner-images#14492](https://github.com/actions/runner-images/issues/14492) (staff reply,
2026-08-05), and it is corroborated by GameCI's troubleshooting page, which names the same
`failed to register layer` failure and lists "free up disk space" as the first remedy. Bigger runners
exist but are a paid plan feature, so cleanup is the free answer.

The `Free disk space` step therefore removes only host-side toolchains that a *container* build
cannot use — the host Android SDK/NDK, the tool cache (with CodeQL), GHC/GHCup, .NET and Boost —
prunes Docker, and escalates to the slower package sweep from GameCI's documented list only if
under 30 GB is still free. It never touches the workspace, `.git`, the Unity project, the actions
runtime, the Docker daemon or swap; `unity-builder` v6 is a `node24` action and this workflow calls
no `setup-*` step, so none of those toolchains are reachable from it anyway.

Measured on the first run that had it: **13 GB free before, 40 GB after, 25,960 MB reclaimed**, and
the fast path was enough — the package sweep did not have to run. Pulling and unpacking the image
costs about 18 GB, which is why 14 GB could never have been enough.

## Unity license activation (the current blocker)

The run now reaches the container and fails at activation, with GameCI's own classification as the
error annotation:

> Unity rejected the account credentials, or wants a second factor.

That message is produced by `licensing_method.sh` in `game-ci/cli`, and it fires when Unity's
licensing client reports a 2FA/device-verification challenge *or* rejects the credentials outright.
Its own advice, in order:

- `UNITY_EMAIL` / `UNITY_PASSWORD` must be a **Unity ID login with a password**, not a
  Google/Facebook/Apple social login — those have no password to give.
- The account must have **two-factor authentication disabled**; headless activation cannot answer a
  challenge. (TOTP support exists only in the deprecated `unity-license-activate` fork, not in the
  path this workflow uses.)
- Unity may be **challenging an unfamiliar IP**: hosted runners change address between runs, so a
  new-device confirmation email may be waiting in the account's inbox.
- A **dedicated CI-only Unity account** is the durable fix for all three.

This is an account-side change. Nothing in the repository can or should work around it.

## The intro render job (temporary)

`Intro render (temporary)` exists for one thing the static gates cannot do: play the studio intro in
the real engine and photograph it. There is no Unity Editor anywhere near this project, so the frames
a phone would show are otherwise unverifiable — and the one bug that got past every static check was
exactly a rendering bug (the mark drawn several times the width of the screen).

It runs `game-ci test --docker --testPlatforms=playmode`, which loads `StudioIntro.unity` through the
shipped component and asserts against the captured pixels: ink present, centred, inside the frame with
margins, aspect ratio preserved, the ground black, and a reveal and an exit that change the picture.
The frames themselves are uploaded as the `intro-render` artifact, so a green run leaves a real
screenshot behind. It builds nothing, restores and saves the same `Library` cache the APK job uses,
and it is deleted once the screenshots have served their purpose.

The same job does not run on `ubuntu-latest` like the build does: the runner image is pinned, because
`ubuntu-latest` moves to Ubuntu 26 on 2026-10-19 and a Unity job is not where an OS migration should
be discovered.

## Cost

GitHub Actions is free for public repositories; this one is private and metered, which is still
well inside the free tier: 2,000 Linux minutes per month for a personal account, against roughly
10–30 minutes per Android build (the first real run will give the true number). The job also
caches Unity's `Library` folder, so consecutive builds import far less. Nothing here needs a paid
service. A run that is cancelled mid-build can leave the Personal seat bound to a machine that no
longer exists, so the workflow lets a newer push queue behind an older run instead of killing it;
a leaked seat is released by hand at id.unity.com.
