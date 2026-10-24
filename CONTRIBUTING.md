# Contributing

This project uses Gitflow. Automation enforces it, so a pull request that breaks the flow fails its checks.

## Branches

| Branch | Purpose | Comes from | Goes to |
| --- | --- | --- | --- |
| `main` | Tagged releases only | | |
| `develop` | Integration | | |
| `feature/<name>` | New work | `develop` | `develop` |
| `bugfix/<name>` | Fixes during development or on a release branch | `develop` or `release/*` | `develop` or `release/*` |
| `release/<x.y.z>` | Stabilise and publish a version | `develop` | `main` |
| `hotfix/<x.y.z>` | Urgent fix for the released version | `main` | `main` |
| `backport/<x.y.z>` | Opened by automation when `main` cannot merge into `develop` cleanly | `main` | `develop` |

## Releasing

1. Branch `release/<x.y.z>` from `develop` and put `<x.y.z>` in the `VERSION` file.
2. Open a pull request into `main`. The checks require the branch name to match `VERSION`, and the version to be newer than the latest tag.
3. When it is merged, the Release workflow builds and tests, then publishes the tag `v<x.y.z>` with the installer and the portable zip, and merges `main` back into `develop`.
4. Hotfixes follow the same steps from `main`.

## Checks

- **Gitflow guard** rejects pull requests that do not follow the table above.
- **CI** builds, tests and packages on Windows.
- **Release** also refuses to publish if `main` was changed by anything other than a `release/*` or `hotfix/*` pull request.

## Packages

`scripts/Package.ps1` builds `HyruleOnline-<version>-portable.zip` and, when Inno Setup is installed, `HyruleOnline-<version>-setup.exe`. Both hold only this project's own files. Cemu, Python, BCML, Milk Bar and the game files are never bundled.
