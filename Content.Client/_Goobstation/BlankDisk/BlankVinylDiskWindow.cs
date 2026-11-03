using System;
using System.Collections.Generic;
using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;

namespace Content.Client._Goobstation.BlankDisk;

/// <summary>
/// One selectable record in the blank disk window.
/// </summary>
public readonly record struct BlankVinylDiskOption(string Id, string Name, Texture Icon);

/// <summary>
/// Lets the player pick which record a blank disk should turn into.
/// </summary>
public sealed class BlankVinylDiskWindow : DefaultWindow
{
    public event Action<string>? OnSelected;

    private readonly List<BlankVinylDiskOption> _options;
    private readonly List<BlankVinylDiskOption> _filtered = new();
    private readonly ItemList _list;
    private readonly LineEdit _search;

    public BlankVinylDiskWindow(List<BlankVinylDiskOption> options)
    {
        _options = options;

        Title = Loc.GetString("blank-vinyl-disk-window-title");
        MinSize = new Vector2(340, 380);
        SetSize = new Vector2(400, 520);

        var box = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
            VerticalExpand = true,
        };

        box.AddChild(new Label
        {
            Text = Loc.GetString("blank-vinyl-disk-window-hint"),
            Margin = new Thickness(2, 2, 2, 0),
        });

        _search = new LineEdit
        {
            PlaceHolder = Loc.GetString("blank-vinyl-disk-window-search"),
            HorizontalExpand = true,
            Margin = new Thickness(2, 0, 2, 2),
        };
        _search.OnTextChanged += _ => Rebuild();
        box.AddChild(_search);

        _list = new ItemList
        {
            VerticalExpand = true,
            HorizontalExpand = true,
        };
        _list.OnItemSelected += args =>
        {
            if (args.ItemIndex < 0 || args.ItemIndex >= _filtered.Count)
                return;

            OnSelected?.Invoke(_filtered[args.ItemIndex].Id);
        };

        box.AddChild(_list);
        ContentsContainer.AddChild(box);

        Rebuild();
    }

    private void Rebuild()
    {
        var filter = _search.Text.Trim().ToLowerInvariant();
        _filtered.Clear();

        foreach (var option in _options)
        {
            if (filter.Length != 0 && !option.Name.ToLowerInvariant().Contains(filter))
                continue;

            _filtered.Add(option);
        }

        _list.Clear();

        foreach (var option in _filtered)
        {
            _list.AddItem(option.Name, option.Icon, selectable: true);
        }
    }
}
