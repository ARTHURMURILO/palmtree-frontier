using System;
using System.Collections.Generic;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;

namespace Content.Client._Goobstation.BlankDisk;

/// <summary>
/// List of every vinyl disk the blank disk can be imprinted into.
/// </summary>
public sealed class BlankVinylDiskWindow : DefaultWindow
{
    public event Action<string>? OnSelected;

    public BlankVinylDiskWindow(List<(string Id, string Name)> options)
    {
        Title = Loc.GetString("blank-vinyl-disk-window-title");
        MinSize = new System.Numerics.Vector2(340, 420);

        var scroll = new ScrollContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
        };

        var list = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 2,
        };

        scroll.AddChild(list);
        ContentsContainer.AddChild(scroll);

        var hint = new Label
        {
            Text = Loc.GetString("blank-vinyl-disk-window-hint"),
            Margin = new Thickness(4, 4, 4, 8),
        };
        list.AddChild(hint);

        foreach (var (id, name) in options)
        {
            var button = new Button
            {
                Text = name,
                HorizontalExpand = true,
            };
            button.OnPressed += _ => OnSelected?.Invoke(id);
            list.AddChild(button);
        }
    }
}
