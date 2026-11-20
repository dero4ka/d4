using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace D4;

/// <summary>Всплывающая карточка в стиле темы: список окон, меню, сеть, громкость. Закрывается кликом в любом другом месте.</summary>
public sealed class Flyout
{
    readonly Popup popup;
    readonly Border card = new();
    readonly StackPanel body = new() { Margin = new Thickness(6) };
    readonly TranslateTransform slide = new();
    readonly double fromY;

    public Flyout(FrameworkElement target, bool below)
    {
        fromY = below ? -8 : 8;

        card.CornerRadius = new CornerRadius(14);
        card.BorderThickness = new Thickness(1);
        card.MinWidth = 240;
        card.MaxWidth = 380;
        card.Child = body;
        card.Effect = new DropShadowEffect { BlurRadius = 18, Opacity = .45, ShadowDepth = 3 };
        card.Dyn(Border.BackgroundProperty, "BgBrush");
        card.Dyn(Border.BorderBrushProperty, "CardBorderBrush");
        card.RenderTransform = slide;

        popup = new Popup
        {
            AllowsTransparency = true,
            StaysOpen = false,
            PlacementTarget = target,
            Placement = below ? PlacementMode.Bottom : PlacementMode.Top,
            Child = new Border { Padding = new Thickness(12), Background = Brushes.Transparent, Child = card },
        };
    }

    public bool IsOpen => popup.IsOpen;

    public void Show()
    {
        card.Opacity = 0;
        slide.Y = fromY;
        popup.IsOpen = true;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        card.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(170)));
        slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(fromY, 0, TimeSpan.FromMilliseconds(230)) { EasingFunction = ease });
    }

    public void Close() => popup.IsOpen = false;

    /// <summary>Пустой контейнер, который можно заполнить позже (например, когда найдутся Wi-Fi сети).</summary>
    public StackPanel Slot()
    {
        var p = new StackPanel();
        body.Children.Add(p);
        return p;
    }

    /// <summary>Добавляет в карточку любой свой элемент.</summary>
    public void Add(UIElement element, StackPanel? into = null) => Target(into).Children.Add(element);

    StackPanel Target(StackPanel? into) => into ?? body;

    /// <summary>Маленький серый заголовок раздела.</summary>
    public void Section(string text, StackPanel? into = null)
    {
        var t = Ui.Text(text, 11, "MutedBrush");
        t.Margin = new Thickness(10, 4, 10, 2);
        Target(into).Children.Add(t);
    }

    public TextBlock Text(string text, double size, string brush, bool bold = false, StackPanel? into = null)
    {
        var t = Ui.Text(text, size, brush, bold);
        t.Margin = new Thickness(10, 3, 10, 3);
        t.TextWrapping = TextWrapping.Wrap;
        Target(into).Children.Add(t);
        return t;
    }

    public void Separator(StackPanel? into = null)
    {
        var line = new Border { Height = 1, Margin = new Thickness(6, 5, 6, 5) };
        line.Dyn(Border.BackgroundProperty, "CardBorderBrush");
        Target(into).Children.Add(line);
    }

    /// <summary>Строка меню. Если задан <paramref name="close"/>, справа появляется крестик.</summary>
    public Border Row(ImageSource? icon, string text, Action click, bool active = false, Action? close = null, StackPanel? into = null)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        if (icon != null)
        {
            var img = new Image { Source = icon, Width = 20, Height = 20, Margin = new Thickness(0, 0, 10, 0) };
            Grid.SetColumn(img, 0);
            grid.Children.Add(img);
        }

        var label = Ui.Text(text, 14, active ? "AccentBrush" : "TextBrush", active);
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        label.MaxWidth = 300;
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);

        if (close != null)
        {
            var x = new Chip("✕", 12) { ToolTip = "Закрыть окно", Margin = new Thickness(10, 0, 0, 0) };
            x.Click += () =>
            {
                Close();
                close();
            };
            Grid.SetColumn(x, 2);
            grid.Children.Add(x);
        }

        var row = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 7, 10, 7),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Child = grid,
        };
        row.MouseEnter += (_, _) => row.Dyn(Border.BackgroundProperty, "HoverBrush");
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        row.MouseLeftButtonUp += (_, _) =>
        {
            Close();
            click();
        };
        Target(into).Children.Add(row);
        return row;
    }

    /// <summary>Ползунок 0..1 в стиле темы (громкость). <paramref name="changed"/> вызывается при перетаскивании.</summary>
    public FrameworkElement Slider(double value, Action<double> changed)
    {
        const double width = 220;

        var track = new Border { Height = 8, CornerRadius = new CornerRadius(4) };
        track.Dyn(Border.BackgroundProperty, "HoverBrush");
        var fill = new Border { Height = 8, CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left };
        fill.Dyn(Border.BackgroundProperty, "AccentBrush");
        var thumb = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(8), HorizontalAlignment = HorizontalAlignment.Left };
        thumb.Dyn(Border.BackgroundProperty, "TextBrush");

        var grid = new Grid
        {
            Width = width,
            Height = 24,
            Margin = new Thickness(10, 4, 10, 6),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
        };
        grid.Children.Add(track);
        grid.Children.Add(fill);
        grid.Children.Add(thumb);

        void Set(double v)
        {
            v = Math.Clamp(v, 0, 1);
            fill.Width = width * v;
            thumb.Margin = new Thickness(Math.Max(0, width * v - 8), 0, 0, 0);
        }

        void Drag(double x)
        {
            var v = Math.Clamp(x / width, 0, 1);
            Set(v);
            changed(v);
        }

        bool dragging = false;
        grid.MouseLeftButtonDown += (_, e) =>
        {
            dragging = true;
            grid.CaptureMouse();
            Drag(e.GetPosition(grid).X);
        };
        grid.MouseMove += (_, e) =>
        {
            if (dragging) Drag(e.GetPosition(grid).X);
        };
        grid.MouseLeftButtonUp += (_, _) =>
        {
            dragging = false;
            grid.ReleaseMouseCapture();
        };

        Set(value);
        body.Children.Add(grid);
        return grid;
    }
}
