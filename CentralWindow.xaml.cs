using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Cercadinhos
{
    // A janela do aplicativo: caixinhas, estilos, comportamento, páginas e layouts.
    public partial class CentralWindow : Window
    {
        // chave, nome e o desenho do menu (fonte de ícones do Windows)
        static readonly (string chave, string nome, string icone)[] PAGINAS =
        {
            ("caixinhas", "Caixinhas", ""), ("personalizacao", "Personalização", ""), ("estilos", "Estilos prontos", ""),
            ("comportamento", "Comportamento", ""), ("paginas", "Páginas e layouts", ""), ("sobre", "Sobre", ""),
        };

        string atual = "caixinhas";
        FenceWindow escolhida;      // a caixinha sendo personalizada
        App A => App.I;

        public CentralWindow()
        {
            InitializeComponent();
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            Versao.Text = "versão " + v.Major + "." + v.Minor + "." + v.Build;
            foreach (var (chave, nome, icone) in PAGINAS)
            {
                var c = chave;
                var conteudo = new StackPanel { Orientation = Orientation.Horizontal };
                conteudo.Children.Add(new TextBlock
                {
                    Text = icone, FontFamily = (FontFamily)FindResource("Icones"), FontSize = 15, Width = 28,
                    VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("TextoSuave"),
                });
                conteudo.Children.Add(new TextBlock { Text = nome, VerticalAlignment = VerticalAlignment.Center });
                var item = new RadioButton { Content = conteudo, Style = (Style)FindResource("Nav"), GroupName = "nav", IsChecked = chave == atual };
                item.Checked += (s, e) => Mostrar(c);
                Menu.Children.Add(item);
            }
            SourceInitialized += (s, e) => Shell.CorDaBarra(new WindowInteropHelper(this).Handle, 0xF3, 0xF3, 0xF3);
            // clicar aqui devolve as caixinhas pro fundo (elas vêm pra frente no "Mostrar");
            // na Personalização elas ficam, pra dar pra ver o que está mudando
            PreviewMouseDown += (s, e) => { if (atual != "personalizacao") A.FimDoPeek(); };
            Mostrar(atual);
        }

        public void Personalizar(FenceWindow w)
        {
            escolhida = w;
            Mostrar("personalizacao");
        }

        public void Atualizar() => Mostrar(atual);

        public void Mostrar(string pagina)
        {
            atual = pagina;
            int n = Array.FindIndex(PAGINAS, p => p.chave == pagina);
            if (n >= 0 && n < Menu.Children.Count && Menu.Children[n] is RadioButton rb && rb.IsChecked != true) { rb.IsChecked = true; return; }
            Pagina.Children.Clear();
            switch (pagina)
            {
                case "personalizacao": PaginaPersonalizacao(); break;
                case "estilos": PaginaEstilos(); break;
                case "comportamento": PaginaComportamento(); break;
                case "paginas": PaginaPaginas(); break;
                case "sobre": PaginaSobre(); break;
                default: PaginaCaixinhas(); break;
            }
        }

        // ---- pecinhas ----

        void Cabecalho(string titulo, string sub)
        {
            Pagina.Children.Add(new TextBlock { Text = titulo, Style = (Style)FindResource("Titulo") });
            Pagina.Children.Add(new TextBlock { Text = sub, Style = (Style)FindResource("Sub"), Margin = new Thickness(0, 4, 0, 18) });
        }

        void Secao(string texto) => Pagina.Children.Add(new TextBlock
        {
            Text = texto, FontWeight = FontWeights.SemiBold, FontSize = 15, Margin = new Thickness(2, 12, 0, 8),
        });

        Button Botao(string texto, string estilo, Action acao)
        {
            var b = new Button { Content = texto, Style = (Style)FindResource(estilo), Margin = new Thickness(0, 0, 8, 0) };
            b.Click += (s, e) => acao();
            return b;
        }

        Border Cartao(UIElement dentro)
        {
            var c = new Border { Style = (Style)FindResource("Cartao"), Child = dentro };
            Pagina.Children.Add(c);
            return c;
        }

        // Cartão com texto à esquerda e alguma coisa (botões, interruptor) à direita.
        Border Linha(UIElement antes, string titulo, string sub, UIElement direita)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            if (antes != null) g.Children.Add(antes);
            var textos = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            textos.Children.Add(new TextBlock { Text = titulo, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            if (!string.IsNullOrEmpty(sub))
                textos.Children.Add(new TextBlock { Text = sub, Style = (Style)FindResource("Sub"), FontSize = 12, Margin = new Thickness(0, 2, 0, 0) });
            Grid.SetColumn(textos, 1);
            g.Children.Add(textos);
            if (direita != null)
            {
                if (direita is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(direita, 2);
                g.Children.Add(direita);
            }
            return Cartao(g);
        }

        void Interruptor(string titulo, string sub, bool valor, Action<bool> mudar)
        {
            var c = new CheckBox { Style = (Style)FindResource("Interruptor"), IsChecked = valor };
            c.Click += (s, e) => mudar(c.IsChecked == true);
            Linha(null, titulo, sub, c);
        }

        static StackPanel Fila(params UIElement[] itens)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var i in itens) p.Children.Add(i);
            return p;
        }

        // ---- Caixinhas ----

        static string NomeDaRegra(TabData t)
        {
            switch (t.Regra)
            {
                case "pastas": return "recebe pastas";
                case "programas": return "recebe programas e atalhos";
                case "documentos": return "recebe documentos";
                case "imagens": return "recebe imagens e vídeos";
                case "ext": return "recebe " + t.RegraTexto;
                case "nome": return "recebe nomes com \"" + t.RegraTexto + "\"";
                case "outros": return "recebe o que sobrar";
                default: return null;
            }
        }

        void PaginaCaixinhas()
        {
            Cabecalho("Caixinhas", "Crie quantas quiser. Na área de trabalho, arraste pelo título, puxe as bordas pra mudar o tamanho e use o ⋯ pra mais opções.");
            var acoes = Fila(
                Botao("Nova caixinha", "Primario", () =>
                {
                    var nome = Pergunta.Texto(this, "Nova caixinha", "Como ela vai se chamar?", "Nova caixinha");
                    if (nome != null) { A.Nova(null, nome); Mostrar(atual); }
                }),
                Botao("Caixinha de uma pasta", "Suave", () =>
                {
                    using var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = "Escolha a pasta que a caixinha vai mostrar", UseDescriptionForTitle = true };
                    if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK) { A.Nova(dlg.SelectedPath); Mostrar(atual); }
                }),
                Botao("Organizar a área de trabalho", "Suave", () => { A.Organizar(); Mostrar(atual); }));
            acoes.Margin = new Thickness(0, 0, 0, 18);
            Pagina.Children.Add(acoes);

            var janelas = A.Janelas.ToList();
            if (janelas.Count == 0)
            {
                Linha(null, "Nenhuma caixinha ainda", "Clique em \"Organizar a área de trabalho\" pra eu montar as primeiras com o que já está lá.", null);
                return;
            }
            foreach (var w in janelas)
            {
                var d = w.Data;
                var janela = w;
                var bolinha = new Ellipse
                {
                    Width = 14, Height = 14, Margin = new Thickness(2, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center,
                    Fill = new SolidColorBrush(FenceWindow.CorDe(d.Color, Colors.MediumPurple)), Stroke = (Brush)FindResource("Linha"),
                };
                string titulo = string.Join("  ·  ", w.Abas.Select(t => t.Title));
                int itens = w.Abas.Sum(t => t.Folder == null ? t.Items.Count : 0);
                var partes = new System.Collections.Generic.List<string>();
                if (w.Abas.Any(t => t.Folder != null)) partes.Add("mostra uma pasta");
                if (itens > 0 || !w.Abas.Any(t => t.Folder != null)) partes.Add(itens + (itens == 1 ? " item" : " itens"));
                if (A.TotalPaginas > 1) partes.Add("página " + (d.Page + 1));
                var regra = NomeDaRegra(w.Aba);
                if (regra != null) partes.Add(regra);

                Linha(bolinha, titulo, string.Join("  ·  ", partes), Fila(
                    Botao("Mostrar", "Suave", () => A.MostrarCaixinha(janela)),
                    Botao("Personalizar", "Suave", () => Personalizar(janela)),
                    Botao("Renomear", "Suave", () =>
                    {
                        var nome = Pergunta.Texto(this, "Renomear", "Novo nome da caixinha:", janela.Aba.Title);
                        if (nome != null) { janela.Aba.Title = nome; janela.Aplicar(); A.SaveSoon(); Mostrar(atual); }
                    }),
                    Botao("Apagar", "Suave", () =>
                    {
                        var r = MessageBox.Show(this, "Apagar a caixinha \"" + d.Title + "\"?\n\nOs arquivos continuam onde estão.",
                            "Cercadinhos", MessageBoxButton.YesNo, MessageBoxImage.Question);
                        if (r == MessageBoxResult.Yes) { A.Apagar(janela); Mostrar(atual); }
                    })));
            }
        }

        // ---- Personalização ----

        void PaginaPersonalizacao()
        {
            Cabecalho("Personalização", "Cor, transparência, texto e ícones de cada caixinha. Tudo muda na hora.");
            var janelas = A.Janelas.ToList();
            if (janelas.Count == 0)
            {
                Linha(null, "Nenhuma caixinha ainda", "Crie uma em Caixinhas e volte aqui.", null);
                return;
            }
            if (escolhida == null || !janelas.Contains(escolhida)) escolhida = janelas[0];

            var combo = new ComboBox { Width = 230, Padding = new Thickness(8, 5, 8, 5) };
            foreach (var w in janelas) combo.Items.Add(string.Join(" · ", w.Abas.Select(t => t.Title)));
            combo.SelectedIndex = janelas.IndexOf(escolhida);
            combo.SelectionChanged += (s, e) =>
            {
                if (combo.SelectedIndex < 0) return;
                escolhida = janelas[combo.SelectedIndex];
                Mostrar(atual);
            };
            var ver = Botao(App.Peek ? "Devolver as caixinhas pro fundo" : "Ver as caixinhas por cima das janelas", "Suave",
                () => { A.DefinirPeek(!App.Peek); Mostrar(atual); });
            ver.Margin = new Thickness(10, 0, 0, 0);
            var topo = Fila(new TextBlock { Text = "Caixinha:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 10, 0) }, combo, ver);
            topo.Margin = new Thickness(0, 0, 0, 12);
            Pagina.Children.Add(topo);

            var painel = new StackPanel();
            new PainelDeEdicao(painel, escolhida).Montar(null);
            var cartao = Cartao(painel);
            cartao.MaxWidth = 620;
            cartao.HorizontalAlignment = HorizontalAlignment.Left;
            cartao.Padding = new Thickness(20, 16, 20, 16);
        }

        // ---- Estilos ----

        UIElement Miniatura(Estilo estilo)
        {
            var d = new FenceData();
            estilo.Aplicar(d, 0);
            var (corpo, titulo, texto) = FenceWindow.Pinceis(d);
            var papel = new LinearGradientBrush(new GradientStopCollection
            {
                new GradientStop(Color.FromRgb(120, 146, 176), 0), new GradientStop(Color.FromRgb(186, 172, 190), 0.55),
                new GradientStop(Color.FromRgb(228, 196, 172), 1),
            }, new Point(0, 0), new Point(1, 1));

            var icones = new WrapPanel { Margin = new Thickness(8, 7, 0, 0) };
            var corIcone = new SolidColorBrush(d.Claro ? Color.FromArgb(120, 60, 60, 70) : Color.FromArgb(200, 255, 255, 255));
            for (int i = 0; i < 8; i++)
                icones.Children.Add(new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(5), Background = corIcone, Margin = new Thickness(0, 0, 9, 8) });
            var caixa = new DockPanel();
            var barra = new Border
            {
                Height = 18, Background = titulo, CornerRadius = new CornerRadius(7, 7, 0, 0),
                Child = new TextBlock { Text = "Programas", FontSize = 9.5, FontWeight = FontWeights.SemiBold, Foreground = texto, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            DockPanel.SetDock(barra, Dock.Top);
            caixa.Children.Add(barra);
            caixa.Children.Add(icones);
            return new Border
            {
                Height = 118, CornerRadius = new CornerRadius(6), Background = papel,
                Child = new Border
                {
                    Margin = new Thickness(26, 16, 26, 16), CornerRadius = new CornerRadius(8), Background = corpo, Child = caixa,
                    BorderThickness = new Thickness(d.Border ? 1 : 0), BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)),
                },
            };
        }

        void PaginaEstilos()
        {
            Cabecalho("Estilos prontos", "Um clique muda o visual de todas as caixinhas e vira o padrão das novas. Depois, cada uma ainda pode ser ajustada sozinha em Personalização.");
            var grade = new WrapPanel();
            foreach (var estilo in Estilos.Lista)
            {
                var e = estilo;
                bool emUso = A.Cfg.Estilo == e.Nome;
                var p = new StackPanel();
                p.Children.Add(Miniatura(e));
                p.Children.Add(new TextBlock { Text = e.Nome, FontWeight = FontWeights.SemiBold, FontSize = 15, Margin = new Thickness(2, 12, 0, 2) });
                p.Children.Add(new TextBlock { Text = e.Descricao, Style = (Style)FindResource("Sub"), FontSize = 12, Height = 34, Margin = new Thickness(2, 0, 0, 10) });
                var b = Botao(emUso ? "Em uso" : "Usar este estilo", emUso ? "Suave" : "Primario", () => { A.UsarEstilo(e); Mostrar(atual); });
                b.Margin = new Thickness(0);
                p.Children.Add(b);
                grade.Children.Add(new Border
                {
                    Style = (Style)FindResource("Cartao"), Width = 270, Margin = new Thickness(0, 0, 14, 14), Padding = new Thickness(14), Child = p,
                });
            }
            Pagina.Children.Add(grade);
        }

        // ---- Comportamento ----

        void PaginaComportamento()
        {
            Cabecalho("Comportamento", "Como as caixinhas reagem no dia a dia.");
            Interruptor("Abrir junto com o Windows", "O Cercadinhos liga sozinho, quietinho, quando o computador inicia.",
                Shell.IniciaComWindows(), v => Shell.IniciarComWindows(v));
            Interruptor("Mostrar os ícones só nas caixinhas", "Esconde os ícones soltos da área de trabalho (não apaga: as caixinhas apontam pra eles). O que estiver sem caixinha vai pra \"Outros\".",
                Shell.IconesEscondidos(), v => A.EsconderIconesDoWindows(v));
            Interruptor("Dois cliques escondem tudo", "Dois cliques num lugar vazio da área de trabalho somem com as caixinhas; mais dois trazem de volta.",
                A.Cfg.DoisCliques, v => { A.Cfg.DoisCliques = v; A.SaveSoon(); });
            Interruptor("Ímã ao arrastar", "A caixinha encosta nas outras e nas bordas da tela. Segurar Alt desliga na hora.",
                A.Cfg.Ima, v => { A.Cfg.Ima = v; A.SaveSoon(); });
            Interruptor("Caixinha enrolada abre com o mouse", "Enrolada (dois cliques na barra, ao lado do nome) mostra só o nome; passar o mouse abre.",
                A.Cfg.AbrirAoPassar, v => { A.Cfg.AbrirAoPassar = v; A.SaveSoon(); });
            Interruptor("Trancar no lugar", "Ninguém move nem muda o tamanho sem querer.",
                A.Cfg.Locked, v => { A.Cfg.Locked = v; A.AplicarEmTodas(); A.SaveSoon(); });

            Secao("Atalhos");
            var lista = new StackPanel();
            void Atalho(string tecla, string faz)
            {
                var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
                g.ColumnDefinitions.Add(new ColumnDefinition());
                g.Children.Add(new Border
                {
                    Background = (Brush)FindResource("Cinza"), CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 3, 8, 3), HorizontalAlignment = HorizontalAlignment.Left,
                    Child = new TextBlock { Text = tecla, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("Texto"), FontSize = 12 },
                });
                var t = new TextBlock { Text = faz, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
                Grid.SetColumn(t, 1);
                g.Children.Add(t);
                lista.Children.Add(g);
            }
            Atalho(A.AtalhoPeek == "" ? "(sem atalho livre)" : A.AtalhoPeek, "Traz as caixinhas pra frente das janelas");
            Atalho("Ctrl + Alt + PgUp / PgDn", "Troca de página");
            Atalho("2 cliques no nome", "Renomeia a caixinha (ou a aba)");
            Atalho("2 cliques ao lado do nome", "Enrola ou desenrola a caixinha");
            Atalho("Alt enquanto arrasta", "Desliga o ímã");
            Cartao(lista);
        }

        // ---- Páginas e layouts ----

        void PaginaPaginas()
        {
            Cabecalho("Páginas e layouts", "Páginas são várias \"telas\" de caixinhas, uma de cada vez. Layout é uma foto da arrumação pra você voltar quando quiser.");
            Secao("Páginas");
            int total = A.TotalPaginas;
            for (int i = 0; i < total; i++)
            {
                int pg = i;
                int quantas = A.Cfg.Fences.Count(f => f.Page == pg);
                bool aqui = pg == A.Cfg.Page;
                Linha(null, "Página " + (i + 1), quantas + (quantas == 1 ? " caixinha" : " caixinhas") + (aqui ? "  ·  é a que está aparecendo" : ""),
                    aqui ? null : Botao("Ir pra ela", "Suave", () => { A.IrParaPagina(pg); Mostrar(atual); }));
            }
            var nova = Botao("Página nova", "Suave", () => { A.NovaPagina(); Mostrar(atual); });
            nova.HorizontalAlignment = HorizontalAlignment.Left;
            Pagina.Children.Add(nova);

            Secao("Layouts salvos");
            var salvar = Botao("Salvar o layout de agora", "Primario", () => { A.SalvarLayout(); Mostrar(atual); });
            salvar.HorizontalAlignment = HorizontalAlignment.Left;
            salvar.Margin = new Thickness(0, 0, 0, 12);
            Pagina.Children.Add(salvar);
            var arquivos = Directory.Exists(Store.Layouts)
                ? Directory.GetFiles(Store.Layouts, "*.json").OrderByDescending(f => f).ToList() : new System.Collections.Generic.List<string>();
            if (arquivos.Count == 0) Linha(null, "Nenhum layout salvo", "Salve um antes de mexer bastante: aí dá pra voltar atrás.", null);
            foreach (var arq in arquivos)
            {
                var caminho = arq;
                var cfg = Store.LoadFrom(arq);
                Linha(null, System.IO.Path.GetFileNameWithoutExtension(arq).Replace('.', ':'),
                    cfg == null ? "não consegui ler" : cfg.Fences.Count + " caixinhas",
                    Fila(
                        Botao("Voltar pra este", "Suave", () => { A.RestaurarLayout(caminho); Mostrar(atual); }),
                        Botao("Apagar", "Suave", () =>
                        {
                            var r = MessageBox.Show(this, "Apagar este layout salvo?", "Cercadinhos", MessageBoxButton.YesNo, MessageBoxImage.Question);
                            if (r == MessageBoxResult.Yes) { try { File.Delete(caminho); } catch { } Mostrar(atual); }
                        })));
            }
        }

        // ---- Sobre ----

        void PaginaSobre()
        {
            Cabecalho("Sobre", "Caixinhas pra arrumar a área de trabalho, do seu jeito.");
            var texto = new StackPanel();
            foreach (var linha in new[]
            {
                "As caixinhas só apontam pros arquivos, que continuam na área de trabalho. Por isso nunca apague os ícones \"de fora\": eles são os de verdade. Pra sumirem, ligue \"Mostrar os ícones só nas caixinhas\" em Comportamento.",
                "A exceção é a caixinha de pasta, que move de verdade o que você solta nela.",
                "Fechar esta janela não desliga o programa: ele continua no ícone perto do relógio.",
                "Suas caixinhas ficam guardadas em " + Store.Dir + ".",
            })
                texto.Children.Add(new TextBlock { Text = "•  " + linha, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 3) });
            Cartao(texto);
            Pagina.Children.Add(Fila(
                Botao("Abrir a pasta dos dados", "Suave", () =>
                {
                    try { Directory.CreateDirectory(Store.Dir); Process.Start(new ProcessStartInfo(Store.Dir) { UseShellExecute = true }); } catch { }
                }),
                Botao("Desligar o Cercadinhos", "Suave", () =>
                {
                    var r = MessageBox.Show(this, "Desligar o Cercadinhos? As caixinhas somem até você abrir de novo.",
                        "Cercadinhos", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (r == MessageBoxResult.Yes) A.Sair();
                })));
        }
    }

    // Janelinha de uma pergunta só (nome da caixinha).
    public static class Pergunta
    {
        public static string Texto(Window dona, string titulo, string pergunta, string valor)
        {
            var w = new Window
            {
                Title = titulo, Width = 380, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = dona, ShowInTaskbar = false,
                Background = (Brush)Application.Current.FindResource("Fundo"), FontFamily = new FontFamily("Segoe UI"), FontSize = 13.5,
            };
            var p = new StackPanel { Margin = new Thickness(20, 16, 20, 18) };
            p.Children.Add(new TextBlock { Text = pergunta, Margin = new Thickness(0, 0, 0, 8), Foreground = (Brush)Application.Current.FindResource("Texto") });
            var caixa = new TextBox { Text = valor, Padding = new Thickness(6, 5, 6, 5) };
            p.Children.Add(caixa);
            var botoes = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            string resposta = null;
            var cancelar = new Button { Content = "Cancelar", Style = (Style)Application.Current.FindResource("Suave"), Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
            var ok = new Button { Content = "Pronto", Style = (Style)Application.Current.FindResource("Primario"), IsDefault = true };
            ok.Click += (s, e) => { if (caixa.Text.Trim().Length > 0) { resposta = caixa.Text.Trim(); w.Close(); } };
            botoes.Children.Add(cancelar);
            botoes.Children.Add(ok);
            p.Children.Add(botoes);
            w.Content = p;
            w.Loaded += (s, e) => { caixa.Focus(); caixa.SelectAll(); };
            w.ShowDialog();
            return resposta;
        }
    }
}
