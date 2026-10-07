using System;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Cercadinhos
{
    // Conversa com o Windows: ícones dos arquivos, janela da área de trabalho e afins.
    public static class Shell
    {
        [StructLayout(LayoutKind.Sequential)]
        struct SIZE { public int cx, cy; }

        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItemImageFactory
        {
            [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFOHEADER
        {
            public int biSize, biWidth, biHeight;
            public short biPlanes, biBitCount;
            public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct BITMAP
        {
            public int bmType, bmWidth, bmHeight, bmWidthBytes;
            public short bmPlanes, bmBitsPixel;
            public IntPtr bmBits;
        }

        [DllImport("gdi32.dll")] static extern int GetObject(IntPtr h, int c, out BITMAP bm);
        [DllImport("gdi32.dll")] static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER bi, uint usage);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

        static readonly string[] COM_MINIATURA = { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".mp4", ".mkv", ".mov", ".webm", ".avi", ".pdf" };

        // Ícone (ou miniatura) do arquivo, com transparência.
        public static ImageSource Icon(string path, int size)
        {
            IntPtr hbm = IntPtr.Zero;
            try
            {
                var iid = typeof(IShellItemImageFactory).GUID;
                if (SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var factory) != 0 || factory == null) return null;
                // Miniatura só pra foto e vídeo; o resto pede o ícone puro (senão ícone pequeno vem com moldura).
                var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
                int flags = Array.IndexOf(COM_MINIATURA, ext) >= 0 ? 0 : 0x4;
                int hr = factory.GetImage(new SIZE { cx = size, cy = size }, flags, out hbm);
                Marshal.ReleaseComObject(factory);
                if (hr != 0 || hbm == IntPtr.Zero) return null;

                GetObject(hbm, Marshal.SizeOf<BITMAP>(), out var bm);
                int w = bm.bmWidth, h = Math.Abs(bm.bmHeight);
                if (w <= 0 || h <= 0) return null;
                var bi = new BITMAPINFOHEADER
                {
                    biSize = Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = w, biHeight = -h,
                    biPlanes = 1, biBitCount = 32, biCompression = 0
                };
                var bits = new byte[w * h * 4];
                var dc = GetDC(IntPtr.Zero);
                GetDIBits(dc, hbm, 0, (uint)h, bits, ref bi, 0);
                ReleaseDC(IntPtr.Zero, dc);

                // Miniatura de foto às vezes vem sem canal alfa (tudo 0): aí é imagem opaca.
                bool temAlfa = false;
                for (int i = 3; i < bits.Length; i += 4) if (bits[i] != 0) { temAlfa = true; break; }
                var src = BitmapSource.Create(w, h, 96, 96, temAlfa ? PixelFormats.Pbgra32 : PixelFormats.Bgr32, null, bits, w * 4);
                src.Freeze();
                if (!temAlfa) return src;

                // Ícone que só existe pequeno vem no meio de um quadro vazio (às vezes com uma moldura
                // fininha na beirada): olha só o miolo e corta a sobra pra ele poder crescer.
                int b = Math.Max(3, w / 20);
                int x0 = w, y0 = h, x1 = -1, y1 = -1;
                for (int y = b; y < h - b; y++)
                    for (int x = b; x < w - b; x++)
                        if (bits[(y * w + x) * 4 + 3] > 8)
                        {
                            if (x < x0) x0 = x; if (x > x1) x1 = x;
                            if (y < y0) y0 = y; if (y > y1) y1 = y;
                        }
                if (x1 < 0) return src;
                int lado = Math.Max(x1 - x0 + 1, y1 - y0 + 1);
                if (lado >= Math.Min(w, h) * 0.8) return src;
                int cx = Math.Max(0, Math.Min(w - lado, (x0 + x1 + 1 - lado) / 2));
                int cy = Math.Max(0, Math.Min(h - lado, (y0 + y1 + 1 - lado) / 2));
                var corte = new CroppedBitmap(src, new System.Windows.Int32Rect(cx, cy, Math.Min(lado, w - cx), Math.Min(lado, h - cy)));
                corte.Freeze();
                return corte;
            }
            catch { return null; }
            finally { if (hbm != IntPtr.Zero) DeleteObject(hbm); }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHFILEINFO
        {
            public IntPtr hIcon; public int iIcon; public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SHGetFileInfo(string path, uint attrs, ref SHFILEINFO info, uint size, uint flags);

        // Nome do jeito que o Windows mostra (atalho sem ".lnk").
        public static string DisplayName(string path)
        {
            try
            {
                var info = new SHFILEINFO();
                if (SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), 0x200) != IntPtr.Zero
                    && !string.IsNullOrEmpty(info.szDisplayName))
                    return info.szDisplayName;
            }
            catch { }
            return System.IO.Path.GetFileName(path.TrimEnd('\\'));
        }

        // ---- janelas ----

        [StructLayout(LayoutKind.Sequential)]
        public struct WINDOWPOS
        {
            public IntPtr hwnd, hwndInsertAfter;
            public int x, y, cx, cy;
            public uint flags;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string name);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);

        // Deixa a caixinha "colada" na área de trabalho: sem botão na barra, fora do Alt+Tab,
        // e dona = área de trabalho, pra não sumir no Win+D.
        public static void GrudarNaAreaDeTrabalho(IntPtr hwnd)
        {
            const int GWL_EXSTYLE = -20, GWLP_HWNDPARENT = -8;
            const long WS_EX_TOOLWINDOW = 0x80;
            var ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex | WS_EX_TOOLWINDOW));
            var progman = FindWindow("Progman", null);
            if (progman != IntPtr.Zero) SetWindowLongPtr(hwnd, GWLP_HWNDPARENT, progman);
        }

        static IntPtr DefView()
        {
            var progman = FindWindow("Progman", null);
            var view = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (view != IntPtr.Zero) return view;
            IntPtr worker = IntPtr.Zero;
            while ((worker = FindWindowEx(IntPtr.Zero, worker, "WorkerW", null)) != IntPtr.Zero)
            {
                view = FindWindowEx(worker, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (view != IntPtr.Zero) return view;
            }
            return IntPtr.Zero;
        }

        public static bool IconesEscondidos()
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
                return Convert.ToInt32(k?.GetValue("HideIcons", 0) ?? 0) == 1;
            }
            catch { return false; }
        }

        // O mesmo que botão direito na área de trabalho > Exibir > Mostrar ícones da área de trabalho.
        public static void AlternarIcones()
        {
            var view = DefView();
            if (view != IntPtr.Zero) SendMessage(view, 0x0111, new IntPtr(0x7402), IntPtr.Zero);
        }

        // ---- vidro fosco ----

        [StructLayout(LayoutKind.Sequential)]
        struct ACCENT { public int State, Flags, Color, Anim; }
        [StructLayout(LayoutKind.Sequential)]
        struct WCA { public int Attr; public IntPtr Data; public int Size; }
        [DllImport("user32.dll")] static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WCA data);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        // Raio que o Windows 11 usa quando arredonda a janela: grande (8), pequeno (4) ou nenhum.
        public static int RaioDoVidro(int raio) => raio >= 7 ? 8 : raio >= 2 ? 4 : 0;

        // Desfoca o que está atrás da janela e pede pro Windows arredondar os cantos junto.
        public static void Vidro(IntPtr hwnd, bool ligar, int raio)
        {
            try
            {
                var a = new ACCENT { State = ligar ? 3 : 0 };
                var p = Marshal.AllocHGlobal(Marshal.SizeOf<ACCENT>());
                Marshal.StructureToPtr(a, p, false);
                var d = new WCA { Attr = 19, Data = p, Size = Marshal.SizeOf<ACCENT>() };
                SetWindowCompositionAttribute(hwnd, ref d);
                Marshal.FreeHGlobal(p);

                int canto = !ligar ? 1 : RaioDoVidro(raio) == 8 ? 2 : RaioDoVidro(raio) == 4 ? 3 : 1;
                DwmSetWindowAttribute(hwnd, 33, ref canto, 4);
                int semBorda = unchecked((int)0xFFFFFFFE);
                DwmSetWindowAttribute(hwnd, 34, ref semBorda, 4);
            }
            catch { }
        }

        // Pinta a barra de título de uma janela normal (Windows 11).
        public static void CorDaBarra(IntPtr hwnd, byte r, byte g, byte b)
        {
            try { int cor = r | (g << 8) | (b << 16); DwmSetWindowAttribute(hwnd, 35, ref cor, 4); } catch { }
        }

        // ---- ordem das janelas, encostar, atalhos e dois cliques na área de trabalho ----

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int left, top, right, bottom; }
        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int x, y; }
        [StructLayout(LayoutKind.Sequential)]
        struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData, flags, time; public IntPtr extra; }

        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT p);
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder sb, int max);
        [DllImport("user32.dll")] static extern uint GetDoubleClickTime();
        delegate IntPtr HookProc(int code, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr mod, uint thread);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);

        // Em cima de todas (Peek) ou lá no fundo, colada na área de trabalho.
        public static void Empilhar(IntPtr hwnd, bool porCima)
        {
            SetWindowPos(hwnd, new IntPtr(porCima ? -1 : 1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
        }

        static string Classe(IntPtr hwnd)
        {
            var sb = new System.Text.StringBuilder(64);
            GetClassName(hwnd, sb, 64);
            return sb.ToString();
        }

        // O ponto está no fundo da área de trabalho, sem ícone embaixo?
        static bool NoFundo(int x, int y)
        {
            var h = WindowFromPoint(new POINT { x = x, y = y });
            if (h == IntPtr.Zero) return false;
            var raiz = Classe(GetAncestor(h, 2));
            if (raiz != "Progman" && raiz != "WorkerW") return false;
            // depois do 1º clique, se foi num ícone ele fica selecionado; no vazio, nada fica
            if (Classe(h) == "SysListView32") return SendMessage(h, 0x1032, IntPtr.Zero, IntPtr.Zero) == IntPtr.Zero;
            return true;
        }

        static IntPtr hook;
        static HookProc hookProc;
        static uint ultimoT;
        static int ultimoX, ultimoY;

        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);

        public static System.Windows.Point Cursor()
        {
            GetCursorPos(out var p);
            return new System.Windows.Point(p.x, p.y);
        }

        // Janela que não recebe clique nem foco (o "fantasma" do ícone sendo arrastado).
        public static void Fantasma(IntPtr hwnd)
        {
            const int GWL_EXSTYLE = -20;
            var ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex | 0x20 | 0x80 | 0x08000000)); // TRANSPARENT | TOOLWINDOW | NOACTIVATE
        }

        // Chama a ação quando alguém dá dois cliques no fundo da área de trabalho.
        // O gancho de mouse roda numa linha só dele: na linha das janelas, todo movimento do mouse
        // do computador esperava o programa desenhar, e arrastar caixinha ficava travado.
        public static void VigiarDoisCliques(Action acao)
        {
            var linha = new System.Threading.Thread(() =>
            {
                Enganchar(acao);
                System.Windows.Forms.Application.Run();
            }) { IsBackground = true, Name = "gancho do mouse" };
            linha.SetApartmentState(System.Threading.ApartmentState.STA);
            linha.Start();
        }

        static void Enganchar(Action acao)
        {
            hookProc = (code, w, l) =>
            {
                if (code >= 0 && w.ToInt64() == 0x0201)
                {
                    var m = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(l);
                    if (m.time - ultimoT <= GetDoubleClickTime() && Math.Abs(m.pt.x - ultimoX) < 5 && Math.Abs(m.pt.y - ultimoY) < 5)
                    {
                        ultimoT = 0;
                        int x = m.pt.x, y = m.pt.y;
                        System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() => { if (NoFundo(x, y)) acao(); }));
                    }
                    else { ultimoT = m.time; ultimoX = m.pt.x; ultimoY = m.pt.y; }
                }
                return CallNextHookEx(hook, code, w, l);
            };
            hook = SetWindowsHookEx(14, hookProc, GetModuleHandle(null), 0);
        }

        public static void PararDeVigiar()
        {
            if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook);
            hook = IntPtr.Zero;
        }

        // Ímã: puxa o retângulo que está sendo arrastado pras bordas da tela e das outras caixinhas.
        public static void Encostar(ref RECT r, System.Collections.Generic.List<RECT> outras)
        {
            const int PERTO = 7, VAO = 8;   // ímã fraquinho: com 12 a caixinha "grudava" e parecia dura
            int w = r.right - r.left, h = r.bottom - r.top, esq = r.left, topo = r.top;
            int dx = int.MaxValue, dy = int.MaxValue;
            void X(int alvoEsq) { int d = alvoEsq - esq; if (Math.Abs(d) < PERTO && Math.Abs(d) < Math.Abs((long)dx)) dx = d; }
            void Y(int alvoTopo) { int d = alvoTopo - topo; if (Math.Abs(d) < PERTO && Math.Abs(d) < Math.Abs((long)dy)) dy = d; }

            foreach (var s in System.Windows.Forms.Screen.AllScreens)
            {
                var a = s.WorkingArea;
                X(a.Left + VAO); X(a.Right - VAO - w);
                Y(a.Top + VAO); Y(a.Bottom - VAO - h);
            }
            foreach (var o in outras)
            {
                bool pertoV = r.top < o.bottom + 40 && r.bottom > o.top - 40;
                bool pertoH = r.left < o.right + 40 && r.right > o.left - 40;
                if (pertoV) { X(o.left); X(o.right + VAO); X(o.right - w); X(o.left - VAO - w); }
                if (pertoH) { Y(o.top); Y(o.bottom + VAO); Y(o.bottom - h); Y(o.top - VAO - h); }
            }
            if (dx != int.MaxValue) { r.left += dx; r.right += dx; }
            if (dy != int.MaxValue) { r.top += dy; r.bottom += dy; }
        }

        const string RunKey =@"Software\Microsoft\Windows\CurrentVersion\Run";

        public static bool IniciaComWindows()
        {
            try { using var k = Registry.CurrentUser.OpenSubKey(RunKey); return k?.GetValue("Cercadinhos") != null; }
            catch { return false; }
        }

        public static void IniciarComWindows(bool ligar)
        {
            try
            {
                using var k = Registry.CurrentUser.CreateSubKey(RunKey);
                if (ligar) k.SetValue("Cercadinhos", "\"" + Environment.ProcessPath + "\" --quieto");
                else k.DeleteValue("Cercadinhos", false);
                Aprovar(ligar);
            }
            catch { }
        }

        // O Windows guarda, pra cada programa de inicialização, se ele está "aprovado" (é o que o Gerenciador
        // de Tarefas mostra). Em 07/10/2026 a entrada do Cercadinhos, a única sem esse registro, foi pulada no login.
        const string AprovadosKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

        static void Aprovar(bool ligar)
        {
            try
            {
                using var k = Registry.CurrentUser.CreateSubKey(AprovadosKey);
                if (ligar) k.SetValue("Cercadinhos", new byte[] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary);
                else k.DeleteValue("Cercadinhos", false);
            }
            catch { }
        }

        // Se o programa mudou de pasta (instalou), o "abrir com o Windows" passa a apontar pro lugar novo.
        public static void ConsertarInicio()
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(RunKey);
                var atual = k?.GetValue("Cercadinhos") as string;
                if (atual == null) return;
                if (atual != "\"" + Environment.ProcessPath + "\" --quieto") { IniciarComWindows(true); return; }
                // entrada certa, mas sem a aprovação (instalações antigas): grava. Se a pessoa desligou
                // pelo Gerenciador de Tarefas, o registro existe e fica como ela deixou.
                using var ap = Registry.CurrentUser.OpenSubKey(AprovadosKey);
                if (ap?.GetValue("Cercadinhos") == null) Aprovar(true);
            }
            catch { }
        }
    }
}
