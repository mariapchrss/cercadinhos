using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Cercadinhos
{
    public class FenceItem : INotifyPropertyChanged
    {
        public string FilePath { get; set; }
        public string Name { get; set; }
        ImageSource icon;
        public ImageSource Icon
        {
            get => icon;
            set { icon = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon))); }
        }
        bool alvo;
        public bool Alvo
        {
            get => alvo;
            set { if (alvo == value) return; alvo = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Alvo))); }
        }
        public event PropertyChangedEventHandler PropertyChanged;
    }

    // O ícone meio transparente que acompanha o mouse enquanto um item é arrastado.
    static class Arrasto
    {
        static Window w;
        static double escala = 1;

        public static void Mostrar(ImageSource icone, double tamanho)
        {
            Fechar();
            if (icone == null) return;
            w = new Window
            {
                WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, Topmost = true,
                ShowInTaskbar = false, ShowActivated = false, IsHitTestVisible = false, ResizeMode = ResizeMode.NoResize,
                Width = tamanho, Height = tamanho, WindowStartupLocation = WindowStartupLocation.Manual,
                Content = new Image { Source = icone, Opacity = 0.8 },
            };
            var janela = w;
            w.SourceInitialized += (s, e) => Shell.Fantasma(new WindowInteropHelper(janela).Handle);
            Mover();
            w.Show();
            escala = VisualTreeHelper.GetDpi(w).DpiScaleX;
            Mover();
        }

        public static void Mover()
        {
            if (w == null) return;
            var p = Shell.Cursor();
            w.Left = p.X / escala - w.Width / 2;
            w.Top = p.Y / escala - w.Height / 2;
        }

        public static void Fechar()
        {
            w?.Close();
            w = null;
        }
    }

    public partial class FenceWindow : Window
    {
        const string FORMATO = "CercadinhoItem";
        const double ALTURA_TITULO = 32;

        public static readonly Dictionary<string, (string nome, Color cor)> CORES = new Dictionary<string, (string, Color)>
        {
            ["roxo"] = ("💜 Roxo", Color.FromRgb(155, 107, 255)),
            ["rosa"] = ("💗 Rosa", Color.FromRgb(255, 111, 181)),
            ["azul"] = ("💙 Azul", Color.FromRgb(90, 160, 255)),
            ["verde"] = ("💚 Verde", Color.FromRgb(76, 201, 160)),
            ["escuro"] = ("🖤 Escuro", Color.FromRgb(70, 70, 80)),
        };

        static readonly (string chave, string nome)[] ORDENS =
        {
            ("manual", "Do meu jeito"), ("nome", "Nome"), ("tipo", "Tipo"), ("data", "Mais recentes primeiro"),
        };

        public FenceData Data;
        readonly ObservableCollection<FenceItem> items = new ObservableCollection<FenceItem>();
        FileSystemWatcher watcher;
        string vigiando;
        DispatcherTimer reloadTimer, sairTimer;
        Point dragStart;
        FenceItem dragItem;
        bool pronto, menuAberto, temp;
        string nav;                 // subpasta aberta dentro de uma caixinha de pasta
        IntPtr hwnd;
        EditarWindow editor;

        // A aba que está aparecendo (a primeira é a própria caixinha).
        public TabData Aba => Data.ActiveTab > 0 && Data.ActiveTab <= Data.Tabs.Count ? Data.Tabs[Data.ActiveTab - 1] : Data;
        public IEnumerable<TabData> Abas => new TabData[] { Data }.Concat(Data.Tabs);
        string PastaAtual => Aba.Folder == null ? null : (nav ?? Aba.Folder);
        bool Portal => Aba.Folder != null;
        bool Fechada => Data.Collapsed && !temp;
        public IntPtr Hwnd => hwnd;

        public FenceWindow(FenceData data)
        {
            InitializeComponent();
            Data = data;
            Left = data.Left; Top = data.Top; Width = data.Width;
            List.ItemsSource = items;
            Aplicar();
            ApplyCollapsed();
            Reload();

            reloadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            reloadTimer.Tick += (s, e) => { reloadTimer.Stop(); Reload(); };
            sairTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            sairTimer.Tick += (s, e) =>
            {
                sairTimer.Stop();
                if (temp && !IsMouseOver && !menuAberto && !TitleEdit.IsVisible) { temp = false; ApplyCollapsed(); }
            };

            SourceInitialized += (s, e) =>
            {
                hwnd = new WindowInteropHelper(this).Handle;
                Shell.GrudarNaAreaDeTrabalho(hwnd);
                Shell.Vidro(hwnd, Data.Blur, Data.Radius);
                HwndSource.FromHwnd(hwnd).AddHook(WndProc);
            };
            Loaded += (s, e) => pronto = true;
            LocationChanged += (s, e) => Guardar();
            SizeChanged += (s, e) => Guardar();
            Closed += (s, e) => { watcher?.Dispose(); editor?.Close(); };
            DragEnter += (s, e) => Expandir();
            DragLeave += (s, e) => Marcar(null);
            DragOver += Window_DragOver;
            Drop += Window_Drop;
        }

        void Guardar()
        {
            if (!pronto) return;
            Data.Left = Left; Data.Top = Top; Data.Width = ActualWidth;
            if (!Fechada && !animando) Data.Height = ActualHeight;
            App.I.SaveSoon();
        }

        // ---- movimento suave ----

        DispatcherTimer animTimer;
        bool animando, sumindo;

        // Muda a altura deslizando (enrolar e desenrolar), em vez de pular.
        void AnimarAltura(double destino, Action fim)
        {
            animTimer?.Stop();
            if (!pronto || !IsVisible) { animando = false; Height = destino; fim?.Invoke(); return; }
            double inicio = ActualHeight;
            var t0 = DateTime.UtcNow;
            animando = true;
            animTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(10) };
            var meu = animTimer;
            animTimer.Tick += (s, e) =>
            {
                double k = Math.Min(1, (DateTime.UtcNow - t0).TotalMilliseconds / 140);
                double suave = 1 - Math.Pow(1 - k, 3);
                Height = inicio + (destino - inicio) * suave;
                if (k < 1) return;
                meu.Stop();
                animando = false;
                fim?.Invoke();
            };
            animTimer.Start();
        }

        public void Aparecer()
        {
            if (IsVisible && !sumindo) return;
            sumindo = false;
            if (!IsVisible) Show();
            Empilhar();
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(170)));
        }

        public void Sumir()
        {
            if (!IsVisible || sumindo) return;
            sumindo = true;
            var a = new DoubleAnimation(0, TimeSpan.FromMilliseconds(130));
            a.Completed += (s, e) => { if (sumindo) { sumindo = false; Hide(); } };
            BeginAnimation(OpacityProperty, a);
        }

        // ---- aparência ----

        // O que o desenho de cada item lê (ligado no XAML).
        static DependencyProperty DP<T>(string nome, T padrao) =>
            DependencyProperty.Register(nome, typeof(T), typeof(FenceWindow), new PropertyMetadata(padrao));
        public static readonly DependencyProperty IconSizeProperty = DP("IconSize", 48.0);
        public static readonly DependencyProperty CellWProperty = DP("CellW", 88.0);
        public static readonly DependencyProperty CellHProperty = DP("CellH", 96.0);
        public static readonly DependencyProperty TextSizeProperty = DP("TextSize", 12.0);
        public static readonly DependencyProperty NameMaxHProperty = DP("NameMaxH", 34.0);
        public static readonly DependencyProperty TextBrushProperty = DP<Brush>("TextBrush", Brushes.White);
        public static readonly DependencyProperty TextWeightProperty = DP("TextWeight", FontWeights.Normal);
        public static readonly DependencyProperty NamesVisProperty = DP("NamesVis", Visibility.Visible);
        public static readonly DependencyProperty TextEffectProperty = DP<System.Windows.Media.Effects.Effect>("TextEffect", null);
        public static readonly DependencyProperty TintBrushProperty = DP<Brush>("TintBrush", Brushes.Transparent);
        public static readonly DependencyProperty TintOpacityProperty = DP("TintOpacity", 0.0);
        public static readonly DependencyProperty HoverBrushProperty = DP<Brush>("HoverBrush", Brushes.Transparent);
        public Brush HoverBrush { get => (Brush)GetValue(HoverBrushProperty); set => SetValue(HoverBrushProperty, value); }
        public double IconSize { get => (double)GetValue(IconSizeProperty); set => SetValue(IconSizeProperty, value); }
        public double CellW { get => (double)GetValue(CellWProperty); set => SetValue(CellWProperty, value); }
        public double CellH { get => (double)GetValue(CellHProperty); set => SetValue(CellHProperty, value); }
        public double TextSize { get => (double)GetValue(TextSizeProperty); set => SetValue(TextSizeProperty, value); }
        public double NameMaxH { get => (double)GetValue(NameMaxHProperty); set => SetValue(NameMaxHProperty, value); }
        public Brush TextBrush { get => (Brush)GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }
        public FontWeight TextWeight { get => (FontWeight)GetValue(TextWeightProperty); set => SetValue(TextWeightProperty, value); }
        public Visibility NamesVis { get => (Visibility)GetValue(NamesVisProperty); set => SetValue(NamesVisProperty, value); }
        public System.Windows.Media.Effects.Effect TextEffect { get => (System.Windows.Media.Effects.Effect)GetValue(TextEffectProperty); set => SetValue(TextEffectProperty, value); }
        public Brush TintBrush { get => (Brush)GetValue(TintBrushProperty); set => SetValue(TintBrushProperty, value); }
        public double TintOpacity { get => (double)GetValue(TintOpacityProperty); set => SetValue(TintOpacityProperty, value); }

        static readonly System.Windows.Media.Effects.Effect SOMBRA = CriarSombra();
        static System.Windows.Media.Effects.Effect CriarSombra()
        {
            var s = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, BlurRadius = 4, ShadowDepth = 1, Opacity = 0.9 };
            s.Freeze();
            return s;
        }

        // Cor da lista (roxo, rosa…) ou "#RRGGBB".
        public static Color CorDe(string valor, Color padrao)
        {
            if (valor != null && CORES.TryGetValue(valor, out var c)) return c.cor;
            try { if (!string.IsNullOrEmpty(valor)) return (Color)ColorConverter.ConvertFromString(valor); } catch { }
            return padrao;
        }

        // Transparência 0 vira quase 0: pixel totalmente transparente deixa o clique passar pra trás.
        static byte Alfa(int pct) => (byte)Math.Max(1, Math.Min(255, pct * 255 / 100));

        // Raio de verdade: com vidro, quem arredonda é o Windows (8 ou 4), então o desenho acompanha.
        static double Raio(FenceData d) => d.Blur ? Shell.RaioDoVidro(d.Radius) : d.Radius;

        // Cores de fundo, título e texto de uma caixinha (também usadas nas miniaturas do aplicativo).
        public static (Brush corpo, Brush titulo, Brush texto) Pinceis(FenceData d)
        {
            var cor = CorDe(d.Color, CORES["roxo"].cor);
            // escuro: a cor bem escurecida; claro: a cor quase branca
            Color fundo = d.Claro
                ? Color.FromRgb((byte)(cor.R + (255 - cor.R) * 0.82), (byte)(cor.G + (255 - cor.G) * 0.82), (byte)(cor.B + (255 - cor.B) * 0.82))
                : Color.FromRgb((byte)(cor.R * 0.4), (byte)(cor.G * 0.4), (byte)(cor.B * 0.4));
            var corpo = new SolidColorBrush(Color.FromArgb(Alfa(d.Opacity), fundo.R, fundo.G, fundo.B));
            var titulo = new SolidColorBrush(Color.FromArgb(Alfa(d.TitleOpacity), cor.R, cor.G, cor.B));
            var texto = new SolidColorBrush(CorDe(d.TextColor, Colors.White));
            corpo.Freeze(); titulo.Freeze(); texto.Freeze();
            return (corpo, titulo, texto);
        }

        public void Aplicar()
        {
            var d = Data;
            var (corpo, titulo, texto) = Pinceis(d);
            Frame.Background = corpo;
            TitleBar.Background = titulo;
            Frame.BorderThickness = new Thickness(d.Border ? 1 : 0);
            Frame.BorderBrush = new SolidColorBrush(d.Claro ? Color.FromArgb(40, 0, 0, 0) : Color.FromArgb(64, 255, 255, 255));
            TitleBar.BorderBrush = new SolidColorBrush(d.Claro ? Color.FromArgb(22, 0, 0, 0) : Color.FromArgb(28, 255, 255, 255));
            HoverBrush = new SolidColorBrush(d.Claro ? Color.FromArgb(30, 0, 0, 0) : Color.FromArgb(46, 255, 255, 255));
            Frame.CornerRadius = new CornerRadius(Raio(d));
            if (hwnd != IntPtr.Zero) Shell.Vidro(hwnd, d.Blur, d.Radius);

            try { FontFamily = new FontFamily(d.Font); } catch { }
            var sombra = d.Shadow ? SOMBRA : null;
            TextBrush = texto; TextEffect = sombra;
            TitleText.Foreground = texto; MenuDots.Foreground = texto; BackArrow.Foreground = texto;
            Hint.Foreground = texto; Grip.Stroke = texto;
            TitleText.Effect = sombra;
            TitleText.FontSize = d.FontSize + 1;
            TextSize = d.FontSize;
            TextWeight = d.Bold ? FontWeights.Bold : FontWeights.Normal;

            IconSize = d.IconSize;
            NamesVis = d.ShowNames ? Visibility.Visible : Visibility.Collapsed;
            NameMaxH = Math.Ceiling(d.FontSize * 1.4 * 2);
            CellW = d.ShowNames ? Math.Max(d.IconSize + 40, d.FontSize * 6) : d.IconSize + 20;
            CellH = d.IconSize + (d.ShowNames ? 14 + NameMaxH : 12);

            var tinta = new SolidColorBrush(CorDe(d.Tint, CORES["roxo"].cor));
            tinta.Freeze();
            TintBrush = tinta;
            TintOpacity = Math.Max(0, Math.Min(100, d.TintStrength)) / 100.0;

            bool trancada = App.I.Cfg.Locked;
            Alcas.IsHitTestVisible = !trancada;
            Grip.Visibility = trancada ? Visibility.Collapsed : Visibility.Visible;
            Cantos();
            AtualizarTitulo();
            AtualizarFade(false);
        }

        void Cantos()
        {
            double r = Math.Max(0, Raio(Data) - 1);
            TitleBar.CornerRadius = Fechada ? new CornerRadius(r) : new CornerRadius(r, r, 0, 0);
            TitleBar.BorderThickness = new Thickness(0, 0, 0, Fechada ? 0 : 1);
        }

        void ApplyCollapsed()
        {
            Grip.Opacity = Fechada ? 0 : 0.55;
            if (Fechada)
            {
                // desliza até virar só o título, e só então esconde o corpo
                AnimarAltura(ALTURA_TITULO, () => { if (Fechada) { Body.Visibility = Visibility.Collapsed; Cantos(); } });
            }
            else
            {
                Body.Visibility = Visibility.Visible;
                Cantos();
                AnimarAltura(Math.Max(Data.Height, 80), null);
            }
        }

        void ToggleCollapsed()
        {
            Data.Collapsed = !Data.Collapsed;
            temp = false;
            ApplyCollapsed();
            App.I.SaveSoon();
        }

        // Enrolada abre sozinha enquanto o mouse está em cima.
        void Expandir()
        {
            if (Data.Collapsed && !temp) { temp = true; ApplyCollapsed(); }
        }

        // Camaleão: sem o mouse em cima, os ícones ficam apagadinhos.
        void AtualizarFade(bool animar = true)
        {
            double alvo = (IsMouseOver || menuAberto || Data.Fade >= 100) ? 1 : Math.Max(0.08, Data.Fade / 100.0);
            Body.BeginAnimation(OpacityProperty, new DoubleAnimation(alvo, TimeSpan.FromMilliseconds(animar ? 180 : 0)));
        }

        void Win_MouseEnter(object sender, MouseEventArgs e)
        {
            sairTimer.Stop();
            if (App.I.Cfg.AbrirAoPassar) Expandir();
            AtualizarFade();
        }

        void Win_MouseLeave(object sender, MouseEventArgs e)
        {
            AtualizarFade();
            if (temp) { sairTimer.Stop(); sairTimer.Start(); }
        }

        // ---- conteúdo ----

        static IEnumerable<string> Ordenar(IEnumerable<string> paths, string ordem)
        {
            switch (ordem)
            {
                case "nome": return paths.OrderBy(p => Shell.DisplayName(p), StringComparer.CurrentCultureIgnoreCase);
                case "tipo":
                    return paths.OrderBy(p => Directory.Exists(p) ? 0 : 1)
                        .ThenBy(p => System.IO.Path.GetExtension(p), StringComparer.OrdinalIgnoreCase)
                        .ThenBy(p => Shell.DisplayName(p), StringComparer.CurrentCultureIgnoreCase);
                case "data": return paths.OrderByDescending(p => { try { return File.GetLastWriteTime(p); } catch { return DateTime.MinValue; } });
                default: return paths;
            }
        }

        public void Reload()
        {
            items.Clear();
            var pasta = PastaAtual;
            List<string> paths;
            if (pasta != null)
            {
                try
                {
                    var todos = new DirectoryInfo(pasta).EnumerateFileSystemInfos()
                        .Where(i => (i.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0)
                        .Select(i => i.FullName).ToList();
                    paths = Ordenar(todos, string.IsNullOrEmpty(Aba.Sort) || Aba.Sort == "manual" ? "data" : Aba.Sort).Take(200).ToList();
                }
                catch { paths = new List<string>(); }
            }
            else paths = Ordenar(Aba.Items.Where(p => File.Exists(p) || Directory.Exists(p)), Aba.Sort).ToList();

            int px = Data.IconSize <= 48 ? 64 : 128;
            foreach (var p in paths)
            {
                var item = new FenceItem { FilePath = p, Name = Shell.DisplayName(p) };
                items.Add(item);
                // os ícones entram aos poucos, pra janela não travar com pasta grande
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => item.Icon = Shell.Icon(p, px)));
            }
            Hint.Text = Portal ? "Pasta vazia" : "Arraste arquivos pra cá";
            Hint.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            Vigiar(pasta);
            AtualizarTitulo();
        }

        void Vigiar(string pasta)
        {
            if (pasta == vigiando) return;
            watcher?.Dispose();
            watcher = null;
            vigiando = pasta;
            if (pasta == null || !Directory.Exists(pasta)) return;
            try
            {
                watcher = new FileSystemWatcher(pasta) { EnableRaisingEvents = true };
                FileSystemEventHandler mudou = (s, e) => Dispatcher.BeginInvoke(new Action(ReloadLogo));
                watcher.Created += mudou;
                watcher.Deleted += mudou;
                watcher.Renamed += (s, e) => mudou(s, e);
            }
            catch { }
        }

        public void ReloadLogo()
        {
            reloadTimer.Stop();
            reloadTimer.Start();
        }

        public void RemoveItem(string path)
        {
            Aba.Items.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            Reload();
            App.I.SaveSoon();
        }

        void AddItems(IEnumerable<string> paths, int index)
        {
            var lista = Aba.Items;
            foreach (var p in paths)
            {
                if (lista.Any(x => string.Equals(x, p, StringComparison.OrdinalIgnoreCase))) continue;
                if (index >= 0 && index <= lista.Count) lista.Insert(index++, p);
                else lista.Add(p);
            }
            Reload();
            App.I.SaveSoon();
        }

        void Open(string path)
        {
            // pasta dentro de caixinha de pasta: entra nela aqui mesmo
            if (Portal && Directory.Exists(path)) { nav = path; Reload(); return; }
            AbrirFora(path);
        }

        static void AbrirFora(string path)
        {
            App.I.FimDoPeek();
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show("Não consegui abrir:\n" + ex.Message, "Cercadinhos"); }
        }

        static FenceItem ItemDe(object source) => (source as FrameworkElement)?.DataContext as FenceItem;

        // ---- título e abas ----

        void AtualizarTitulo()
        {
            if (TitleEdit.IsVisible) return;
            string sub = nav == null ? "" : " › " + Shell.DisplayName(nav);
            BackBtn.Visibility = nav == null ? Visibility.Collapsed : Visibility.Visible;
            bool temAbas = Data.Tabs.Count > 0;
            TitleText.Visibility = temAbas ? Visibility.Collapsed : Visibility.Visible;
            TabsPanel.Visibility = temAbas ? Visibility.Visible : Visibility.Collapsed;
            TitleText.Text = Data.Title + sub;
            TabsPanel.Children.Clear();
            TabsPanel.Margin = new Thickness(nav == null ? 3 : 32, 0, 30, 0);
            if (!temAbas) return;

            // a aba escolhida fica mais escura (ou mais clara, no fundo claro); as outras, apagadinhas
            var fundoAtiva = new SolidColorBrush(Data.Claro ? Color.FromArgb(120, 255, 255, 255) : Color.FromArgb(70, 0, 0, 0));
            int i = 0;
            foreach (var t in Abas)
            {
                int idx = i++;
                bool ativa = idx == Data.ActiveTab;
                var texto = new TextBlock
                {
                    Text = t.Title + (ativa ? sub : ""), Foreground = TextBrush, FontSize = Data.FontSize + 1,
                    FontWeight = ativa ? FontWeights.SemiBold : FontWeights.Normal, Opacity = ativa ? 1 : 0.6,
                    Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis, Effect = TextEffect,
                };
                var aba = new Border
                {
                    Child = texto, CornerRadius = new CornerRadius(5), Margin = new Thickness(1, 3, 1, 3), Cursor = Cursors.Hand,
                    Background = ativa ? fundoAtiva : Brushes.Transparent, ToolTip = "Dois cliques pra renomear",
                    AllowDrop = true,
                };
                aba.MouseLeftButtonDown += (s, e) =>
                {
                    e.Handled = true;
                    if (e.ClickCount == 2) { TrocarAba(idx); ComecarRenomear(); }
                    else TrocarAba(idx);
                };
                aba.MouseRightButtonUp += (s, e) => { e.Handled = true; TrocarAba(idx); AbrirMenu(aba); };
                // arrastar um arquivo e parar em cima da aba troca pra ela
                aba.DragEnter += (s, e) => TrocarAba(idx);
                TabsPanel.Children.Add(aba);
            }
        }

        void TrocarAba(int idx)
        {
            if (idx == Data.ActiveTab) return;
            Data.ActiveTab = idx;
            nav = null;
            Reload();
            App.I.SaveSoon();
        }

        void NovaAba()
        {
            Data.Tabs.Add(new TabData { Title = "Nova aba" });
            Data.ActiveTab = Data.Tabs.Count;
            nav = null;
            Reload();
            App.I.SaveSoon();
            ComecarRenomear();
        }

        void RemoverAba(int idx)
        {
            if (Data.Tabs.Count == 0) return;
            if (idx == 0) { Data.CopiarConteudo(Data.Tabs[0]); Data.Tabs.RemoveAt(0); }
            else Data.Tabs.RemoveAt(idx - 1);
            if (Data.ActiveTab >= idx && Data.ActiveTab > 0) Data.ActiveTab--;
            Data.ActiveTab = Math.Min(Data.ActiveTab, Data.Tabs.Count);
            nav = null;
            Reload();
            App.I.SaveSoon();
        }

        void SepararAba()
        {
            var f = new FenceData { Left = Data.Left + 30, Top = Data.Top + 40, Width = Data.Width, Height = Data.Height, Page = Data.Page };
            f.CopiarConteudo(Aba);
            f.CopiarAparencia(Data, true);
            RemoverAba(Data.ActiveTab);
            App.I.Adicionar(f);
        }

        void VirarAbaDe(FenceWindow outra)
        {
            foreach (var t in Abas)
            {
                var nova = new TabData();
                nova.CopiarConteudo(t);
                outra.Data.Tabs.Add(nova);
            }
            outra.Data.ActiveTab = outra.Data.Tabs.Count - Data.Tabs.Count;
            outra.nav = null;
            outra.Reload();
            App.I.Apagar(this);
        }

        void Title_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (TitleEdit.IsVisible) return;
            if (e.ClickCount == 2) { ToggleCollapsed(); return; }
            if (App.I.Cfg.Locked) return;
            try { DragMove(); } catch { }
        }

        // Dois cliques em cima do nome renomeia; um clique só continua servindo pra arrastar.
        void TitleText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount != 2) return;
            e.Handled = true;
            ComecarRenomear();
        }

        void Title_MouseRightButtonUp(object sender, MouseButtonEventArgs e) => AbrirMenu(TitleBar);

        void MenuBtn_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            AbrirMenu(MenuBtn);
        }

        void BackBtn_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (nav == null) return;
            var pai = System.IO.Path.GetDirectoryName(nav.TrimEnd('\\'));
            bool dentro = pai != null && pai.TrimEnd('\\').Length > Aba.Folder.TrimEnd('\\').Length
                && pai.StartsWith(Aba.Folder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            nav = dentro ? pai : null;
            Reload();
        }

        static MenuItem Opcao(string texto, Action acao)
        {
            var m = new MenuItem { Header = texto };
            m.Click += (s, e) => acao();
            return m;
        }

        void Mostrar(ContextMenu menu)
        {
            menuAberto = true;
            menu.Closed += (s, e) =>
            {
                menuAberto = false;
                AtualizarFade();
                if (temp && !IsMouseOver) { sairTimer.Stop(); sairTimer.Start(); }
            };
            menu.IsOpen = true;
        }

        void AbrirMenu(UIElement alvo)
        {
            var menu = new ContextMenu { PlacementTarget = alvo };
            menu.Items.Add(Opcao("🎨 Editar aparência e regra…", Editar));
            menu.Items.Add(Opcao("Renomear", ComecarRenomear));
            var cores = new MenuItem { Header = "Cor" };
            foreach (var kv in CORES)
            {
                var chave = kv.Key;
                var m = Opcao(kv.Value.nome, () => { Data.Color = chave; Aplicar(); App.I.SaveSoon(); });
                m.IsChecked = Data.Color == chave;
                cores.Items.Add(m);
            }
            menu.Items.Add(cores);
            var ordem = new MenuItem { Header = "Ordenar por" };
            string atual = string.IsNullOrEmpty(Aba.Sort) ? (Portal ? "data" : "manual") : Aba.Sort;
            foreach (var (chave, nome) in ORDENS)
            {
                if (Portal && chave == "manual") continue;
                var m = Opcao(nome, () => { Aba.Sort = chave; Reload(); App.I.SaveSoon(); });
                m.IsChecked = atual == chave;
                ordem.Items.Add(m);
            }
            menu.Items.Add(ordem);
            menu.Items.Add(Opcao(Data.Collapsed ? "Desenrolar" : "Enrolar (abre quando o mouse passa)", ToggleCollapsed));
            if (Portal) menu.Items.Add(Opcao("Abrir a pasta no Explorer", () => AbrirFora(PastaAtual)));

            menu.Items.Add(new Separator());
            menu.Items.Add(Opcao("Nova aba", NovaAba));
            var outras = App.I.Janelas.Where(w => w != this && w.Data.Page == Data.Page).ToList();
            if (outras.Count > 0)
            {
                var juntar = new MenuItem { Header = "Virar aba de" };
                foreach (var w in outras)
                {
                    var alvoW = w;
                    juntar.Items.Add(Opcao(w.Data.Title, () => VirarAbaDe(alvoW)));
                }
                menu.Items.Add(juntar);
            }
            if (Data.Tabs.Count > 0)
            {
                menu.Items.Add(Opcao("Separar esta aba em outra caixinha", SepararAba));
                menu.Items.Add(Opcao("Fechar esta aba", () =>
                {
                    var r = MessageBox.Show("Fechar a aba \"" + Aba.Title + "\"?\n\nOs arquivos continuam onde estão.",
                        "Cercadinhos", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (r == MessageBoxResult.Yes) RemoverAba(Data.ActiveTab);
                }));
            }
            var paginas = new MenuItem { Header = "Mover pra página" };
            int total = App.I.TotalPaginas;
            for (int i = 0; i <= total; i++)
            {
                int pg = i;
                var m = Opcao(i == total ? "Página nova" : "Página " + (i + 1), () => App.I.MoverParaPagina(this, pg));
                m.IsChecked = i == Data.Page;
                paginas.Items.Add(m);
            }
            menu.Items.Add(paginas);

            menu.Items.Add(new Separator());
            menu.Items.Add(Opcao("Nova caixinha", () => App.I.Nova(null)));
            menu.Items.Add(Opcao("Apagar esta caixinha", () =>
            {
                var r = MessageBox.Show("Apagar a caixinha \"" + Data.Title + "\"?\n\nOs arquivos continuam onde estão.",
                    "Cercadinhos", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (r == MessageBoxResult.Yes) App.I.Apagar(this);
            }));
            Mostrar(menu);
        }

        public void AbrirMenuDeTeste() => AbrirMenu(MenuBtn);

        public void Editar()
        {
            if (editor != null) { editor.Activate(); return; }
            editor = new EditarWindow(this);
            editor.Closed += (s, e) => { editor = null; Reload(); };
            editor.Show();
        }

        public void ComecarRenomear()
        {
            TitleEdit.Text = Aba.Title;
            TitleText.Visibility = Visibility.Hidden;
            TabsPanel.Visibility = Visibility.Hidden;
            TitleEdit.Visibility = Visibility.Visible;
            Activate();
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => { TitleEdit.Focus(); TitleEdit.SelectAll(); }));
        }

        void TerminarRenomear(bool salvar)
        {
            if (!TitleEdit.IsVisible) return;
            if (salvar && TitleEdit.Text.Trim().Length > 0)
            {
                Aba.Title = TitleEdit.Text.Trim();
                App.I.SaveSoon();
            }
            TitleEdit.Visibility = Visibility.Collapsed;
            AtualizarTitulo();
        }

        void TitleEdit_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) TerminarRenomear(true);
            else if (e.Key == Key.Escape) TerminarRenomear(false);
        }

        void TitleEdit_LostFocus(object sender, KeyboardFocusChangedEventArgs e) => TerminarRenomear(true);

        // ---- itens: abrir, menu, arrastar ----

        void List_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var item = ItemDe(e.OriginalSource);
            if (item == null) return;
            if (e.ClickCount == 2) { dragItem = null; Open(item.FilePath); e.Handled = true; return; }
            dragStart = e.GetPosition(this);
            dragItem = item;
        }

        void List_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (dragItem == null || e.LeftButton != MouseButtonState.Pressed) { dragItem = null; return; }
            var d = e.GetPosition(this) - dragStart;
            if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;

            var item = dragItem;
            dragItem = null;
            var data = new DataObject();
            data.SetData(FORMATO, Data.Id + "\n" + item.FilePath);
            data.SetData(DataFormats.FileDrop, new[] { item.FilePath });
            // o ícone vai junto com o mouse
            GiveFeedbackEventHandler segue = (s, a) => { Arrasto.Mover(); a.UseDefaultCursors = true; a.Handled = true; };
            GiveFeedback += segue;
            Arrasto.Mostrar(item.Icon, Data.IconSize);
            try { DragDrop.DoDragDrop(this, data, DragDropEffects.Move | DragDropEffects.Copy | DragDropEffects.Link); }
            finally { GiveFeedback -= segue; Arrasto.Fechar(); }
            // foi levado pra outra pasta pelo Explorer: some daqui
            if (!Portal && !File.Exists(item.FilePath) && !Directory.Exists(item.FilePath)) RemoveItem(item.FilePath);
        }

        void List_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            var item = ItemDe(e.OriginalSource);
            if (item == null) return;
            e.Handled = true;
            var menu = new ContextMenu();
            menu.Items.Add(Opcao("Abrir", () => Open(item.FilePath)));
            if (Portal && Directory.Exists(item.FilePath))
                menu.Items.Add(Opcao("Abrir no Explorer", () => AbrirFora(item.FilePath)));
            menu.Items.Add(Opcao("Mostrar na pasta", () =>
            {
                try { Process.Start("explorer.exe", "/select,\"" + item.FilePath + "\""); } catch { }
            }));
            if (!Portal)
            {
                menu.Items.Add(new Separator());
                menu.Items.Add(Opcao("Tirar da caixinha", () => RemoveItem(item.FilePath)));
            }
            Mostrar(menu);
        }

        FenceItem marcado;

        void Marcar(FenceItem item)
        {
            if (marcado == item) return;
            if (marcado != null) marcado.Alvo = false;
            marcado = item;
            if (marcado != null) marcado.Alvo = true;
        }

        void Window_DragOver(object sender, DragEventArgs e)
        {
            bool ordenavel = !Portal && (string.IsNullOrEmpty(Aba.Sort) || Aba.Sort == "manual");
            Marcar(ordenavel && e.Data.GetDataPresent(FORMATO) ? ItemDe(e.OriginalSource) : null);
            if (e.Data.GetDataPresent(FORMATO)) e.Effects = DragDropEffects.Move;
            else if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effects = Portal ? DragDropEffects.Move : DragDropEffects.Link;
            else e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        void Window_Drop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            Marcar(null);
            Expandir();
            var sobre = ItemDe(e.OriginalSource);
            var lista = Aba.Items;
            bool manual = string.IsNullOrEmpty(Aba.Sort) || Aba.Sort == "manual";

            if (e.Data.GetDataPresent(FORMATO))
            {
                var partes = ((string)e.Data.GetData(FORMATO)).Split(new[] { '\n' }, 2);
                var origem = App.I.Find(partes[0]);
                var path = partes[1];
                bool mesmaLista = origem == this && lista.Any(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
                if (mesmaLista)
                {
                    if (Portal || !manual || sobre == null || sobre.FilePath == path) return;
                    lista.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
                    int i = lista.FindIndex(p => string.Equals(p, sobre.FilePath, StringComparison.OrdinalIgnoreCase));
                    lista.Insert(Math.Max(i, 0), path);
                    Reload();
                    App.I.SaveSoon();
                    return;
                }
                if (Portal) { MoverPraPasta(new[] { path }); origem?.ReloadLogo(); return; }
                if (origem != null) App.I.TirarDeTodas(path);
                int onde = sobre == null ? -1 : lista.FindIndex(p => string.Equals(p, sobre.FilePath, StringComparison.OrdinalIgnoreCase));
                AddItems(new[] { path }, onde);
                return;
            }

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (Portal) MoverPraPasta(paths);
                else AddItems(paths, -1);
            }
        }

        // Caixinha de pasta: soltar um arquivo nela move o arquivo de verdade (o Windows pergunta se já existir).
        void MoverPraPasta(IEnumerable<string> paths)
        {
            foreach (var p in paths)
            {
                try
                {
                    var destino = System.IO.Path.Combine(PastaAtual, System.IO.Path.GetFileName(p.TrimEnd('\\')));
                    if (string.Equals(destino, p, StringComparison.OrdinalIgnoreCase)) continue;
                    if (Directory.Exists(p))
                        Microsoft.VisualBasic.FileIO.FileSystem.MoveDirectory(p, destino, Microsoft.VisualBasic.FileIO.UIOption.AllDialogs);
                    else
                        Microsoft.VisualBasic.FileIO.FileSystem.MoveFile(p, destino, Microsoft.VisualBasic.FileIO.UIOption.AllDialogs);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { MessageBox.Show("Não consegui mover:\n" + ex.Message, "Cercadinhos"); }
            }
        }

        // ---- redimensionar pelas bordas e cantos ----

        void Alca_DragDelta(object sender, DragDeltaEventArgs e)
        {
            var lado = (string)((FrameworkElement)sender).Tag;
            const double MIN_ALT = 80;
            if (lado.Contains("R")) Width = Math.Max(MinWidth, ActualWidth + e.HorizontalChange);
            if (lado.Contains("L"))
            {
                double nova = Math.Max(MinWidth, ActualWidth - e.HorizontalChange);
                Left += ActualWidth - nova;
                Width = nova;
            }
            if (Fechada) return;
            if (lado.Contains("B")) Height = Math.Max(MIN_ALT, ActualHeight + e.VerticalChange);
            if (lado.Contains("T"))
            {
                double nova = Math.Max(MIN_ALT, ActualHeight - e.VerticalChange);
                Top += ActualHeight - nova;
                Height = nova;
            }
        }

        // ---- janela: ficar atrás das outras (ou por cima, no Peek) e encostar ao arrastar ----

        public void Empilhar()
        {
            if (hwnd != IntPtr.Zero) Shell.Empilhar(hwnd, App.Peek);
        }

        IntPtr WndProc(IntPtr h, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_WINDOWPOSCHANGING = 0x46, WM_MOVING = 0x216;
            if (msg == WM_WINDOWPOSCHANGING)
            {
                // Só quando o Windows quer trocar a ordem (um clique traria a caixinha pra frente).
                // Mexer nisso a cada pixel arrastado refazia a pilha de janelas e travava o movimento.
                var pos = Marshal.PtrToStructure<Shell.WINDOWPOS>(lParam);
                if ((pos.flags & 0x4u) == 0)
                {
                    pos.hwndInsertAfter = new IntPtr(App.Peek ? -1 : 1); // HWND_TOPMOST ou HWND_BOTTOM
                    Marshal.StructureToPtr(pos, lParam, false);
                }
            }
            else if (msg == WM_MOVING && App.I.Cfg.Ima && (Keyboard.Modifiers & ModifierKeys.Alt) == 0)
            {
                var r = Marshal.PtrToStructure<Shell.RECT>(lParam);
                var outras = new List<Shell.RECT>();
                foreach (var w in App.I.Janelas)
                    if (w != this && w.IsVisible && Shell.GetWindowRect(w.hwnd, out var o)) outras.Add(o);
                Shell.Encostar(ref r, outras);
                Marshal.StructureToPtr(r, lParam, false);
            }
            return IntPtr.Zero;
        }
    }
}
