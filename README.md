# herd-wow

Release host for the Herd Android apps.

- **Releases:** signed APKs for each app (tagged `<app>-v<version>`, e.g. `ocarina-v0.4`).
- **`store/`:** the catalog (`catalog.json`) and icons that the Herd Store app reads to install and update the other apps.
- **`installer/`:** installer files.

The app sources live in separate private repositories. Please don't move or rename files here: the Store fetches them by path.
