using System.Drawing;

namespace Auro_OSD;

public static class AppIcon
{
    private static Icon? _icon;

    public static Icon Get()
    {
        if (_icon != null) return _icon;

        try
        {
            _icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        }
        catch
        {
            _icon = SystemIcons.Application;
        }

        return _icon;
    }
}
