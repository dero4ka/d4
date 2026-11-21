using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace D4;

/// <summary>Список дополнительных виджетов: id хранится в настройках, окно создаёт Shell.</summary>
public static class WidgetCatalog
{
    public sealed record Info(string Id, string Name, Func<Settings, WidgetWindow> Make);

    public static readonly Info[] All =
    {
        new("weather", "Погода", s => new WeatherWidget(s)),
        new("music", "Музыкальный плеер (то, что играет в Windows)", s => new MusicWidget(s)),
        new("calendar", "Календарь", s => new CalendarWidget(s)),
        new("pomodoro", "Помодоро-таймер", s => new PomodoroWidget(s)),
        new("currency", "Курсы валют", s => new CurrencyWidget(s)),
        new("github", "GitHub-репозиторий", s => new GitHubWidget(s)),
        new("discord", "Discord-сервер", s => new DiscordWidget(s)),
        new("todo", "Чек-лист", s => new TodoWidget(s)),
        new("snippets", "Быстрые фразы (клик — копировать)", s => new SnippetsWidget(s)),
    };
}

internal static class Web
{
    static readonly HttpClient Http = Create();

    static HttpClient Create()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("D4/1.2");
        return c;
    }

    public static async Task<JsonDocument?> Json(string url)
    {
        try
        {
            using var resp = await Http.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return null;
            return JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        }
        catch (Exception ex)
        {
            Settings.Log("web " + url + ": " + ex.Message);
            return null;
        }
    }
}

/// <summary>Мелкие помощники для разметки виджетов.</summary>
internal static class W
{
    public static TextBlock Title(string text)
    {
        var t = Ui.Text(text, 12, "MutedBrush", true);
        t.Margin = new Thickness(0, 0, 0, 6);
        return t;
    }

    public static StackPanel Panel(double pad = 18) => new() { Margin = new Thickness(pad, pad - 2, pad, pad - 2), VerticalAlignment = VerticalAlignment.Center };

    public static Chip Icon(string glyph, double size = 16, string tip = "")
    {
        var c = new Chip(glyph, size) { ToolTip = tip };
        c.Label.FontFamily = new FontFamily("Segoe MDL2 Assets");
        c.Label.Margin = new Thickness(8, 5, 8, 5);
        return c;
    }

    public static TextBox Input(string hint)
    {
        var tb = new TextBox { Padding = new Thickness(8, 5, 8, 5), FontSize = 13, BorderThickness = new Thickness(1), ToolTip = hint };
        tb.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        tb.SetResourceReference(TextBox.CaretBrushProperty, "TextBrush");
        tb.SetResourceReference(Control.BackgroundProperty, "HoverBrush");
        tb.SetResourceReference(Control.BorderBrushProperty, "CardBorderBrush");
        return tb;
    }
}

// ---------- погода ----------

