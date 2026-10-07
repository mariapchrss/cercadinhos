using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WF = System.Windows.Forms;

namespace Cercadinhos
{
    // Os controles de aparência e regra de uma caixinha. Cada mexida já aparece nela.
    // Usado na janelinha do ⋯ e na página "Personalização" do aplicativo.
    public class PainelDeEdicao
    {
        readonly FenceWindow alvo;
        readonly StackPanel painel;
        Action fechar;
        FenceData D => alvo.Data;

        public PainelDeEdicao(StackPanel painel, FenceWindow alvo)
        {
            this.painel = painel;
            this.alvo = alvo;
        }

        void Mudou()
        {
            alvo.Aplicar();
            App.I.SaveSoon();
        }

        void Secao(string texto) => painel.Children.Add(new TextBlock
        {
            Text = texto, FontWeight = FontWeights.SemiBold, FontSize = 14, Foreground = (Brush)Application.Current.FindResource("Texto"),
            Margin = new Thickness(0, painel.Children.Count == 0 ? 0 : 16, 0, 6),
        });

        void Nota(string texto) => painel.Children.Add(new TextBlock
        {
            Text = texto, FontSize = 11, Foreground = (Brush)Application.Current.FindResource("TextoSuave"),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(118, 0, 0, 2),
        });

        void Linha(string rotulo, FrameworkElement controle)
        {
            var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(118) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.Children.Add(new TextBlock { Text = rotulo, VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(controle, 1);
            controle.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(controle);
            painel.Children.Add(g);
        }

        void Barra(string rotulo, double min, double max, double valor, string sufixo, Action<double> mudar)
        {
            var dock = new DockPanel();
            var num = new TextBlock { Width = 42, TextAlignment = TextAlignment.Right, Text = (int)valor + sufixo };
            DockPanel.SetDock(num, Dock.Right);
            var s = new Slider { Minimum = min, Maximum = max, Value = valor, IsSnapToTickEnabled = true, TickFrequency = 1 };
            s.ValueChanged += (o, e) => { num.Text = (int)s.Value + sufixo; mudar(s.Value); Mudou(); };
            dock.Children.Add(num);
            dock.Children.Add(s);
            Linha(rotulo, dock);
        }

        void Caixa(string texto, bool valor, Action<bool> mudar)
        {
            var c = new CheckBox { Content = texto, IsChecked = valor, Margin = new Thickness(0, 4, 0, 2) };
            c.Click += (o, e) => { mudar(c.IsChecked == true); Mudou(); };
            painel.Children.Add(c);
        }

        static string Hex(System.Drawing.Color c) => "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");

        static string EscolherCor(Color atual)
        {
            using var dlg = new WF.ColorDialog { FullOpen = true, Color = System.Drawing.Color.FromArgb(atual.R, atual.G, atual.B) };
            return dlg.ShowDialog() == WF.DialogResult.OK ? Hex(dlg.Color) : null;
        }

        Border Amostra(Color cor, string dica, Action clique)
        {
            var b = new Border
            {
                Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 6, 0),
                Background = new SolidColorBrush(cor), BorderBrush = new SolidColorBrush(Color.FromRgb(170, 170, 170)), BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand, ToolTip = dica,
            };
            b.MouseLeftButtonDown += (o, e) => clique();
            return b;
        }

        // Bolinhas de cor + "Outra…", pra cor da caixinha, do texto e da tinta.
        StackPanel Cores((Color cor, string nome, string valor)[] opcoes, Func<string> atual, Action<string> mudar)
        {
            var fila = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var o in opcoes)
            {
                var valor = o.valor;
                fila.Children.Add(Amostra(o.cor, o.nome, () => { mudar(valor); Mudou(); }));
            }
            var outra = new Button { Content = "Outra…", Style = (Style)Application.Current.FindResource("Suave"), Padding = new Thickness(9, 3, 9, 3) };
            outra.Click += (s, e) =>
            {
                var hex = EscolherCor(FenceWindow.CorDe(atual(), Colors.Gray));
                if (hex != null) { mudar(hex); Mudou(); }
            };
            fila.Children.Add(outra);
            return fila;
        }

        public void Montar(Action fechar)
        {
            this.fechar = fechar;
            painel.Children.Clear();
            var paleta = FenceWindow.CORES.Select(kv => (kv.Value.cor, kv.Value.nome, kv.Key)).ToArray();

            Secao("Fundo");
            Linha("Cor", Cores(paleta, () => D.Color, v => D.Color = v));
            Barra("Transparência", 0, 100, 100 - D.Opacity, "%", v => D.Opacity = 100 - (int)v);
            Barra("Título", 0, 100, 100 - D.TitleOpacity, "%", v => D.TitleOpacity = 100 - (int)v);
            Nota("100% = some de vez, ficam só o nome e os ícones.");
            Barra("Cantos", 0, 24, D.Radius, "", v => D.Radius = (int)v);
            Caixa("Vidro fosco (desfoca o que está atrás)", D.Blur, v => D.Blur = v);
            Caixa("Fundo claro (combine com texto escuro)", D.Claro, v => D.Claro = v);
            Caixa("Borda fininha em volta", D.Border, v => D.Border = v);

            Secao("Texto");
            var fontes = new ComboBox { MaxDropDownHeight = 320 };
            foreach (var nome in Fonts.SystemFontFamilies.Select(f => f.Source).Distinct().OrderBy(n => n))
                fontes.Items.Add(new ComboBoxItem { Content = nome, FontFamily = new FontFamily(nome), FontSize = 14 });
            fontes.SelectedItem = fontes.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Content == D.Font);
            fontes.SelectionChanged += (o, e) =>
            {
                if (fontes.SelectedItem is ComboBoxItem i) { D.Font = (string)i.Content; Mudou(); }
            };
            Linha("Fonte", fontes);
            Barra("Tamanho", 9, 22, D.FontSize, "", v => D.FontSize = v);
            Linha("Cor", Cores(new[]
            {
                (Colors.White, "Branco", "#FFFFFF"), (Color.FromRgb(31, 31, 31), "Preto", "#1F1F1F"), (Color.FromRgb(200, 200, 200), "Cinza", "#C8C8C8"),
            }, () => D.TextColor, v => D.TextColor = v));
            Caixa("Negrito", D.Bold, v => D.Bold = v);
            Caixa("Sombra no texto (bom com fundo transparente)", D.Shadow, v => D.Shadow = v);

