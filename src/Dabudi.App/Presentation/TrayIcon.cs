using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace Dabudi.Presentation;

public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Drawing.Icon _image;
    private readonly Forms.ContextMenuStrip _menu;

    public TrayIcon(MainWindow window, AppController controller)
    {
        using var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"))?.Stream
            ?? throw new IOException("Не удалось загрузить значок dabudi.");
        using var icon = new Drawing.Icon(resource);
        _image = (Drawing.Icon)icon.Clone();
        _menu = new Forms.ContextMenuStrip();
        _menu.Items.Add("Открыть dabudi", null, (_, _) => window.Dispatcher.BeginInvoke(new Action(window.Restore)));
        _menu.Items.Add("Остановить все инструменты", null, (_, _) => window.Dispatcher.BeginInvoke(new Action(() => controller.Run(AppAction.StopAll))));
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add("Выход", null, (_, _) => window.Dispatcher.BeginInvoke(new Action(() => controller.Run(AppAction.Exit))));
        _icon = new Forms.NotifyIcon { Icon = _image, Text = "dabudi", ContextMenuStrip = _menu, Visible = true };
        _icon.DoubleClick += (_, _) => window.Dispatcher.BeginInvoke(new Action(window.Restore));
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
        _image.Dispose();
    }
}
