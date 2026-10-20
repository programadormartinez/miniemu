namespace MiniEmu;

static class Program
{
    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(initiallyOwned: true, @"Local\MiniEmu.SingleInstance", out bool isFirstInstance);
        ApplicationConfiguration.Initialize();

        if (!isFirstInstance)
        {
            TaskDialog.ShowDialog(new TaskDialogPage
            {
                Caption = "MiniEmu",
                Heading = "MiniEmu ya está abierto",
                Text = "Buscá el ícono en la bandeja del sistema o abrí el menú con el atajo (Alt + Shift + E por defecto).",
                Icon = TaskDialogIcon.Information,
                Buttons = { TaskDialogButton.OK },
            });
            return;
        }

        // Las continuaciones de los await vuelven al hilo de la UI.
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        Application.Run(new TrayApp());
    }
}
