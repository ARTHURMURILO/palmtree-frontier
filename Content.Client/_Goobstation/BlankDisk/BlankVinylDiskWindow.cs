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
        MinSize = new System.Numerics.Vector2(420, 420);
        SetSize = new System.Numerics.Vector2(460, 520);

        var box = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
            VerticalExpand = true,
        };

        box.AddChild(new Label
        {
            Text = Loc.GetString("blank-vinyl-disk-window-hint"),
            Margin = new Thickness(2, 2, 2, 6),
        });

        var list = new ItemList
        {
            VerticalExpand = true,
            HorizontalExpand = true,
        };

        var entries = new List<ItemList.Item>(options.Count);
        foreach (var (_, name) in options)
        {
            entries.Add(new ItemList.Item(list)
            {
                Text = name,
                Selectable = true,
            });
        }

        list.SetItems(entries);
        list.OnItemSelected += args =>
        {
            if (args.ItemIndex >= 0 && args.ItemIndex < options.Count)
                OnSelected?.Invoke(options[args.ItemIndex].Id);
        };

        box.AddChild(list);
        ContentsContainer.AddChild(box);
    }
}
