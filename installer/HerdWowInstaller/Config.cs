namespace HerdWowInstaller;

public static class Config
{
    public const string ServerName      = "Herd WoW";
    public const string DefaultInstallPath = @"C:\Herd WoW";
    public const string LauncherExeName = "WowLauncher.exe";
    public const string ShortcutName   = "Herd WoW";

    // GitHub release — same release the launcher's patch manager uses
    public const string ReleaseBaseUrl =
        "https://github.com/wizardofth3moon/herd-wow/releases/download/patches-v1";

    // Lists the split client archive parts (base client is too big for one GitHub
    // asset) — see client-manifest.json alongside the launcher's manifest.json.
    public const string ClientManifestUrl = ReleaseBaseUrl + "/client-manifest.json";

    public const string LauncherDownloadUrl = ReleaseBaseUrl + "/WowLauncher.exe";
}