            Secao("Ícones");
            Barra("Tamanho", 24, 96, D.IconSize, "", v => { D.IconSize = (int)v; alvo.ReloadLogo(); });
            Caixa("Mostrar os nomes", D.ShowNames, v => D.ShowNames = v);
            Barra("Camaleão", 10, 100, D.Fade, "%", v => D.Fade = (int)v);
            Nota("Abaixo de 100% os ícones ficam apagadinhos até o mouse chegar.");
            Barra("Tingir", 0, 100, D.TintStrength, "%", v => D.TintStrength = (int)v);
            Linha("Cor da tinta", Cores(paleta, () => D.Tint, v => D.Tint = v));

            var aba = alvo.Aba;
            if (aba.Folder == null)
            {
                Secao("Regra automática" + (D.Tabs.Count > 0 ? " da aba \"" + aba.Title + "\"" : ""));
                var regras = new (string chave, string nome)[]
                {
                    ("", "Nenhuma"), ("pastas", "Pastas"), ("programas", "Programas e atalhos"), ("documentos", "Documentos"),
                    ("imagens", "Imagens e vídeos"), ("ext", "Estas extensões:"), ("nome", "Nome contém:"), ("outros", "Tudo o que sobrar"),
                };
                var combo = new ComboBox();
                foreach (var r in regras) combo.Items.Add(r.nome);
                combo.SelectedIndex = Math.Max(0, Array.FindIndex(regras, r => r.chave == (aba.Regra ?? "")));
                var texto = new TextBox { Text = aba.RegraTexto ?? "", Padding = new Thickness(3, 2, 3, 2) };
                Action ajustar = () => texto.IsEnabled = aba.Regra == "ext" || aba.Regra == "nome";
                combo.SelectionChanged += (o, e) => { aba.Regra = regras[combo.SelectedIndex].chave; ajustar(); App.I.SaveSoon(); };
                texto.TextChanged += (o, e) => { aba.RegraTexto = texto.Text; App.I.SaveSoon(); };
                ajustar();
                Linha("Recebe sozinha", combo);
                Linha("", texto);
                Nota("Arquivo novo na área de trabalho que bater com a regra entra aqui sozinho. Extensões separadas por vírgula: .pdf, .docx");
            }

            var botoes = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
            Button Botao(string texto, string estilo, Action acao)
            {
                var b = new Button { Content = texto, Margin = new Thickness(6, 0, 0, 0), Style = (Style)Application.Current.FindResource(estilo) };
                b.Click += (o, e) => acao();
                botoes.Children.Add(b);
                return b;
            }
            Botao("Copiar pras outras caixinhas", "Suave", () =>
            {
                foreach (var w in App.I.Janelas.Where(w => w != alvo).ToList())
                {
                    w.Data.CopiarAparencia(D, false);
                    w.Aplicar();
                    w.Reload();
                }
                App.I.SaveSoon();
            }).ToolTip = "Copia tudo, menos a cor";
            Botao("Voltar ao padrão", "Suave", () =>
            {
                var padrao = new FenceData { Color = D.Color };
                Estilos.Lista[0].Aplicar(padrao, 0);
                D.CopiarAparencia(padrao, true);
                Mudou();
                alvo.Reload();
                Montar(this.fechar);
            });
            if (fechar != null) Botao("Fechar", "Primario", fechar);
            painel.Children.Add(botoes);
        }
    }

    // A janelinha do ⋯ > Editar aparência e regra.
    public class EditarWindow : Window
    {
        public EditarWindow(FenceWindow alvo)
        {
            Title = "Editar: " + alvo.Data.Title;
            Width = 450;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;
            Background = (Brush)Application.Current.FindResource("Fundo");
            Foreground = (Brush)Application.Current.FindResource("Texto");
            try { Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/assets/Cercadinhos.ico")); } catch { }
            SourceInitialized += (s, e) => Shell.CorDaBarra(new System.Windows.Interop.WindowInteropHelper(this).Handle, 0xF3, 0xF3, 0xF3);

            var area = SystemParameters.WorkArea;
            var painel = new StackPanel { Margin = new Thickness(18, 14, 18, 16) };
            Content = new ScrollViewer { Content = painel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            MaxHeight = area.Height - 20;
            WindowStartupLocation = WindowStartupLocation.Manual;
            double x = alvo.Left - Width - 10;
            if (x < area.Left) x = alvo.Left + alvo.ActualWidth + 10;
            Left = Math.Max(area.Left, Math.Min(x, area.Right - Width));
            Top = area.Top + 10;

            new PainelDeEdicao(painel, alvo).Montar(Close);
        }
    }
}