public sealed class WeatherWidget : WidgetWindow
{
    readonly TextBlock city = Ui.Text("", 14, "MutedBrush", true);
    readonly TextBlock temp = Ui.Text("—", 46);
    readonly TextBlock desc = Ui.Text("Загрузка…", 14);
    readonly TextBlock extra = Ui.Text("", 12, "MutedBrush");
    readonly StackPanel days = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMinutes(20) };
    string loadedCity = "";

    public WeatherWidget(Settings s) : base(s, "weather", "D4 Weather", 300, 0, 440, 100)
    {
        temp.FontWeight = FontWeights.Light;
        var p = W.Panel();
        p.Children.Add(city);
        p.Children.Add(temp);
        p.Children.Add(desc);
        p.Children.Add(extra);
        p.Children.Add(days);
        Card.Child = p;
        timer.Tick += async (_, _) => await Load();
        timer.Start();
        Loaded += async (_, _) => await Load();
        Closed += (_, _) => timer.Stop();
    }

    public override void OnSettingsChanged()
    {
        if (!string.Equals(loadedCity, S.City, StringComparison.OrdinalIgnoreCase)) _ = Load();
    }

    static (string Text, string Icon) Describe(int code) => code switch
    {
        0 => ("Ясно", "☀"),
        1 or 2 => ("Малооблачно", "🌤"),
        3 => ("Облачно", "☁"),
        45 or 48 => ("Туман", "🌫"),
        >= 51 and <= 57 => ("Морось", "🌦"),
        >= 61 and <= 67 => ("Дождь", "🌧"),
        >= 71 and <= 77 => ("Снег", "❄"),
        >= 80 and <= 82 => ("Ливень", "🌧"),
        85 or 86 => ("Снегопад", "🌨"),
        >= 95 => ("Гроза", "⛈"),
        _ => ("Погода", "·"),
    };

    async Task Load()
    {
        try
        {
            string name = string.IsNullOrWhiteSpace(S.City) ? "Москва" : S.City;
            loadedCity = S.City;
            using var geo = await Web.Json("https://geocoding-api.open-meteo.com/v1/search?count=1&language=ru&name=" + Uri.EscapeDataString(name));
            if (geo == null || !geo.RootElement.TryGetProperty("results", out var res) || res.GetArrayLength() == 0)
            {
                desc.Text = "Город не найден";
                return;
            }
            var r0 = res[0];
            double lat = r0.GetProperty("latitude").GetDouble(), lon = r0.GetProperty("longitude").GetDouble();
            city.Text = r0.GetProperty("name").GetString() ?? name;

            string url = "https://api.open-meteo.com/v1/forecast?timezone=auto&forecast_days=4"
                + "&latitude=" + lat.ToString(CultureInfo.InvariantCulture) + "&longitude=" + lon.ToString(CultureInfo.InvariantCulture)
                + "&current=temperature_2m,apparent_temperature,weather_code,wind_speed_10m"
                + "&daily=weather_code,temperature_2m_max,temperature_2m_min";
            using var doc = await Web.Json(url);
            if (doc == null)
            {
                desc.Text = "Нет связи";
                return;
            }

            var cur = doc.RootElement.GetProperty("current");
            var (text, icon) = Describe(cur.GetProperty("weather_code").GetInt32());
            temp.Text = Math.Round(cur.GetProperty("temperature_2m").GetDouble()) + "°";
            desc.Text = icon + "  " + text;
            extra.Text = "Ощущается как " + Math.Round(cur.GetProperty("apparent_temperature").GetDouble()) + "°  ·  ветер " + Math.Round(cur.GetProperty("wind_speed_10m").GetDouble()) + " км/ч";

            var daily = doc.RootElement.GetProperty("daily");
            var times = daily.GetProperty("time");
            var codes = daily.GetProperty("weather_code");
            var max = daily.GetProperty("temperature_2m_max");
            var min = daily.GetProperty("temperature_2m_min");
            days.Children.Clear();
            var ru = CultureInfo.GetCultureInfo("ru-RU");
            for (int i = 1; i < times.GetArrayLength(); i++)
            {
                var d = DateTime.Parse(times[i].GetString()!, CultureInfo.InvariantCulture);
                var col = new StackPanel { Margin = new Thickness(0, 0, 14, 0) };
                col.Children.Add(Ui.Text(d.ToString("ddd", ru), 11, "MutedBrush"));
                col.Children.Add(Ui.Text(Describe(codes[i].GetInt32()).Icon, 16));
                col.Children.Add(Ui.Text(Math.Round(max[i].GetDouble()) + "° / " + Math.Round(min[i].GetDouble()) + "°", 11));
                days.Children.Add(col);
            }
        }
        catch (Exception ex)
        {
            Settings.Log("weather: " + ex.Message);
            desc.Text = "Нет данных";
        }
    }
}

// ---------- музыка ----------

