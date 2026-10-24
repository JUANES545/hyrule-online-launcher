## What changes

<!-- Short description of the change and why. -->

## Branch flow

- [ ] The target branch follows Gitflow: `feature/*` and `bugfix/*` go to `develop`, `release/*` and `hotfix/*` go to `main`.
- [ ] Releases and hotfixes bump `VERSION` and the branch name matches it (`release/1.2.0`).

## Verification

- [ ] `scripts/Build.ps1` and `scripts/Test.ps1` pass.
- [ ] Anything that needs a controller or visible gameplay is listed below as pending, not as passed.
