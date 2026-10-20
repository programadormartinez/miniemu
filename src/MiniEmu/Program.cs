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
            MessageBox.Show(
                "MiniEmu ya está abierto. Buscá el ícono en la bandeja del sistema o usá el atajo para abrir el menú.",
                "MiniEmu", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Las continuaciones de los await vuelven al hilo de la UI.
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        Application.Run(new TrayApp());
    }
}