public sealed class MusicWidget : WidgetWindow
{
    readonly Image cover = new() { Width = 64, Height = 64, Stretch = Stretch.UniformToFill };
    readonly TextBlock title = Ui.Text("Ничего не играет", 14, "TextBrush", true);
    readonly TextBlock artist = Ui.Text("", 12, "MutedBrush");
    readonly Chip play = W.Icon("", 18, "Играть / пауза");
    readonly ProgressBar bar = new() { Height = 4, Minimum = 0, Maximum = 100, BorderThickness = new Thickness(0), Margin = new Thickness(0, 10, 0, 0) };
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1.5) };
    Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager? mgr;
    string lastKey = "";

    public MusicWidget(Settings s) : base(s, "music", "D4 Music", 300, 0, 760, 100)
    {
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        artist.TextTrimming = TextTrimming.CharacterEllipsis;
        bar.SetResourceReference(Control.ForegroundProperty, "AccentBrush");
        bar.SetResourceReference(Control.BackgroundProperty, "HoverBrush");

        var coverBox = new Border { Width = 64, Height = 64, CornerRadius = new CornerRadius(12), ClipToBounds = true, Child = cover, Margin = new Thickness(0, 0, 12, 0) };
        coverBox.SetResourceReference(Border.BackgroundProperty, "HoverBrush");

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Width = 190 };
        text.Children.Add(title);
        text.Children.Add(artist);

        var top = new StackPanel { Orientation = Orientation.Horizontal };
        top.Children.Add(coverBox);
        top.Children.Add(text);

        var prev = W.Icon("", 16, "Назад");
        var next = W.Icon("", 16, "Вперёд");
        prev.Click += async () => await Act(ses => ses.TrySkipPreviousAsync().AsTask());
        next.Click += async () => await Act(ses => ses.TrySkipNextAsync().AsTask());
        play.Click += async () => await Act(ses => ses.TryTogglePlayPauseAsync().AsTask());
        var controls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
        controls.Children.Add(prev);
        controls.Children.Add(play);
        controls.Children.Add(next);

        var p = W.Panel();
        p.Children.Add(top);
        p.Children.Add(bar);
        p.Children.Add(controls);
        Card.Child = p;

        timer.Tick += async (_, _) => await Poll();
        timer.Start();
        Loaded += async (_, _) => await Poll();
        Closed += (_, _) => timer.Stop();
    }

    async Task Act(Func<Windows.Media.Control.GlobalSystemMediaTransportControlsSession, Task> action)
    {
        try
        {
            var ses = mgr?.GetCurrentSession();
            if (ses != null) await action(ses);
            await Poll();
        }
        catch (Exception ex)
        {
            Settings.Log("music action: " + ex.Message);
        }
    }

    async Task Poll()
    {
        try
        {
            mgr ??= await Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var ses = mgr.GetCurrentSession();
            if (ses == null)
            {
                title.Text = "Ничего не играет";
                artist.Text = "";
                cover.Source = null;
                bar.Value = 0;
                lastKey = "";
                return;
            }

            var props = await ses.TryGetMediaPropertiesAsync();
            title.Text = string.IsNullOrWhiteSpace(props.Title) ? "Без названия" : props.Title;
            artist.Text = props.Artist;
            bool playing = ses.GetPlaybackInfo().PlaybackStatus == Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            play.Label.Text = playing ? "" : "";

            var tl = ses.GetTimelineProperties();
            bar.Value = tl.EndTime > TimeSpan.Zero ? Math.Clamp(tl.Position.TotalSeconds / tl.EndTime.TotalSeconds * 100, 0, 100) : 0;

            string key = props.Title + "|" + props.Artist;
            if (key != lastKey)
            {
                lastKey = key;
                cover.Source = null;
                if (props.Thumbnail != null)
                {
                    using var raw = await props.Thumbnail.OpenReadAsync();
                    using var ms = new MemoryStream();
                    await raw.AsStreamForRead().CopyToAsync(ms);
                    ms.Position = 0;
                    var img = new BitmapImage();
                    img.BeginInit();
                    img.CacheOption = BitmapCacheOption.OnLoad;
                    img.StreamSource = ms;
                    img.EndInit();
                    img.Freeze();
                    cover.Source = img;
                }
            }
        }
        catch (Exception ex)
        {
            Settings.Log("music: " + ex.Message);
        }
    }
}

// ---------- календарь ----------

public sealed class CalendarWidget : WidgetWindow
{
    readonly TextBlock header = Ui.Text("", 15, "TextBrush", true);
    readonly UniformGrid grid = new() { Columns = 7 };
    DateTime shown = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    readonly CultureInfo ru = CultureInfo.GetCultureInfo("ru-RU");

