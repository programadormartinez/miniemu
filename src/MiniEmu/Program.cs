using System.Windows;
using Forms = System.Windows.Forms;

namespace MiniEmu;

static class Program
{
    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(initiallyOwned: true, @"Local\MiniEmu.SingleInstance", out bool isFirstInstance);
        // TaskDialog necesita los controles comunes v6 de Windows.
        Forms.Application.EnableVisualStyles();

        if (!isFirstInstance)
        {
            Forms.TaskDialog.ShowDialog(new Forms.TaskDialogPage
            {
                Caption = "MiniEmu",
                Heading = "MiniEmu ya está abierto",
                Text = "Buscá el ícono en la bandeja del sistema o abrí el panel con el atajo (Alt + Shift + E por defecto).",
                Icon = Forms.TaskDialogIcon.Information,
                Buttons = { Forms.TaskDialogButton.OK },
            });
            return;
        }

        // Una app de bandeja: no se cierra al cerrar ventanas, solo desde "Salir".
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Theme.Refresh();
        Palette.Apply(app.Resources);
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/MiniEmu;component/Styles.xaml", UriKind.Absolute),
        });

        // Un error inesperado se muestra en vez de cerrar la app en silencio.
        app.DispatcherUnhandledException += (_, e) =>
        {
            e.Handled = true;
            AppController.ShowError("Error inesperado", e.Exception.ToString());
        };

        var controller = new AppController();
        app.Run();
        GC.KeepAlive(controller);
    }
}
