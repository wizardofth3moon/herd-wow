using HerdWowInstaller;

try
{
    Application.EnableVisualStyles();
    Application.SetCompatibleTextRenderingDefault(false);
    Application.SetHighDpiMode(HighDpiMode.SystemAware);
    Application.Run(new InstallerForm());
}
catch (Exception ex)
{
    var logPath = Path.Combine(AppContext.BaseDirectory, "installer-crash.log");
    File.WriteAllText(logPath, $"{DateTime.Now}\n{ex}\n");
    MessageBox.Show($"Installer crashed:\n\n{ex.Message}\n\nDetails written to installer-crash.log",
        "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
}