    public CalendarWidget(Settings s) : base(s, "calendar", "D4 Calendar", 300, 0, 1080, 100)
    {
        var prev = W.Icon("", 12, "Предыдущий месяц");
        var next = W.Icon("", 12, "Следующий месяц");
        prev.Click += () => { shown = shown.AddMonths(-1); Build(); };
        next.Click += () => { shown = shown.AddMonths(1); Build(); };
        header.VerticalAlignment = VerticalAlignment.Center;
        header.TextAlignment = TextAlignment.Center;

        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(prev, Dock.Left);
        DockPanel.SetDock(next, Dock.Right);
        head.Children.Add(prev);
        head.Children.Add(next);
        head.Children.Add(header);

        var p = W.Panel();
        p.Children.Add(head);
        p.Children.Add(grid);
        Card.Child = p;
        header.MouseLeftButtonUp += (_, _) => { shown = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1); Build(); };
        Build();
    }

    void Build()
    {
        var title = shown.ToString("LLLL yyyy", ru);
        header.Text = char.ToUpper(title[0], ru) + title.Substring(1);
        grid.Children.Clear();

        foreach (var d in new[] { "Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс" })
        {
            var t = Ui.Text(d, 11, "MutedBrush");
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.Margin = new Thickness(0, 0, 0, 4);
            grid.Children.Add(t);
        }

        int offset = ((int)shown.DayOfWeek + 6) % 7; // неделя начинается с понедельника
        for (int i = 0; i < offset; i++) grid.Children.Add(new Border());

        int days = DateTime.DaysInMonth(shown.Year, shown.Month);
        for (int d = 1; d <= days; d++)
        {
            bool today = shown.Year == DateTime.Today.Year && shown.Month == DateTime.Today.Month && d == DateTime.Today.Day;
            var label = Ui.Text(d.ToString(), 13, today ? "BgBrush" : "TextBrush", today);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.VerticalAlignment = VerticalAlignment.Center;
            var cell = new Border { Height = 30, Margin = new Thickness(1), CornerRadius = new CornerRadius(15), Child = label };
            if (today) cell.SetResourceReference(Border.BackgroundProperty, "AccentGradBrush");
            grid.Children.Add(cell);
        }
    }
}

// ---------- помодоро ----------

public sealed class PomodoroWidget : WidgetWindow
{
    readonly TextBlock phase = Ui.Text("Работа", 13, "MutedBrush", true);
    readonly TextBlock time = Ui.Text("25:00", 46);
    readonly ProgressBar bar = new() { Height = 6, Minimum = 0, Maximum = 100, BorderThickness = new Thickness(0), Margin = new Thickness(0, 8, 0, 8) };
    readonly Chip toggle = W.Icon("", 18, "Старт / пауза");
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    bool work = true, running;
    int left, total;

    public PomodoroWidget(Settings s) : base(s, "pomodoro", "D4 Pomodoro", 260, 0, 1400, 100)
    {
        time.FontWeight = FontWeights.Light;
        time.HorizontalAlignment = HorizontalAlignment.Center;
        phase.HorizontalAlignment = HorizontalAlignment.Center;
        bar.SetResourceReference(Control.ForegroundProperty, "AccentBrush");
        bar.SetResourceReference(Control.BackgroundProperty, "HoverBrush");

        toggle.Click += () =>
        {
            running = !running;
            toggle.Label.Text = running ? "" : "";
            if (running) timer.Start(); else timer.Stop();
        };
        var reset = W.Icon("", 16, "Сбросить");
        reset.Click += () => Reset(true);
        var skip = W.Icon("", 16, "Пропустить этап");
        skip.Click += () => { work = !work; Reset(false); };

        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        row.Children.Add(reset);
        row.Children.Add(toggle);
        row.Children.Add(skip);

        var p = W.Panel();
        p.Children.Add(phase);
        p.Children.Add(time);
        p.Children.Add(bar);
        p.Children.Add(row);
        Card.Child = p;

        timer.Tick += (_, _) =>
        {
            if (--left <= 0)
            {
                System.Media.SystemSounds.Asterisk.Play();
                work = !work;
                Reset(false);
                return;
            }
            Show_();
        };
        Reset(true);
        Closed += (_, _) => timer.Stop();
    }

