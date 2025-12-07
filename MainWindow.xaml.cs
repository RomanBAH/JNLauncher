using Microsoft.Win32;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Navigation;
using System.Xml;


namespace JNightLauncher
{
    public partial class MainWindow : Window
    {
        private const string CONFIG_FILE = "jnconfig.json";
        private const string VERSION = "0.0.1";
        private LauncherConfig cfg;

        // Логирование
        private void Log(string message)
        {
            try
            {
                string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "launcher.log");
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
            BuildGUI();
            LoadHtmlContent();
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
            File.WriteAllText(CONFIG_FILE, JsonConvert.SerializeObject(cfg, Newtonsoft.Json.Formatting.Indented));
        }
        #endregion

        
        #region GUI
        // Собираем конпки
        private void BuildGUI()
        {
            // Ник
            NameBox.TextChanged += (s, e) => cfg.Name = NameBox.Text;

            // Кнопки серверов
            ServersPanel.Children.Clear();
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
                    Padding = new Thickness(4, 5, 4, 5)
                };

                // Эффект нажатия
                btn.PreviewMouseLeftButtonDown += (s, e) => btn.Background = System.Windows.Media.Brushes.Gray;
                btn.PreviewMouseLeftButtonUp += (s, e) => btn.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#3E444C");

                btn.Click += (s, e) => RunLauncher(srv);
                ServersPanel.Children.Add(btn);
            }
        }

        // подгрузка html контента
        private void LoadHtmlContent()
        {
            try
            {
                using (var client = new WebClient())
                {
                    byte[] data = client.DownloadData("https://jnight.ru/news_feed.php?v=" + VERSION);

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
                    // Моды в кавычках, как требует DayZ
                    // -mod="mod1;mod2;..."
                    string modList = string.Join(";", mods.Select(m =>
                        Path.Combine(dir, "!Workshop", m)));

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

        // Здесь вставляем обработчик клика по ссылке в логотипе
        private void WebsiteLink_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            System.Diagnostics.Process.Start(new ProcessStartInfo
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
            return new LauncherConfig
            {
                Name = "JNSurvivour",
                Servers = new List<Server>
                {
                    new Server { Name="JNight.ru Vanilla|PVE|NO KOS|Chernorussia", Ip="91.122.15.163", Port="2502", Mods=new List<string>() },
                    new Server { Name="JNight.ru Vanilla|PVE|NO KOS|Livonia", Ip="91.122.15.163", Port="2402", Mods=new List<string>() },
                    new Server { Name="JNight.ru Vanilla|PVE|NO KOS|Sakhal", Ip="91.122.15.163", Port="2102", Mods=new List<string>() },
                    new Server { Name="JNight.ru Vanilla|PVE|NO KOS|Namalsk", Ip="91.122.15.163", Port="2302", Mods=new List<string>{"@Namalsk Survival","@Namalsk Island"} },
                    new Server { Name="JNight.ru Vanilla|PVE|NO KOS|DeerIsle", Ip="91.122.15.163", Port="2202", Mods=new List<string>{"@CF","@DeerIsle","@Disable_DeerIsle_ClassicWalk"} },
                }
            };
        }
    }

    public class Server
    {
        public string Name { get; set; }
        public string Ip { get; set; }
        public string Port { get; set; }
        public List<string> Mods { get; set; }
    }
    #endregion




}
