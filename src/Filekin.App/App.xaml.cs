using System.Windows;

namespace Filekin.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // The production shell is intentionally not stubbed with the stock WPF window.
        // Remove this shutdown only when the specified Filekin shell is composed.
        Shutdown();
    }
}
