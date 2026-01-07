using Microsoft.Win32;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Navigation;
using System.Windows.Threading;

namespace JNightLauncher
{
    public partial class MainWindow : Window
    {
        private const string CONFIG_FILE = "jnconfig.json";
        private LauncherConfig cfg;
        // Получаем версию исполняемой сборки
        private readonly string VERSION = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).FileVersion;
        private DispatcherTimer updateTimer;

        // Логирование
        private void Log(string message)
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "launcher.log");
                // Отключим логирование если за это нас блочит антивирь
                //File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
            }
            catch { }
        }

        // Закрыть окно по кастомному крестику
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        // Свернуть окно кастомной кнопкой
        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        // Перетаскивание за кастомный заголовок
        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Если удерживаем левую кнопку мыши, перемещаем окно
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }

        // Перехват ссылок из html с сайта, чтобы открывать их в браузере а не в окне html лаунчера
        private void RightBrowser_Navigating(object sender, NavigatingCancelEventArgs e)
        {
            // Если ссылка НЕ about:blank и не наш первоначальный NavigateToString
            if (e.Uri != null && e.Uri.ToString() != "about:blank")
            {
                e.Cancel = true; // Браузер внутри WPF не открывает ссылку
                Process.Start(new ProcessStartInfo
                {
                    FileName = e.Uri.ToString(),
                    UseShellExecute = true // запускает в системном браузере
                });
            }
        }

        // Инициализация окна
        public MainWindow()
        {
            InitializeComponent();
            LoadConfig();
            // Задержка 5 секунд перед обновлением серверов с сайта
            Task.Delay(5000).ContinueWith(_ => Dispatcher.Invoke(UpdateServersFromSite));
            BuildGUI();
            LoadHtmlContent();
            // Настройка таймера для периодического обновления онлайна (каждые 30 сек)
            updateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(30)
            };
            updateTimer.Tick += (s, e) => UpdatePlayerCounts();
            updateTimer.Start();
            // Первое обновление онлайна сразу после запуска
            UpdatePlayerCounts();
        }

        #region CONFIG
        // Чтение конфига
        private void LoadConfig()
        {
            if (!File.Exists(CONFIG_FILE))
            {
                cfg = LauncherConfig.Default();
                SaveConfig();
                return;
            }

            try
            {
                string json = File.ReadAllText(CONFIG_FILE);
                cfg = JsonConvert.DeserializeObject<LauncherConfig>(json);
            }
            catch
            {
                cfg = LauncherConfig.Default();
                SaveConfig();
            }

            NameBox.Text = cfg.Name;
        }

        // Запись конфига
        private void SaveConfig()
        {
            File.WriteAllText(CONFIG_FILE, JsonConvert.SerializeObject(cfg, Formatting.Indented));
        }

        // Обновление блока Servers из удалённого JSON
        private void UpdateServersFromSite()
        {
            try
            {
                using (var client = new WebClient())
                {
                    string json = client.DownloadString("https://jnight.ru/launcher/config_default.json");
                    var remoteConfig = JsonConvert.DeserializeObject<LauncherConfig>(json);
                    if (remoteConfig?.Servers != null && remoteConfig.Servers.Count > 0)
                    {
                        cfg.Servers = remoteConfig.Servers;
                        SaveConfig();
                        Log("Servers updated from site successfully.");
                        // Перестроить GUI после обновления
                        Dispatcher.Invoke(BuildGUI);
                    }
                }
            }
            catch (Exception ex)
            {
                Log("Error updating servers from site: " + ex.Message);
            }
        }
        #endregion

        #region GUI
        // Собираем конпки
        private void BuildGUI()
        {
            NameBox.TextChanged += (s, e) => cfg.Name = NameBox.Text;

            ServersPanel.Children.Clear();

            const double buttonEffectiveHeight = 55; // Height=40 + Margin Top/Bottom=10
            const double baseHeight = 220;           // Заголовок + логотип + поля + отступы (подобрано под ~6 серверов)
            const double minHeight = 560;            // Не меньше оригинала

            foreach (var srv in cfg.Servers)
            {
                var btn = new Button
                {
                    Content = srv.Name,
                    Margin = new Thickness(5),
                    Style = (Style)FindResource("CustomButtonStyle"),
                    Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#3E444C"),
                    Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#c8c8c8"),
                    BorderBrush = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#191B1E"),
                    BorderThickness = new Thickness(2),
                    Height = 40,
                    Padding = new Thickness(4, 5, 4, 5),
                    Name = "Btn_" + (srv.Code?.ToLower() ?? Guid.NewGuid().ToString("N").Substring(0, 8))
                };

                btn.PreviewMouseLeftButtonDown += (s, e) => btn.Background = System.Windows.Media.Brushes.Gray;
                btn.PreviewMouseLeftButtonUp += (s, e) => btn.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#3E444C");

                btn.Click += (s, e) => RunLauncher(srv);
                ServersPanel.Children.Add(btn);
            }

            // === Динамическая высота окна ===
            int serversCount = cfg.Servers.Count;
            double calculatedHeight = baseHeight + serversCount * buttonEffectiveHeight;

            if (calculatedHeight < minHeight)
                calculatedHeight = minHeight;

            this.Height = calculatedHeight;
            UpdatePlayerCounts();
            Log($"Window height adjusted to {calculatedHeight} px for {serversCount} servers.");
        }

        // подгрузка html контента
        private void LoadHtmlContent()
        {
            try
            {
                using (var client = new WebClient())
                {
                    byte[] data = client.DownloadData("https://jnight.ru/launcher/news_feed.php?v=" + VERSION);
                    // Декодируем правильно, без автоопределения
                    string html = System.Text.Encoding.UTF8.GetString(data);
                    RightBrowser.NavigateToString(html);
                }
            }
            catch
            {
                RightBrowser.NavigateToString("<p>Ошибка загрузки новостей</p>");
            }
        }
        #endregion

        #region PLAYER COUNTS
        // Обновление количества игроков на кнопках
        private void UpdatePlayerCounts()
        {
            try
            {
                using (var client = new WebClient())
                {
                    client.Encoding = System.Text.Encoding.UTF8;
                    string json = client.DownloadString("https://jnight.ru/ajax/getplayerscount.php");
                    var counts = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);

                    foreach (Button btn in ServersPanel.Children.OfType<Button>())
                    {
                        // Берём текущее содержимое (может быть уже с (число) или чистое)
                        string currentText = btn.Content.ToString();
                        string baseName = currentText.Contains("(") ? currentText.Substring(0, currentText.LastIndexOf(" (")).Trim() : currentText;

                        // Ищем сервер с точно таким же Name
                        var srv = cfg.Servers.FirstOrDefault(s => s.Name == baseName);
                        if (srv != null && !string.IsNullOrEmpty(srv.Code))
                        {
                            string codeLower = srv.Code.ToLower();
                            if (counts.TryGetValue(codeLower, out var countObj) && int.TryParse(countObj.ToString(), out int count))
                            {
                                btn.Content = $"{srv.Name} ({count})";
                            }
                            else
                            {
                                btn.Content = srv.Name; // если нет данных — убираем старое число
                            }
                        }
                    }
                    Log("Player counts updated successfully.");
                }
            }
            catch (Exception ex)
            {
                Log("Error updating player counts: " + ex.Message + " | " + ex.StackTrace);
            }
        }
        #endregion

        #region LAUNCHER LOGIC
        // Ищем папку стима
        private string GetSteamPath()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Valve\Steam"))
            {
                return key?.GetValue("SteamPath")?.ToString();
            }
        }

        // Парсинг путей из vdf 
        private List<string> ParseVdf(string path)
        {
            string text = File.ReadAllText(path);
            return Regex.Matches(text, "\"path\"\\s*\"([^\"]+)\"")
                        .Cast<Match>()
                        .Select(m => m.Groups[1].Value)
                        .ToList();
        }

        // Ищем dayz exe по возмоджным путям из vdf
        private string FindDayZExe(List<string> libraries)
        {
            foreach (var basePath in libraries)
            {
                string full = Path.Combine(basePath, "steamapps", "common", "DayZ", "DayZ_BE.exe");
                if (File.Exists(full)) return full;
            }
            return null;
        }

        // Убиваем активный процесс
        private void KillDayZ()
        {
            foreach (var p in Process.GetProcessesByName("DayZ_BE"))
                p.Kill();
        }

        // Запускаем dayz с параметрами
        private void StartDayZ(string exe, string name, string ip, string port, List<string> mods)
        {
            try
            {
                string dir = Path.GetDirectoryName(exe);
                // Лог
                Log("============ StartDayZ ============");
                Log("Exe: " + exe);
                Log("Dir: " + dir);
                Log("PlayerName: " + name);
                Log("Server: " + ip + ":" + port);

                // Формируем аргументы
                string args = $"-name={name}";

                if (mods != null && mods.Count > 0)
                {
                    string modList = string.Join(";", mods.Select(m => Path.Combine(dir, "!Workshop", m)));
                    args += $" \"-mod={modList}\"";
                    Log("Mods: " + modList);
                }
                else
                {
                    Log("Mods: NONE");
                }

                args += $" -connect={ip}:{port}";
                Log("Args: " + args);

                // Подготовка процесса
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args,
                    WorkingDirectory = dir,
                    UseShellExecute = false
                };

                // Старт
                Process.Start(psi);
                Log("Process started successfully!");
            }
            catch (Exception ex)
            {
                Log("ERROR StartDayZ: " + ex);
                MessageBox.Show("Ошибка запуска DayZ");
            }
        }

        // Функция нажатия кнопок серверов, чтобы проверять состояние перед запуском
        private async void RunLauncher(Server server)
        {
            if (string.IsNullOrWhiteSpace(cfg.Name))
            {
                MessageBox.Show("Введите ник!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            SaveConfig();

            string steamPath = GetSteamPath();
            if (steamPath == null)
            {
                MessageBox.Show("Steam не найден", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdfPath))
            {
                MessageBox.Show("libraryfolders.vdf не найден", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var libraries = ParseVdf(vdfPath);
            libraries.Add(steamPath);

            string dayzPath = FindDayZExe(libraries);
            if (dayzPath == null)
            {
                MessageBox.Show("DayZ_BE.exe не найден", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            KillDayZ();

            // --- Блокируем все кнопки серверов ---
            foreach (Button btn in ServersPanel.Children.OfType<Button>())
                btn.IsEnabled = false;

            try
            {
                // Запуск DayZ
                StartDayZ(dayzPath, cfg.Name, server.Ip, server.Port, server.Mods);
                // Ждем n секунд, чтобы не спамили кнопки
                await Task.Delay(10000);
            }
            finally
            {
                // Разблокируем кнопки
                foreach (Button btn in ServersPanel.Children.OfType<Button>())
                    btn.IsEnabled = true;
            }
        }
        #endregion

        private void WebsiteLink_Click(object sender, MouseButtonEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://jnight.ru",
                UseShellExecute = true
            });
        }

        // Проверка ввода только латиницей и числами в ник
        private void NameBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var tb = sender as TextBox;
            if (tb == null) return;

            // Удаляем все символы, кроме латиницы и цифр
            int selStart = tb.SelectionStart;
            string clean = Regex.Replace(tb.Text, "[^a-zA-Z0-9]", "");
            if (tb.Text != clean)
            {
                tb.Text = clean;
                tb.SelectionStart = selStart > 0 ? selStart - 1 : 0; // корректируем позицию курсора
            }
        }
    }

    #region CONFIG CLASSES
    public class LauncherConfig
    {
        public string Name { get; set; }
        public List<Server> Servers { get; set; }

        public static LauncherConfig Default()
        {
            var config = new LauncherConfig { Name = "JNSurvivour" };

            try
            {
                using (var client = new WebClient())
                {
                    string json = client.DownloadString("https://jnight.ru/launcher/config_default.json");
                    var remoteConfig = JsonConvert.DeserializeObject<LauncherConfig>(json);
                    if (remoteConfig?.Servers != null && remoteConfig.Servers.Count > 0)
                    {
                        config.Servers = remoteConfig.Servers;
                        return config;
                    }
                }
            }
            catch { }

            // Fallback на hardcoded с добавлением Code
            config.Servers = new List<Server>
            {
                new Server { Name="JNight.ru Vanilla|PVE|NO KOS|Chernorussia", Ip="91.122.15.163", Port="2502", Mods=new List<string>(), Code="cherno" },
                new Server { Name="JNight.ru Vanilla|PVE|NO KOS|Livonia", Ip="91.122.15.163", Port="2402", Mods=new List<string>(), Code="enoch" },  
                new Server { Name="JNight.ru Vanilla|PVE|NO KOS|Sakhal", Ip="91.122.15.163", Port="2102", Mods=new List<string>(), Code="sakhal" },
                new Server { Name="JNight.ru Vanilla|PVE|NO KOS|Namalsk", Ip="91.122.15.163", Port="2302", Mods=new List<string>{"@Namalsk Survival","@Namalsk Island"}, Code="namalsk" },
                new Server { Name="JNight.ru Vanilla|PVE|NO KOS|DeerIsle", Ip="91.122.15.163", Port="2202", Mods=new List<string>{"@CF","@DeerIsle","@Disable_DeerIsle_ClassicWalk"}, Code="deerisle" },
                new Server { Name="JNight.ru Vanilla|PVE|NO KOS|Banov", Ip="91.122.15.163", Port="2101", Mods=new List<string>{"@Banov"}, Code="banov" }  
            };
            return config;
        }
    }

    public class Server
    {
        public string Name { get; set; }
        public string Ip { get; set; }
        public string Port { get; set; }
        public List<string> Mods { get; set; }
        public string Code { get; set; }  
    }
    #endregion
}