    public override void OnSettingsChanged()
    {
        if (!running) Reset(false);
    }

    void Reset(bool toWork)
    {
        if (toWork) work = true;
        total = Math.Max(1, work ? S.PomodoroWork : S.PomodoroBreak) * 60;
        left = total;
        phase.Text = work ? "Работа" : "Отдых";
        Show_();
    }

    void Show_()
    {
        time.Text = (left / 60).ToString("00") + ":" + (left % 60).ToString("00");
        bar.Value = (total - left) * 100.0 / total;
    }
}

// ---------- курсы валют ----------

public sealed class CurrencyWidget : WidgetWindow
{
    readonly StackPanel list = new();
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromHours(1) };

    public CurrencyWidget(Settings s) : base(s, "currency", "D4 Currency", 260, 0, 80, 520)
    {
        var p = W.Panel();
        p.Children.Add(W.Title("КУРС К РУБЛЮ (ЦБ РФ)"));
        p.Children.Add(list);
        Card.Child = p;
        list.Children.Add(Ui.Text("Загрузка…", 13, "MutedBrush"));
        timer.Tick += async (_, _) => await Load();
        timer.Start();
        Loaded += async (_, _) => await Load();
        Closed += (_, _) => timer.Stop();
    }

    public override void OnSettingsChanged() => _ = Load();

    async Task Load()
    {
        using var doc = await Web.Json("https://www.cbr-xml-daily.ru/daily_json.js");
        list.Children.Clear();
        if (doc == null || !doc.RootElement.TryGetProperty("Valute", out var v))
        {
            list.Children.Add(Ui.Text("Нет связи", 13, "MutedBrush"));
            return;
        }

        foreach (var code in S.Currencies.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(c => c.Trim().ToUpperInvariant()))
        {
            if (!v.TryGetProperty(code, out var c)) continue;
            double nominal = Math.Max(1, c.GetProperty("Nominal").GetDouble());
            double value = c.GetProperty("Value").GetDouble() / nominal;
            double prev = c.GetProperty("Previous").GetDouble() / nominal;

            var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
            var name = Ui.Text(code, 15, "TextBrush", true);
            DockPanel.SetDock(name, Dock.Left);
            var arrow = Ui.Text(value >= prev ? "▲" : "▼", 10, value >= prev ? "AccentBrush" : "MutedBrush");
            arrow.Margin = new Thickness(6, 0, 0, 0);
            arrow.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(arrow, Dock.Right);
            var num = Ui.Text(value.ToString("0.00", CultureInfo.GetCultureInfo("ru-RU")) + " ₽", 15);
            num.HorizontalAlignment = HorizontalAlignment.Right;
            row.Children.Add(name);
            row.Children.Add(arrow);
            row.Children.Add(num);
            list.Children.Add(row);
        }
        if (list.Children.Count == 0) list.Children.Add(Ui.Text("Укажите коды валют в настройках", 12, "MutedBrush"));
    }
}

// ---------- GitHub ----------

