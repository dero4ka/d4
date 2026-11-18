using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace D4;

/// <summary>Мини-папка на рабочем столе: ярлыки перетаскиваются прямо в окно, клик запускает.</summary>
public sealed class MiniFolderWindow : WidgetWindow
{
    readonly MiniFolder folder;
    readonly Action<MiniFolder> onDelete;
    readonly Border header = new() { Margin = new Thickness(0, 0, 0, 8), Background = Brushes.Transparent };
    readonly WrapPanel items = new() { Width = 216 };

    public MiniFolderWindow(Settings settings, MiniFolder folder, Action<MiniFolder> onDelete)
        : base(settings, "folder:" + folder.Id, folder.Title, 248, 0, 320, 220)
    {
        this.folder = folder;
        this.onDelete = onDelete;

        AllowDrop = true;
        Drop += OnDrop;

        var panel = new StackPanel { Margin = new Thickness(16, 14, 16, 14) };
        panel.Children.Add(header);
        panel.Children.Add(items);
        Card.Child = panel;

        header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2)
            {
                EditTitle();
                e.Handled = true;
            }
        };

        ContextMenu = BuildMenu();
        ApplyShape();
        BuildHeader();
        BuildItems();
    }

    void ApplyShape()
    {
        Card.CornerRadius = folder.Shape switch
        {
            "Capsule" => new CornerRadius(44),
            "Square" => new CornerRadius(8),
            _ => new CornerRadius(28),
        };
    }

    ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();

        var shape = new MenuItem { Header = "Форма" };
        foreach (var (id, name) in new[] { ("Round", "Скруглённая"), ("Capsule", "Капсула"), ("Square", "Прямоугольная") })
        {
            var sid = id;
            var mi = new MenuItem { Header = name };
            mi.Click += (_, _) =>
            {
                folder.Shape = sid;
                S.SaveSoon();
                ApplyShape();
            };
            shape.Items.Add(mi);
        }

        var rename = new MenuItem { Header = "Переименовать" };
        rename.Click += (_, _) => EditTitle();

        var remove = new MenuItem { Header = "Удалить папку" };
        remove.Click += (_, _) => onDelete(folder);

        menu.Items.Add(rename);
        menu.Items.Add(shape);
        menu.Items.Add(new Separator());
        menu.Items.Add(remove);
        return menu;
    }

    void BuildHeader()
    {
        header.Child = Ui.Text(folder.Title, 15, "AccentBrush", true);
    }

    void EditTitle()
    {
        var box = new TextBox
        {
            Text = folder.Title,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0, 0, 0, 1),
        };
        box.Dyn(Control.ForegroundProperty, "TextBrush");
        box.Dyn(Control.BorderBrushProperty, "AccentBrush");
        header.Child = box;
        box.Focus();
        box.SelectAll();

        void Commit()
        {
            if (!string.IsNullOrWhiteSpace(box.Text)) folder.Title = box.Text.Trim();
            S.SaveSoon();
            BuildHeader();
        }
        box.LostFocus += (_, _) => Commit();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) Commit();
        };
    }

    void BuildItems()
    {
        items.Children.Clear();

        foreach (var path in folder.Items.ToList())
        {
            var p = path;
            var name = System.IO.Path.GetFileNameWithoutExtension(p);
            if (string.IsNullOrEmpty(name)) name = p;

            var icon = AppUtil.GetIcon(p);
            UIElement pic = icon != null
                ? new Image { Source = icon, Width = 32, Height = 32 }
                : Ui.Text("📁", 26);

            var cell = new StackPanel { Width = 54, Margin = new Thickness(0, 0, 0, 6) };
            cell.Children.Add(new ContentControl { Content = pic, HorizontalAlignment = HorizontalAlignment.Center });
            var label = Ui.Text(name, 10, "MutedBrush");
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            label.TextAlignment = TextAlignment.Center;
            label.Margin = new Thickness(2, 2, 2, 0);
            cell.Children.Add(label);

            var tile = new Border
            {
                CornerRadius = new CornerRadius(10),
                Background = Brushes.Transparent,
                Padding = new Thickness(0, 4, 0, 0),
                Child = cell,
                Cursor = Cursors.Hand,
                ToolTip = p,
            };
            tile.MouseEnter += (_, _) => tile.Dyn(Border.BackgroundProperty, "HoverBrush");
            tile.MouseLeave += (_, _) => tile.Background = Brushes.Transparent;
            tile.MouseLeftButtonUp += (_, _) => AppUtil.Launch(p);

            var menu = new ContextMenu();
            var remove = new MenuItem { Header = "Убрать из папки" };
            remove.Click += (_, _) =>
            {
                folder.Items.Remove(p);
                S.SaveSoon();
                BuildItems();
            };
            menu.Items.Add(remove);
            tile.ContextMenu = menu;

            items.Children.Add(tile);
        }

        if (folder.Items.Count == 0)
        {
            var hint = Ui.Text("Перетащите сюда ярлыки или программы", 12, "MutedBrush");
            hint.TextWrapping = TextWrapping.Wrap;
            items.Children.Add(hint);
        }
    }

    void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;

        foreach (var f in files)
            if (!folder.Items.Contains(f)) folder.Items.Add(f);

        S.SaveSoon();
        BuildItems();
    }
}
