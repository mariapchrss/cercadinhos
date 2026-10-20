using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Cercadinhos
{
    // O conteúdo de uma aba. Com Folder preenchido ela é um "portal": mostra o que tem na pasta.
    public class TabData
    {
        public string Title { get; set; } = "Caixinha";
        public string Folder { get; set; }
        public List<string> Items { get; set; } = new List<string>();
        // regra automática: "", pastas, programas, documentos, imagens, ext, nome, outros
        public string Regra { get; set; }
        public string RegraTexto { get; set; }
        // manual, nome, tipo, data
        public string Sort { get; set; }

        public void CopiarConteudo(TabData o)
        {
            Title = o.Title; Folder = o.Folder; Items = new List<string>(o.Items);
            Regra = o.Regra; RegraTexto = o.RegraTexto; Sort = o.Sort;
        }
    }

    // Uma caixinha: a primeira aba é ela mesma, as outras ficam em Tabs.
    public class FenceData : TabData
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public double Left { get; set; } = 80;
        public double Top { get; set; } = 80;
        public double Width { get; set; } = 372;
        public double Height { get; set; } = 240;
        public bool Collapsed { get; set; }
        public string Color { get; set; } = "roxo";
        public List<TabData> Tabs { get; set; } = new List<TabData>();
        public int ActiveTab { get; set; }
        public int Page { get; set; }

        // aparência (o "Editar aparência" da caixinha)
        public int Opacity { get; set; } = 60;        // fundo, 0 = transparente
        public int TitleOpacity { get; set; } = 80;
        public int Radius { get; set; } = 12;
        public bool Border { get; set; } = true;
        public string Font { get; set; } = "Segoe UI";
        public double FontSize { get; set; } = 12;
        public bool Bold { get; set; }
        public string TextColor { get; set; } = "#FFFFFF";
        public bool Shadow { get; set; } = true;
        public int IconSize { get; set; } = 48;
        public bool ShowNames { get; set; } = true;
        public int Fade { get; set; } = 100;          // camaleão: quanto os ícones aparecem sem o mouse
        public string Tint { get; set; } = "#9B6BFF";
        public int TintStrength { get; set; }         // 0 = ícones com a cor de verdade
        public bool Blur { get; set; }                // vidro fosco: desfoca o que está atrás
        public bool Claro { get; set; }               // fundo claro em vez de escuro

        public void CopiarAparencia(FenceData o, bool comCor)
        {
            if (comCor) Color = o.Color;
            Blur = o.Blur; Claro = o.Claro;
            Opacity = o.Opacity; TitleOpacity = o.TitleOpacity; Radius = o.Radius; Border = o.Border;
            Font = o.Font; FontSize = o.FontSize; Bold = o.Bold; TextColor = o.TextColor; Shadow = o.Shadow;
            IconSize = o.IconSize; ShowNames = o.ShowNames;
            Fade = o.Fade; Tint = o.Tint; TintStrength = o.TintStrength;
        }
    }

    public class Config
    {
        public List<FenceData> Fences { get; set; } = new List<FenceData>();
        public bool Welcomed { get; set; }
        public int Page { get; set; }
        public int Pages { get; set; } = 1;
        public bool Locked { get; set; }
        public bool DoisCliques { get; set; } = true;     // 2 cliques na área de trabalho escondem tudo
        public bool Ima { get; set; } = true;             // encostar ao arrastar
        public bool AbrirAoPassar { get; set; } = true;   // enrolada abre com o mouse em cima
        public string Estilo { get; set; }                // estilo escolhido no aplicativo (vazio = nenhum; novas nascem em Vidro)
    }

    // Estilos prontos (aba "Estilos" do aplicativo).
    public class Estilo
    {
        public string Nome, Descricao;
        public Action<FenceData, int> Aplicar;   // caixinha, posição dela na lista
    }

    public static class Estilos
    {
        public static readonly Estilo[] Lista =
        {
            new Estilo { Nome = "Vidro", Descricao = "Fosco e discreto, deixa o papel de parede aparecer.", Aplicar = (d, i) =>
            {
                d.Color = "#6B6B7B"; d.Claro = false; d.Blur = true; d.Opacity = 38; d.TitleOpacity = 22;
                d.Radius = 8; d.Border = true; d.TextColor = "#FFFFFF"; d.Shadow = true;
            }},
            new Estilo { Nome = "Vidro claro", Descricao = "Branquinho e leve, com letras escuras.", Aplicar = (d, i) =>
            {
                d.Color = "#FFFFFF"; d.Claro = true; d.Blur = true; d.Opacity = 55; d.TitleOpacity = 35;
                d.Radius = 8; d.Border = true; d.TextColor = "#1F1F1F"; d.Shadow = false;
            }},
            new Estilo { Nome = "Azul-petróleo", Descricao = "Vidro azulado, com o título mais forte.", Aplicar = (d, i) =>
            {
                d.Color = "#2F6F8F"; d.Claro = false; d.Blur = true; d.Opacity = 62; d.TitleOpacity = 55;
                d.Radius = 4; d.Border = false; d.TextColor = "#FFFFFF"; d.Shadow = true;
            }},
            new Estilo { Nome = "Grafite", Descricao = "Escuro e fechado, sem transparência.", Aplicar = (d, i) =>
            {
                d.Color = "#5A5A62"; d.Claro = false; d.Blur = false; d.Opacity = 94; d.TitleOpacity = 45;
                d.Radius = 8; d.Border = true; d.TextColor = "#FFFFFF"; d.Shadow = false;
            }},
            new Estilo { Nome = "Rosa e roxo", Descricao = "Vidro colorido, uma caixinha roxa e outra rosa.", Aplicar = (d, i) =>
            {
                d.Color = i % 2 == 0 ? "roxo" : "rosa"; d.Claro = false; d.Blur = true; d.Opacity = 45; d.TitleOpacity = 70;
                d.Radius = 8; d.Border = true; d.TextColor = "#FFFFFF"; d.Shadow = true;
            }},
            new Estilo { Nome = "Só os ícones", Descricao = "Caixinha invisível: aparecem só o nome e os ícones.", Aplicar = (d, i) =>
            {
                d.Claro = false; d.Blur = false; d.Opacity = 0; d.TitleOpacity = 0;
                d.Border = false; d.TextColor = "#FFFFFF"; d.Shadow = true;
            }},
        };

        public static Estilo Achar(string nome)
        {
            foreach (var e in Lista) if (e.Nome == nome) return e;
            return Lista[0];
        }
    }

    public static class Store
    {
        public static readonly string Dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Cercadinhos");
        public static readonly string File = Path.Combine(Dir, "config.json");
        public static readonly string Layouts = Path.Combine(Dir, "layouts");

        public static readonly string Backups = Path.Combine(Dir, "backups");

        // Sem arquivo = primeira vez (caixinhas vazias). Com arquivo que não dá pra ler, devolve null e
        // diz o motivo: quem chama NÃO pode seguir com caixinhas vazias, senão salva por cima da arrumação.
        // Na hora do login o arquivo às vezes está preso por outro programa, então tenta de novo por uns segundos.
        public static Config Load(out string problema)
        {
            problema = null;
            if (!System.IO.File.Exists(File)) return new Config();
            for (int i = 0; i < 12; i++)
            {
                try
                {
                    var c = JsonSerializer.Deserialize<Config>(System.IO.File.ReadAllText(File));
                    if (c != null) return c;
                    problema = "arquivo vazio";
                    break;
                }
                catch (IOException e) { problema = e.Message; System.Threading.Thread.Sleep(500); }
                catch (Exception e) { problema = e.Message; break; }
            }
            try { System.IO.File.Copy(File, Path.Combine(Dir, "config.ilegivel-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json"), true); } catch { }
            return null;
        }

        // A cópia de segurança mais nova que dá pra ler (uma por dia, em backups\).
        public static Config UltimoBackup(out string qual)
        {
            qual = null;
            try
            {
                if (!Directory.Exists(Backups)) return null;
                var arquivos = new List<string>(Directory.GetFiles(Backups, "config-*.json"));
                arquivos.Sort();
                for (int i = arquivos.Count - 1; i >= 0; i--)
                {
                    var c = LoadFrom(arquivos[i]);
                    if (c != null && c.Fences.Count > 0) { qual = Path.GetFileName(arquivos[i]); return c; }
                }
            }
            catch { }
            return null;
        }

        // Antes da primeira gravação do dia, guarda como estava (mantém as últimas 14).
        static void CopiaDoDia()
        {
            try
            {
                if (!System.IO.File.Exists(File)) return;
                Directory.CreateDirectory(Backups);
                var hoje = Path.Combine(Backups, "config-" + DateTime.Now.ToString("yyyy-MM-dd") + ".json");
                if (System.IO.File.Exists(hoje)) return;
                System.IO.File.Copy(File, hoje);
                var velhos = new List<string>(Directory.GetFiles(Backups, "config-*.json"));
                velhos.Sort();
                for (int i = 0; i < velhos.Count - 14; i++) System.IO.File.Delete(velhos[i]);
            }
            catch { }
        }

        public static Config LoadFrom(string arquivo)
        {
            try
            {
                if (System.IO.File.Exists(arquivo))
                    return JsonSerializer.Deserialize<Config>(System.IO.File.ReadAllText(arquivo));
            }
            catch { }
            return null;
        }

        public static void Save(Config cfg) => SaveTo(cfg, File);

        public static void SaveTo(Config cfg, string arquivo)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(arquivo));
                if (arquivo == File) CopiaDoDia();
                var tmp = arquivo + ".tmp";
                System.IO.File.WriteAllText(tmp, JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true }));
                System.IO.File.Move(tmp, arquivo, true);
            }
            catch { }
        }
    }
}
