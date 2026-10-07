# Cercadinhos

Caixinhas para organizar a área de trabalho do Windows, no estilo do Fences.

![ícone](assets/Cercadinhos.png)

## O que faz

- **Caixinhas** translúcidas na área de trabalho, com título, que ficam atrás das janelas. Arraste pelo título, puxe as bordas pra mudar o tamanho, dois cliques no nome pra renomear.
- **Vidro fosco**: desfoca o papel de parede atrás da caixinha.
- **Abas**, **páginas** (várias telas de caixinhas) e **layouts** salvos.
- **Regras automáticas**: arquivo novo na área de trabalho entra sozinho na caixinha certa (por tipo, extensão ou nome).
- **Caixinha de pasta**: mostra uma pasta ao vivo (Downloads, por exemplo) e deixa navegar nas subpastas.
- **Peek** (Win+Espaço): traz as caixinhas pra frente das janelas.
- **Esconder tudo** com dois cliques num lugar vazio da área de trabalho.
- **Enrolar**: a caixinha vira só o título e abre quando o mouse passa.
- **Personalização**: cor, transparência, cantos, fonte, tamanho dos ícones, tingir ícones, camaleão (ícones apagadinhos até o mouse chegar) e estilos prontos.
- **Ímã** ao arrastar, **trancar no lugar** e **abrir junto com o Windows**.

As caixinhas só apontam pros arquivos: nada sai do lugar. A exceção é a caixinha de pasta, que move de verdade o que é solto nela.

## Instalar

Precisa do Windows 10 ou 11 e do [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

Dê dois cliques em `Instalar Cercadinhos.bat`. Ele compila, copia pra `%LocalAppData%\Programs\Cercadinhos`, cria o atalho no Menu Iniciar e registra em "Aplicativos instalados" (com desinstalar). Rodar de novo atualiza.

As caixinhas ficam guardadas em `%AppData%\Cercadinhos\config.json`. Desinstalar não apaga esse arquivo.

## Por dentro

Programa em C# com WPF (.NET 8), um `.exe` só.

| Arquivo | O que tem |
|---|---|
| `App.xaml.cs` | Bandeja, regras automáticas, Peek, páginas, layouts, atalhos |
| `FenceWindow.xaml(.cs)` | A caixinha: desenho, abas, arrastar, redimensionar, menus |
| `CentralWindow.xaml(.cs)` | A janela do aplicativo |
| `Editar.cs` | Os controles de personalização |
| `Shell.cs` | Conversa com o Windows: ícones, vidro, gancho de mouse, ímã |
| `Dados.cs` | O que é salvo e os estilos prontos |
| `ferramentas/` | Instalador, desinstalador e gerador do ícone |

Testes por foto, sem mexer em nada: `Cercadinhos.exe --foto arquivo.png`, `--central arquivo.png pagina` e `--menu arquivo.png`.