public sealed class GitHubWidget : WidgetWindow
{
    readonly TextBlock name = Ui.Text("GitHub", 15, "TextBrush", true);
    readonly TextBlock info = Ui.Text("Укажите репозиторий в настройках", 12, "MutedBrush");
    readonly StackPanel stats = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMinutes(10) };
    string url = "";

    public GitHubWidget(Settings s) : base(s, "github", "D4 GitHub", 300, 0, 80, 700)
    {
        info.TextWrapping = TextWrapping.Wrap;
        var p = W.Panel();
        p.Children.Add(W.Title("GITHUB"));
        p.Children.Add(name);
        p.Children.Add(info);
        p.Children.Add(stats);
        Card.Child = p;
        Card.Cursor = Cursors.Hand;
        Card.MouseLeftButtonUp += (_, e) =>
        {
            if (url.Length > 0 && e.ClickCount == 1) AppUtil.Launch(url);
        };
        timer.Tick += async (_, _) => await Load();
        timer.Start();
        Loaded += async (_, _) => await Load();
        Closed += (_, _) => timer.Stop();
    }

    public override void OnSettingsChanged() => _ = Load();

    Border Stat(string icon, string value)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 16, 0) };
        sp.Children.Add(Ui.Text(icon, 13, "AccentBrush"));
        var t = Ui.Text(" " + value, 13);
        sp.Children.Add(t);
        return new Border { Child = sp };
    }

    async Task Load()
    {
        stats.Children.Clear();
        var repo = (S.GithubRepo ?? "").Trim().Trim('/');
        if (repo.Split('/').Length != 2)
        {
            name.Text = "GitHub";
            info.Text = "Укажите репозиторий в настройках (например, owner/name)";
            url = "";
            return;
        }

        using var doc = await Web.Json("https://api.github.com/repos/" + repo);
        if (doc == null)
        {
            name.Text = repo;
            info.Text = "Репозиторий не найден или нет связи";
            return;
        }
        var r = doc.RootElement;
        name.Text = r.GetProperty("full_name").GetString();
        info.Text = r.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : "Без описания";
        url = r.GetProperty("html_url").GetString() ?? "";
        stats.Children.Add(Stat("★", r.GetProperty("stargazers_count").GetInt32().ToString()));
        stats.Children.Add(Stat("⑂", r.GetProperty("forks_count").GetInt32().ToString()));
        stats.Children.Add(Stat("●", r.GetProperty("open_issues_count").GetInt32() + " issues"));

        using var rel = await Web.Json("https://api.github.com/repos/" + repo + "/releases/latest");
        if (rel != null && rel.RootElement.TryGetProperty("tag_name", out var tag))
            info.Text += "\nПоследний релиз: " + tag.GetString();
    }
}

// ---------- Discord ----------

public sealed class DiscordWidget : WidgetWindow
{
    readonly TextBlock name = Ui.Text("Discord", 15, "TextBrush", true);
    readonly TextBlock online = Ui.Text("Укажите ID сервера в настройках", 12, "MutedBrush");
    readonly TextBlock members = Ui.Text("", 12);
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMinutes(3) };
    string invite = "";

    public DiscordWidget(Settings s) : base(s, "discord", "D4 Discord", 280, 0, 440, 560)
    {
        online.TextWrapping = TextWrapping.Wrap;
        members.TextWrapping = TextWrapping.Wrap;
        var p = W.Panel();
        p.Children.Add(W.Title("DISCORD"));
        p.Children.Add(name);
        p.Children.Add(online);
        p.Children.Add(members);
        Card.Child = p;
        Card.Cursor = Cursors.Hand;
        Card.MouseLeftButtonUp += (_, _) =>
        {
            if (invite.Length > 0) AppUtil.Launch(invite);
        };
        timer.Tick += async (_, _) => await Load();
        timer.Start();
        Loaded += async (_, _) => await Load();
        Closed += (_, _) => timer.Stop();
    }

    public override void OnSettingsChanged() => _ = Load();

    async Task Load()
    {
        var id = (S.DiscordGuild ?? "").Trim();
        if (id.Length == 0)
        {
            name.Text = "Discord";
            online.Text = "Укажите ID сервера в настройках. На сервере должен быть включён виджет (Настройки сервера → Виджет).";
            members.Text = "";
            invite = "";
            return;
        }

        using var doc = await Web.Json("https://discord.com/api/guilds/" + Uri.EscapeDataString(id) + "/widget.json");
        if (doc == null)
        {
            online.Text = "Сервер не найден или виджет выключен";
            members.Text = "";
            return;
        }
        var r = doc.RootElement;
        name.Text = r.GetProperty("name").GetString();
        online.Text = "В сети: " + (r.TryGetProperty("presence_count", out var pc) ? pc.GetInt32() : 0);
        invite = r.TryGetProperty("instant_invite", out var inv) && inv.ValueKind == JsonValueKind.String ? inv.GetString() ?? "" : "";
        members.Text = r.TryGetProperty("members", out var m)
            ? string.Join(", ", m.EnumerateArray().Take(10).Select(x => x.GetProperty("username").GetString()))
            : "";
    }
}

