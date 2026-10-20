using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WF = System.Windows.Forms;

namespace Cercadinhos
{
    public partial class App : Application
    {
        public static App I;
        public static bool Peek;          // caixinhas por cima de todas as janelas
        public Config Cfg;
        readonly List<FenceWindow> wins = new List<FenceWindow>();
        readonly List<FileSystemWatcher> vigias = new List<FileSystemWatcher>();
        WF.NotifyIcon tray;
        Mutex mutex;
        DispatcherTimer saveTimer, peekTimer;
        bool semSalvar, escondidas, peekViuNossa;
        string avisoInicio;   // problema ao abrir as caixinhas, mostrado na bandeja
        IntPtr peekFrente;
        string atalhoPeek = "";

        // chave da regra, título, cor, extensões (vazio = pastas, null = o que sobrar)
        public static readonly (string chave, string titulo, string cor, string[] ext)[] CATEGORIAS =
        {
            ("pastas", "📁 Pastas", "azul", new string[0]),
            ("programas", "🚀 Programas", "roxo", new[] { ".lnk", ".exe", ".url", ".bat", ".cmd", ".appref-ms" }),
            ("documentos", "📄 Documentos", "rosa", new[] { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".csv", ".ppt", ".pptx", ".txt", ".rtf", ".odt" }),
            ("imagens", "🖼️ Imagens e vídeos", "verde", new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".mp4", ".mkv", ".mov", ".webm", ".mp3" }),
            ("outros", "📦 Outros", "escuro", null),
        };

