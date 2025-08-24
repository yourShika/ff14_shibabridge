using System.Numerics;

namespace ShibaBridge.UI.Components.Popup;

public interface IPopupHandler
{
    Vector2 PopupSize { get; }
    bool ShowClose { get; }

    void DrawContent();
}