// ---------- чек-лист ----------

public sealed class TodoWidget : WidgetWindow
{
    readonly StackPanel list = new();
    readonly TextBox input = W.Input("Новая задача, Enter — добавить");

    public TodoWidget(Settings s) : base(s, "todo", "D4 Checklist", 300, 0, 760, 400)
    {
        var p = W.Panel();
        p.Children.Add(W.Title("СПИСОК ДЕЛ"));
        p.Children.Add(list);
        input.Margin = new Thickness(0, 8, 0, 0);
        input.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || string.IsNullOrWhiteSpace(input.Text)) return;
            S.Todo.Add(new TodoItem { Text = input.Text.Trim() });
            input.Clear();
            S.SaveSoon();
            Build();
        };
        p.Children.Add(input);
        Card.Child = p;
        Build();
    }

    void Build()
    {
        list.Children.Clear();
        foreach (var item in S.Todo.ToList())
        {
            var it = item;
            var cb = new CheckBox { IsChecked = it.Done, Content = it.Text, Margin = new Thickness(0, 3, 0, 3), FontSize = 14 };
            cb.SetResourceReference(Control.ForegroundProperty, it.Done ? "MutedBrush" : "TextBrush");
            cb.Checked += (_, _) => { it.Done = true; cb.SetResourceReference(Control.ForegroundProperty, "MutedBrush"); S.SaveSoon(); };
            cb.Unchecked += (_, _) => { it.Done = false; cb.SetResourceReference(Control.ForegroundProperty, "TextBrush"); S.SaveSoon(); };

            var del = new Chip("✕", 11) { ToolTip = "Удалить" };
            del.Label.Margin = new Thickness(6, 2, 6, 2);
            del.Click += () => { S.Todo.Remove(it); S.SaveSoon(); Build(); };

            var row = new DockPanel();
            DockPanel.SetDock(del, Dock.Right);
            row.Children.Add(del);
            row.Children.Add(cb);
            list.Children.Add(row);
        }
        if (S.Todo.Count == 0) list.Children.Add(Ui.Text("Пока пусто", 13, "MutedBrush"));
    }
}

// ---------- быстрые фразы ----------

public sealed class SnippetsWidget : WidgetWindow
{
    readonly StackPanel list = new();
    readonly TextBlock status = Ui.Text("Клик — скопировать, правый клик — удалить", 11, "MutedBrush");
    readonly TextBox input = W.Input("Новая фраза, Enter — добавить");

    public SnippetsWidget(Settings s) : base(s, "snippets", "D4 Snippets", 300, 0, 1080, 400)
    {
        var p = W.Panel();
        p.Children.Add(W.Title("БЫСТРЫЕ ФРАЗЫ"));
        p.Children.Add(list);
        input.Margin = new Thickness(0, 8, 0, 4);
        input.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || string.IsNullOrWhiteSpace(input.Text)) return;
            S.Snippets.Add(input.Text.Trim());
            input.Clear();
            S.SaveSoon();
            Build();
        };
        p.Children.Add(input);
        p.Children.Add(status);
        Card.Child = p;
        Build();
    }

    void Build()
    {
        list.Children.Clear();
        foreach (var text in S.Snippets.ToList())
        {
            var snippet = text;
            var label = Ui.Text(snippet, 13);
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            var box = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 2, 0, 2), Cursor = Cursors.Hand, Child = label, Background = Brushes.Transparent };
            box.MouseEnter += (_, _) => box.SetResourceReference(Border.BackgroundProperty, "HoverBrush");
            box.MouseLeave += (_, _) => box.Background = Brushes.Transparent;
            box.MouseLeftButtonUp += (_, _) =>
            {
                try { Clipboard.SetText(snippet); status.Text = "Скопировано ✓"; }
                catch { status.Text = "Не удалось скопировать"; }
            };
            box.MouseRightButtonUp += (_, _) => { S.Snippets.Remove(snippet); S.SaveSoon(); Build(); };
            list.Children.Add(box);
        }
    }
}