        static string[] PastasDaAreaDeTrabalho => new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
        };

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            I = this;
            // erro inesperado vai pro erro.log em vez de derrubar o programa
            DispatcherUnhandledException += (s, a) => { Anotar(a.Exception.ToString()); a.Handled = true; };

            foreach (var teste in new[] { "--foto", "--tela", "--menu", "--central" })
            {
                int i = Array.IndexOf(e.Args, teste);
                if (i < 0 || i + 1 >= e.Args.Length) continue;
                if (teste == "--foto") Foto(e.Args[i + 1]);
                else if (teste == "--tela") FotoDaTela(e.Args[i + 1]);
                else if (teste == "--menu") FotoDoMenu(e.Args[i + 1]);
                else FotoDaCentral(e.Args[i + 1], i + 2 < e.Args.Length ? e.Args[i + 2] : "caixinhas");
                return;
            }

            // Abrir de novo (pelo Menu Iniciar) só chama a janela do que já está ligado.
            mutex = new Mutex(true, "Cercadinhos.UmaSo", out bool primeira);
            var sinal = new EventWaitHandle(false, EventResetMode.AutoReset, "Cercadinhos.Abrir");
            // já tem um ligado: quem abriu pelo Menu Iniciar quer ver a janela; o início do Windows (--quieto), não
            if (!primeira) { if (!e.Args.Contains("--quieto")) sinal.Set(); Shutdown(); return; }
            new Thread(() =>
            {
                while (true) { sinal.WaitOne(); Dispatcher.BeginInvoke(new Action(MostrarCentral)); }
            }) { IsBackground = true }.Start();

            Cfg = Store.Load(out string problema);
            if (Cfg == null)
            {
                // Não deu pra ler as caixinhas: nunca seguir vazio e salvar por cima.
                Anotar("não consegui ler o config.json (" + problema + "); guardei uma cópia como config.ilegivel-…");
                Cfg = Store.UltimoBackup(out string qual);
                if (Cfg != null) avisoInicio = "Não consegui abrir suas caixinhas, então voltei a cópia de segurança " + qual + ".";
                else
                {
                    Cfg = new Config { Welcomed = true };
                    semSalvar = true;
                    avisoInicio = "Não consegui abrir suas caixinhas. Não mexi em nada: feche e abra o Cercadinhos de novo.";
                }
                Anotar(avisoInicio);
            }
            Migrar();
            Shell.ConsertarInicio();
            saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            saveTimer.Tick += (s, a) => { saveTimer.Stop(); Store.Save(Cfg); };
            peekTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            peekTimer.Tick += (s, a) => ConferirPeek();

            RegistrarAtalhos();
            CriarBandeja();
            foreach (var f in Cfg.Fences) Abrir(f);
            // sem as caixinhas de verdade, não junta nada em "Outros" (era assim que a arrumação se perdia)
            if (Shell.IconesEscondidos() && !semSalvar) GuardarSoltos();
            if (avisoInicio != null) tray?.ShowBalloonTip(8000, "Cercadinhos", avisoInicio, WF.ToolTipIcon.Warning);
            VigiarAreaDeTrabalho();
            Shell.VigiarDoisCliques(() => { if (Cfg.DoisCliques) AlternarEscondidas(); });

            if (!Cfg.Welcomed) { Cfg.Welcomed = true; SaveSoon(); }
            // ligado junto com o Windows fica quietinho; aberto por alguém, mostra a janela
            if (!e.Args.Contains("--quieto")) MostrarCentral();
        }

        CentralWindow central;

        public void MostrarCentral()
        {
            if (central == null)
            {
                central = new CentralWindow();
                central.Closed += (s, a) => central = null;
                central.Show();
            }
            else central.Atualizar();
            if (central.WindowState == WindowState.Minimized) central.WindowState = WindowState.Normal;
            central.Activate();
        }

        // Traz a caixinha pra frente de tudo (trocando de página se precisar) pra pessoa ver qual é.
        public void MostrarCaixinha(FenceWindow w)
        {
            if (w.Data.Page != Cfg.Page) IrParaPagina(w.Data.Page);
            DefinirPeek(true);
        }

        public void UsarEstilo(Estilo estilo)
        {
            int i = 0;
            foreach (var f in Cfg.Fences) estilo.Aplicar(f, i++);
            Cfg.Estilo = estilo.Nome;
            AplicarEmTodas();
            SaveSoon();
            DefinirPeek(true);
        }

        public string AtalhoPeek => atalhoPeek;

        // Caixinhas feitas antes das regras existirem: ganham a regra do nome delas.
        void Migrar()
        {
            foreach (var f in Cfg.Fences)
                if (f.Folder == null && f.Regra == null)
                    f.Regra = CATEGORIAS.FirstOrDefault(c => c.titulo == f.Title).chave ?? "";
        }

        public static void Anotar(string texto)
        {
            try
            {
                Directory.CreateDirectory(Store.Dir);
                File.AppendAllText(Path.Combine(Store.Dir, "erro.log"), DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss") + "  " + texto + Environment.NewLine);
            }
            catch { }
        }

        public void SaveSoon()
        {
            if (semSalvar || saveTimer == null) return;
            saveTimer.Stop();
            saveTimer.Start();
        }

        public FenceWindow Find(string id) => wins.FirstOrDefault(w => w.Data.Id == id);
        public IEnumerable<FenceWindow> Janelas => wins;

        // ---- caixinhas ----

        bool Visivel(FenceData f) => !escondidas && f.Page == Cfg.Page;

        FenceWindow Abrir(FenceData f)
        {
            var w = new FenceWindow(f);
            wins.Add(w);
            if (Visivel(f)) w.Show();
            return w;
        }

        void AtualizarVisibilidade()
        {
            foreach (var w in wins)
            {
                if (Visivel(w.Data)) w.Aparecer();
                else w.Sumir();
            }
        }

        public FenceWindow Adicionar(FenceData f)
        {
            Cfg.Fences.Add(f);
            var w = Abrir(f);
            SaveSoon();
            return w;
        }

        // nome vazio = nasce pedindo o nome ali mesmo, no título
        public void Nova(string pasta, string nome = null)
        {
            if (escondidas) { escondidas = false; AtualizarVisibilidade(); }
            var area = SystemParameters.WorkArea;
            int n = Cfg.Fences.Count;
            var f = new FenceData
            {
                Title = nome ?? (pasta == null ? "Nova caixinha" : "📂 " + Shell.DisplayName(pasta)),
                Folder = pasta,
                Regra = "",
                Color = pasta == null ? "roxo" : "azul",
                Left = area.Left + 60 + (n % 8) * 28,
                Top = area.Top + 60 + (n % 8) * 28,
                Page = Cfg.Page,
            };
            Estilos.Achar(Cfg.Estilo).Aplicar(f, n);
            Cfg.Fences.Add(f);
            var w = Abrir(f);
            SaveSoon();
            if (central != null && central.IsActive) DefinirPeek(true);
            else if (pasta == null && nome == null) w.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(w.ComecarRenomear));
        }

        public void Apagar(FenceWindow w)
        {
            Anotar("apagou a caixinha \"" + w.Data.Title + "\" (" + w.Abas.Sum(t => t.Items.Count) + " itens)");
            Cfg.Fences.Remove(w.Data);
            wins.Remove(w);
            w.Close();
            SaveSoon();
        }

        public void TirarDeTodas(string path)
        {
            foreach (var w in wins)
            {
                int tirou = 0;
                foreach (var t in w.Abas)
                    if (t.Folder == null) tirou += t.Items.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
                if (tirou > 0) w.ReloadLogo();
            }
        }

        public void AplicarEmTodas()
        {
            foreach (var w in wins) w.Aplicar();
        }

        // ---- regras automáticas ----

        static bool Casa(TabData t, string path, bool pasta)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            switch (t.Regra)
            {
                case "pastas": return pasta;
                case "programas": case "documentos": case "imagens":
                    return !pasta && CATEGORIAS.First(c => c.chave == t.Regra).ext.Contains(ext);
                case "ext":
                    if (pasta || string.IsNullOrWhiteSpace(t.RegraTexto)) return false;
                    return t.RegraTexto.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => (x.StartsWith(".") ? x : "." + x).ToLowerInvariant()).Contains(ext);
                case "nome":
                    return !string.IsNullOrWhiteSpace(t.RegraTexto)
                        && Path.GetFileName(path).IndexOf(t.RegraTexto.Trim(), StringComparison.CurrentCultureIgnoreCase) >= 0;
                case "outros": return true;
                default: return false;
            }
        }

        // A aba que deve receber o arquivo: regras específicas primeiro, "o que sobrar" por último.
        (FenceWindow janela, TabData aba)? AcharRegra(string path, bool pasta)
        {
            var todas = wins.SelectMany(w => w.Abas.Where(t => t.Folder == null && !string.IsNullOrEmpty(t.Regra)).Select(t => (w, t))).ToList();
            foreach (var par in todas.Where(p => p.t.Regra == "nome").Concat(todas.Where(p => p.t.Regra != "nome" && p.t.Regra != "outros")))
                if (Casa(par.t, path, pasta)) return par;
            foreach (var par in todas.Where(p => p.t.Regra == "outros")) return par;
            return null;
        }

        static string CategoriaDe(string path, bool pasta)
        {
            if (pasta) return "pastas";
            var ext = Path.GetExtension(path).ToLowerInvariant();
            foreach (var c in CATEGORIAS)
                if (c.ext != null && c.ext.Length > 0 && c.ext.Contains(ext)) return c.chave;
            return "outros";
        }

        HashSet<string> Usados() => new HashSet<string>(
            wins.SelectMany(w => w.Abas).Where(t => t.Folder == null).SelectMany(t => t.Items), StringComparer.OrdinalIgnoreCase);

        static bool Ignorar(string path, out bool pasta)
        {
            pasta = false;
            FileAttributes at;
            try { at = File.GetAttributes(path); } catch { return true; }
            pasta = (at & FileAttributes.Directory) != 0;
            if ((at & (FileAttributes.Hidden | FileAttributes.System)) != 0) return true;
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".tmp" || ext == ".crdownload" || ext == ".part" || Path.GetFileName(path).StartsWith("~$");
        }

        // Arquivo novo na área de trabalho: entra sozinho na caixinha que tem regra pra ele.
        void Chegou(string path)
        {
            if (Ignorar(path, out bool pasta) || Usados().Contains(path)) return;
            var alvo = AcharRegra(path, pasta);
            // com os ícones do Windows escondidos, o que não tem caixinha ficaria invisível: vai pra "Outros"
            if (alvo == null && Shell.IconesEscondidos()) alvo = Outros();
            if (alvo == null) return;
            alvo.Value.aba.Items.Add(path);
            alvo.Value.janela.ReloadLogo();
            SaveSoon();
        }

        // A caixinha que recebe "tudo o que sobrar" (cria se não existir).
        (FenceWindow janela, TabData aba) Outros()
        {
            var area = SystemParameters.WorkArea;
            var f = new FenceData
            {
                Title = "📦 Outros", Color = "escuro", Regra = "outros", Page = Cfg.Page,
                Left = area.Left + 16, Top = area.Bottom - 16 - 160, Width = 372, Height = 160,
            };
            Estilos.Achar(Cfg.Estilo).Aplicar(f, Cfg.Fences.Count);
            Anotar("criei a caixinha Outros (havia " + Cfg.Fences.Count + " caixinhas)");
            return (Adicionar(f), f);
        }

        // Põe numa caixinha tudo o que está solto na área de trabalho (pela regra, ou em "Outros").
        // É o que deixa esconder os ícones do Windows sem nada sumir.
        public int GuardarSoltos()
        {
            var usados = Usados();
            int n = 0;
            foreach (var dir in PastasDaAreaDeTrabalho)
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var p in Directory.EnumerateFileSystemEntries(dir))
                {
                    if (usados.Contains(p) || Ignorar(p, out bool pasta)) continue;
                    var alvo = AcharRegra(p, pasta) ?? Outros();
                    alvo.aba.Items.Add(p);
                    alvo.janela.ReloadLogo();
                    n++;
                }
            }
            if (n > 0) SaveSoon();
            return n;
        }

        // Liga ou desliga os ícones do Windows; ao esconder, guarda antes o que estiver solto.
        public void EsconderIconesDoWindows(bool esconder)
        {
            if (Shell.IconesEscondidos() == esconder) return;
            if (esconder) GuardarSoltos();
            Shell.AlternarIcones();
        }

        void VigiarAreaDeTrabalho()
        {
            foreach (var dir in PastasDaAreaDeTrabalho)
            {
                if (!Directory.Exists(dir)) continue;
                try
                {
                    var v = new FileSystemWatcher(dir) { EnableRaisingEvents = true };
                    v.Created += (s, e) => Dispatcher.BeginInvoke(new Action(() => Chegou(e.FullPath)));
                    v.Deleted += (s, e) => Dispatcher.BeginInvoke(new Action(() => { foreach (var w in wins) w.ReloadLogo(); }));
                    v.Renamed += (s, e) => Dispatcher.BeginInvoke(new Action(() =>
                    {
                        bool trocou = false;
                        foreach (var t in wins.SelectMany(w => w.Abas).Where(t => t.Folder == null))
                        {
                            int i = t.Items.FindIndex(p => string.Equals(p, e.OldFullPath, StringComparison.OrdinalIgnoreCase));
                            if (i >= 0) { t.Items[i] = e.FullPath; trocou = true; }
                        }
                        if (!trocou) Chegou(e.FullPath);
                        foreach (var w in wins) w.ReloadLogo();
                        SaveSoon();
                    }));
                    vigias.Add(v);
                }
                catch { }
            }
        }

        // Guarda nas caixinhas tudo o que está na área de trabalho e ainda não tem lugar,
        // criando as caixinhas por tipo que faltarem.
        public void Organizar()
        {
            if (escondidas) { escondidas = false; AtualizarVisibilidade(); }
            var usados = Usados();
            var novos = CATEGORIAS.ToDictionary(c => c.chave, c => new List<string>());
            int guardados = 0;

            foreach (var dir in PastasDaAreaDeTrabalho)
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var p in Directory.EnumerateFileSystemEntries(dir))
                {
                    if (usados.Contains(p) || Ignorar(p, out bool pasta)) continue;
                    guardados++;
                    var alvo = AcharRegra(p, pasta);
                    if (alvo != null && (alvo.Value.aba.Regra != "outros" || CategoriaDe(p, pasta) == "outros"))
                    {
                        alvo.Value.aba.Items.Add(p);
                        alvo.Value.janela.ReloadLogo();
                    }
                    else novos[CategoriaDe(p, pasta)].Add(p);
                }
            }

            var area = SystemParameters.WorkArea;
            double x = area.Right - 372 - 16, y = area.Top + 16;
            foreach (var c in CATEGORIAS)
            {
                var lista = novos[c.chave];
                if (lista.Count == 0) continue;
                lista.Sort((a, b) => string.Compare(Shell.DisplayName(a), Shell.DisplayName(b), StringComparison.CurrentCultureIgnoreCase));
                int linhas = Math.Min(4, (int)Math.Ceiling(lista.Count / 4.0));
                double alt = 32 + linhas * 96 + 14;
                if (y + alt > area.Bottom - 8 && y > area.Top + 16) { y = area.Top + 16; x -= 372 + 12; }
                var nova = new FenceData
                {
                    Title = c.titulo, Color = c.cor, Items = lista, Regra = c.chave, Page = Cfg.Page,
                    Left = Math.Max(area.Left + 8, x), Top = y, Width = 372, Height = alt,
                };
                Estilos.Achar(Cfg.Estilo).Aplicar(nova, Cfg.Fences.Count);
                Adicionar(nova);
                y += alt + 12;
            }
            SaveSoon();
            if (central != null && central.IsActive && guardados > 0) DefinirPeek(true);
            if (guardados == 0)
                tray?.ShowBalloonTip(3000, "Cercadinhos", "Tudo o que está na área de trabalho já tem caixinha.", WF.ToolTipIcon.Info);
        }

        // ---- esconder, Peek, trancar, páginas, layouts ----

        // Dois cliques no fundo da área de trabalho: some tudo / volta tudo.
        public void AlternarEscondidas()
        {
            escondidas = !escondidas;
            AtualizarVisibilidade();
        }

        public void DefinirPeek(bool ligar)
        {
            if (ligar && escondidas) { escondidas = false; AtualizarVisibilidade(); }
            Peek = ligar;
            foreach (var w in wins.Where(w => w.IsVisible)) w.Empilhar();
            peekTimer.Stop();
            if (!ligar) return;
            var primeira = wins.FirstOrDefault(w => w.IsVisible);
            if (primeira != null) Shell.SetForegroundWindow(primeira.Hwnd);
            peekFrente = Shell.GetForegroundWindow();
            peekViuNossa = false;
            peekTimer.Start();
        }

        public void FimDoPeek()
        {
            if (Peek) DefinirPeek(false);
        }

        // O Peek acaba quando outra janela (que não é nossa) vem pra frente.
        void ConferirPeek()
        {
            var frente = Shell.GetForegroundWindow();
            Shell.GetWindowThreadProcessId(frente, out uint pid);
            if (pid == (uint)Environment.ProcessId) { peekViuNossa = true; return; }
            if (peekViuNossa || frente != peekFrente) DefinirPeek(false);
        }

        void RegistrarAtalhos()
        {
            const uint ALT = 1, CTRL = 2, WIN = 8, NAO_REPETIR = 0x4000;
            if (Shell.RegisterHotKey(IntPtr.Zero, 1, WIN | NAO_REPETIR, 0x20)) atalhoPeek = "Win+Espaço";
            else if (Shell.RegisterHotKey(IntPtr.Zero, 1, CTRL | ALT | NAO_REPETIR, 0x20)) atalhoPeek = "Ctrl+Alt+Espaço";
            Shell.RegisterHotKey(IntPtr.Zero, 2, CTRL | ALT | NAO_REPETIR, 0x21);   // PageUp
            Shell.RegisterHotKey(IntPtr.Zero, 3, CTRL | ALT | NAO_REPETIR, 0x22);   // PageDown
            ComponentDispatcher.ThreadFilterMessage += (ref MSG msg, ref bool handled) =>
            {
                if (msg.message != 0x0312) return;
                int id = msg.wParam.ToInt32();
                if (id == 1) DefinirPeek(!Peek);
                else if (id == 2) IrParaPagina(Cfg.Page - 1);
                else if (id == 3) IrParaPagina(Cfg.Page + 1);
                handled = true;
            };
        }

        public int TotalPaginas => Math.Max(Math.Max(1, Cfg.Pages), Cfg.Fences.Count == 0 ? 1 : Cfg.Fences.Max(f => f.Page) + 1);

        public void NovaPagina()
        {
            Cfg.Pages = TotalPaginas + 1;
            IrParaPagina(Cfg.Pages - 1);
        }

        public void IrParaPagina(int pagina)
        {
            int total = TotalPaginas;
            pagina = ((pagina % total) + total) % total;
            if (pagina == Cfg.Page && !escondidas) return;
            Cfg.Page = pagina;
            escondidas = false;
            AtualizarVisibilidade();
            SaveSoon();
            if (total > 1) tray?.ShowBalloonTip(1200, "Cercadinhos", "Página " + (pagina + 1) + " de " + total, WF.ToolTipIcon.None);
        }

        public void MoverParaPagina(FenceWindow w, int pagina)
        {
            w.Data.Page = pagina;
            Cfg.Pages = Math.Max(Cfg.Pages, pagina + 1);
            AtualizarVisibilidade();
            SaveSoon();
        }

        public void SalvarLayout()
        {
            Store.SaveTo(Cfg, Path.Combine(Store.Layouts, DateTime.Now.ToString("yyyy-MM-dd HH.mm.ss") + ".json"));
            tray?.ShowBalloonTip(2000, "Cercadinhos", "Layout salvo. Ele fica em Layouts, no menu.", WF.ToolTipIcon.Info);
        }

        public void RestaurarLayout(string arquivo)
        {
            var r = MessageBox.Show("Voltar pro layout de " + Path.GetFileNameWithoutExtension(arquivo) + "?\n\n" +
                "As caixinhas de agora são trocadas pelas de lá. Os arquivos não mudam.",
                "Cercadinhos", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;
            var novo = Store.LoadFrom(arquivo);
            if (novo == null) { MessageBox.Show("Não consegui ler esse layout.", "Cercadinhos"); return; }
            foreach (var w in wins.ToList()) w.Close();
            wins.Clear();
            novo.Welcomed = true;
            Cfg = novo;
            Migrar();
            escondidas = false;
            foreach (var f in Cfg.Fences) Abrir(f);
            Store.Save(Cfg);
        }

        // ---- bandeja ----

        void CriarBandeja()
        {
            var menu = new WF.ContextMenuStrip();
            var abrir = new WF.ToolStripMenuItem("Abrir o Cercadinhos");
            abrir.Font = new System.Drawing.Font(abrir.Font, System.Drawing.FontStyle.Bold);
            abrir.Click += (s, a) => MostrarCentral();
            menu.Items.Add(abrir);
            menu.Items.Add(new WF.ToolStripSeparator());
            menu.Items.Add("Nova caixinha", null, (s, a) => Nova(null));
            menu.Items.Add("Nova caixinha de pasta…", null, (s, a) =>
            {
                using var dlg = new WF.FolderBrowserDialog { Description = "Escolha a pasta que a caixinha vai mostrar", UseDescriptionForTitle = true };
                if (dlg.ShowDialog() == WF.DialogResult.OK) Nova(dlg.SelectedPath);
            });
            menu.Items.Add("Organizar a área de trabalho", null, (s, a) => Organizar());
            menu.Items.Add(new WF.ToolStripSeparator());

            var peek = new WF.ToolStripMenuItem("Trazer pra frente das janelas" + (atalhoPeek == "" ? "" : "  (" + atalhoPeek + ")"));
            peek.Click += (s, a) => DefinirPeek(!Peek);
            menu.Items.Add(peek);
            var esconder = new WF.ToolStripMenuItem("Esconder as caixinhas  (2 cliques na área de trabalho)");
            esconder.Click += (s, a) => AlternarEscondidas();
            menu.Items.Add(esconder);
            var trancar = new WF.ToolStripMenuItem("Trancar no lugar");
            trancar.Click += (s, a) => { Cfg.Locked = !Cfg.Locked; AplicarEmTodas(); SaveSoon(); };
            menu.Items.Add(trancar);
            var paginas = new WF.ToolStripMenuItem("Páginas  (Ctrl+Alt+PgUp / PgDn)");
            menu.Items.Add(paginas);
            var layouts = new WF.ToolStripMenuItem("Layouts");
            menu.Items.Add(layouts);
            menu.Items.Add(new WF.ToolStripSeparator());

            var icones = new WF.ToolStripMenuItem("Esconder os ícones do Windows");
            icones.Click += (s, a) => EsconderIconesDoWindows(!Shell.IconesEscondidos());
            menu.Items.Add(icones);
            var iniciar = new WF.ToolStripMenuItem("Abrir junto com o Windows");
            iniciar.Click += (s, a) => Shell.IniciarComWindows(!Shell.IniciaComWindows());
            menu.Items.Add(iniciar);
            menu.Items.Add(new WF.ToolStripSeparator());
            menu.Items.Add("Fechar o Cercadinhos", null, (s, a) => Sair());

            menu.Opening += (s, a) =>
            {
                icones.Checked = Shell.IconesEscondidos();
                iniciar.Checked = Shell.IniciaComWindows();
                peek.Checked = Peek;
                esconder.Checked = escondidas;
                trancar.Checked = Cfg.Locked;

                paginas.DropDownItems.Clear();
                int total = TotalPaginas;
                for (int i = 0; i < total; i++)
                {
                    int pg = i;
                    int quantas = Cfg.Fences.Count(f => f.Page == pg);
                    var item = new WF.ToolStripMenuItem("Página " + (i + 1) + "  (" + quantas + ")") { Checked = i == Cfg.Page };
                    item.Click += (s2, a2) => IrParaPagina(pg);
                    paginas.DropDownItems.Add(item);
                }
                paginas.DropDownItems.Add(new WF.ToolStripSeparator());
                paginas.DropDownItems.Add("Página nova", null, (s2, a2) => NovaPagina());

                layouts.DropDownItems.Clear();
                layouts.DropDownItems.Add("Salvar o layout de agora", null, (s2, a2) => SalvarLayout());
                var salvos = Directory.Exists(Store.Layouts)
                    ? Directory.GetFiles(Store.Layouts, "*.json").OrderByDescending(f => f).Take(12).ToList() : new List<string>();
                if (salvos.Count > 0) layouts.DropDownItems.Add(new WF.ToolStripSeparator());
                foreach (var arq in salvos)
                {
                    var caminho = arq;
                    layouts.DropDownItems.Add("Voltar pro de " + Path.GetFileNameWithoutExtension(arq), null, (s2, a2) => RestaurarLayout(caminho));
                }
            };

            tray = new WF.NotifyIcon
            {
                Icon = IconeBandeja(), Text = "Cercadinhos", ContextMenuStrip = menu, Visible = true,
            };
            tray.MouseClick += (s, a) => { if (a.Button == WF.MouseButtons.Left) MostrarCentral(); };
        }

        // O mesmo ícone do aplicativo (vem de dentro do .exe).
        static System.Drawing.Icon IconeBandeja()
        {
            try { return System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath); }
            catch { return System.Drawing.SystemIcons.Application; }
        }

        public void Sair()
        {
            saveTimer?.Stop();
            Store.Save(Cfg);
            Shell.PararDeVigiar();
            for (int i = 1; i <= 3; i++) Shell.UnregisterHotKey(IntPtr.Zero, i);
            foreach (var v in vigias) v.Dispose();
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            Shutdown();
        }

        // ---- teste sem mexer em nada: desenha uma caixinha com a área de trabalho e salva em PNG ----

        void Foto(string arquivo)
        {
            semSalvar = true;
            Cfg = new Config();
            var desk = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var todos = Directory.EnumerateFileSystemEntries(desk).Where(p => !p.EndsWith(".ini")).ToList();
            var f = new FenceData
            {
                Title = "🚀 Programas", Left = 40, Top = 40, Width = 372, Height = 240, Items = todos.Take(8).ToList(),
            };
            bool estilo = Environment.GetCommandLineArgs().Contains("--estilo");
            if (estilo)
            {
                f.Color = "#FF6FB5"; f.Opacity = 0; f.TitleOpacity = 35; f.Font = "Comic Sans MS"; f.FontSize = 14;
                f.IconSize = 64; f.Radius = 4; f.Width = 460; f.Height = 330;
                f.TintStrength = 60; f.Tint = "#FF6FB5"; f.Sort = "nome";
                f.Tabs.Add(new TabData { Title = "📁 Outra aba", Items = todos.Skip(8).Take(4).ToList(), Regra = "ext", RegraTexto = ".pdf, .docx" });
                f.Tabs.Add(new TabData { Title = "📂 Downloads", Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads") });
            }
            Cfg.Fences.Add(f);
            var w = Abrir(f);
            if (estilo) w.Editar();
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            t.Tick += (s, a) =>
            {
                t.Stop();
                if (estilo) Fotografar((FrameworkElement)Windows.OfType<EditarWindow>().First().Content, arquivo + ".editor.png", Brushes.White);
                Fotografar((FrameworkElement)w.Content, arquivo, estilo ? Brushes.SteelBlue : null);
                Shutdown();
            };
            t.Start();
        }

        // Desenha um menu de botão direito num arquivo, sem abrir nada na tela (confere o visual dos menus).
        void FotoDoMenu(string arquivo)
        {
            var menu = new System.Windows.Controls.ContextMenu();
            menu.Items.Add(new System.Windows.Controls.MenuItem { Header = "🎨 Editar aparência e regra…" });
            menu.Items.Add(new System.Windows.Controls.MenuItem { Header = "Renomear" });
            var cor = new System.Windows.Controls.MenuItem { Header = "Cor" };
            cor.Items.Add(new System.Windows.Controls.MenuItem { Header = "Roxo" });
            menu.Items.Add(cor);
            menu.Items.Add(new System.Windows.Controls.MenuItem { Header = "Ordenar por nome", IsChecked = true });
            menu.Items.Add(new System.Windows.Controls.Separator());
            menu.Items.Add(new System.Windows.Controls.MenuItem { Header = "Nova caixinha" });
            menu.Items.Add(new System.Windows.Controls.MenuItem { Header = "Apagar esta caixinha", IsEnabled = false });
            menu.Measure(new Size(600, 600));
            menu.Arrange(new Rect(menu.DesiredSize));
            menu.UpdateLayout();
            Fotografar(menu, arquivo, (Brush)FindResource("Fundo"));
            Shutdown();
        }

        // Fotografa a tela de verdade com três caixinhas (uma por estilo) por cima de tudo:
        // é o único jeito de conferir o vidro fosco.
        void FotoDaTela(string arquivo)
        {
            semSalvar = true;
            Cfg = new Config();
            Peek = true;
            var desk = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var todos = Directory.EnumerateFileSystemEntries(desk).Where(p => !p.EndsWith(".ini")).Take(8).ToList();
            var nomes = new[] { "Vidro", "Rosa e roxo", "Vidro claro" };
            FenceWindow primeira = null;
            for (int i = 0; i < nomes.Length; i++)
            {
                var f = new FenceData { Title = nomes[i], Left = 60 + i * 390, Top = 60, Width = 372, Height = 240, Items = todos };
                Estilos.Achar(nomes[i]).Aplicar(f, i);
                Cfg.Fences.Add(f);
                var w = Abrir(f);
                if (primeira == null) primeira = w;
            }
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            t.Tick += (s, a) =>
            {
                t.Stop();
                var dpi = VisualTreeHelper.GetDpi(primeira);
                int x = (int)(40 * dpi.DpiScaleX), y = (int)(40 * dpi.DpiScaleY);
                int larg = (int)(1210 * dpi.DpiScaleX), alt = (int)(560 * dpi.DpiScaleY);
                using (var bmp = new System.Drawing.Bitmap(larg, alt))
                {
                    using (var g = System.Drawing.Graphics.FromImage(bmp)) g.CopyFromScreen(x, y, 0, 0, bmp.Size);
                    bmp.Save(arquivo, System.Drawing.Imaging.ImageFormat.Png);
                }
                Shutdown();
            };
            t.Start();
        }

        // Fotografa uma página da janela do aplicativo, com as caixinhas de verdade (sem mostrar nem salvar nada).
        void FotoDaCentral(string arquivo, string pagina)
        {
            semSalvar = true;
            escondidas = true;
            Cfg = Store.Load(out _) ?? new Config();
            foreach (var f in Cfg.Fences) Abrir(f);
            var c = new CentralWindow { WindowStartupLocation = WindowStartupLocation.Manual, Left = -4000, Top = 0, ShowInTaskbar = false };
            c.Show();
            c.Mostrar(pagina);
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            t.Tick += (s, a) =>
            {
                t.Stop();
                Fotografar((FrameworkElement)c.Content, arquivo, (Brush)FindResource("Fundo"));
                Shutdown();
            };
            t.Start();
        }

        static void Fotografar(FrameworkElement alvo, string arquivo, Brush fundo)
        {
            var dpi = VisualTreeHelper.GetDpi(alvo);
            var rtb = new RenderTargetBitmap((int)(alvo.ActualWidth * dpi.DpiScaleX), (int)(alvo.ActualHeight * dpi.DpiScaleY),
                96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
            if (fundo != null)
            {
                // fundo de mentira, pra ver a transparência na foto
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen()) dc.DrawRectangle(fundo, null, new Rect(0, 0, alvo.ActualWidth, alvo.ActualHeight));
                rtb.Render(dv);
            }
            rtb.Render(alvo);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using (var fs = File.Create(arquivo)) enc.Save(fs);
        }
    }
}
