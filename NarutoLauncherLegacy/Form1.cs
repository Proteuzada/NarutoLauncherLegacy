using CefSharp;
using CefSharp.WinForms;
using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows.Forms;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Text;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Management;
using System.IO.Pipes;
using DiscordRPC;
using DiscordRPC.Logging;

// RESOLVE A AMBIGUIDADE DE BOTÕES
using Button = System.Windows.Forms.Button;

namespace NarutoLauncherLegacy
{
    public partial class Form1 : Form
    {
        // APIS DO WINDOWS
        [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)] public static extern IntPtr GetModuleHandle(string lpModuleName);
        [DllImport("kernel32", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)] static extern IntPtr GetProcAddress(IntPtr hModule, string procName);
        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)] static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, uint nSize, out UIntPtr lpNumberOfBytesWritten);
        [DllImport("kernel32.dll")] static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr lpThreadAttributes, uint dwStackSize, IntPtr lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, IntPtr lpThreadId);
        [DllImport("kernel32.dll")] public static extern int SetProcessWorkingSetSize(IntPtr process, int minimumWorkingSetSize, int maximumWorkingSetSize);

        // COMPONENTES DA INTERFACE
        public ChromiumWebBrowser browser;
        public Panel pnlLogin, pnlCaixaLogin, pnlMenuLateral;
        public ComboBox cmbEmail;
        public TextBox txtSenha, txtServerLogin, txtServerMenu;
        public Button btnLogar, btnInjetar, btnSpeed, btnNovaTela, btnTrocarServer, btnToggleMenu, btnToggleDiscord;
        public CheckBox chkLembrar;
        public Label lblStatusLogin, lblStatusInjecao;
        public LinkLabel lnkGithub;

        // VARIÁVEIS DE ESTADO E SESSÃO
        private Dictionary<string, string> contasSalvas = new Dictionary<string, string>();
        private string arquivoContas = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "contas_salvas.dat");
        private string arquivoConfig = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.dat"); // Arquivo de memória do Discord
        private static readonly object lockArquivo = new object();
        private string sessionID = Guid.NewGuid().ToString();

        // O SISTEMA MULTI-CONTAS
        public static HashSet<int> pidsInjetadosGlobais = new HashSet<int>();
        private int meuFlashPid = 0;
        private bool dllInjetada = false;
        private bool speedAtivo = false;
        private int contadorTelas = 1;

        // SISTEMA DO DISCORD E RASTREADOR
        public DiscordRpcClient discordClient;
        private Timer rastreadorNome;
        private string nomeJogadorAtual = "";
        private bool discordHabilitado = true; // Controle da presença

        public Form1()
        {
            InitializeComponent();
            this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            this.Text = "Naruto Online";
            this.WindowState = FormWindowState.Maximized;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(15, 15, 15); // Fundo Principal Dark
            this.KeyPreview = true;

            CarregarConfiguracoes(); // Lê a memória do Discord antes de montar a tela
            LimparSessoesAntigas();

            ConfigurarInterface();
            CarregarContasSalvas();
            InitBrowser();

            IniciarDiscordRPC();
            IniciarRastreadorDeNome();

            ReduzirConsumoRAM();

            this.KeyDown += (s, e) => {
                if (e.KeyCode == Keys.Tab && !pnlLogin.Visible) { AlternarMenuLateral(); e.Handled = true; e.SuppressKeyPress = true; }
            };
        }

        private void CarregarConfiguracoes()
        {
            try
            {
                if (File.Exists(arquivoConfig))
                {
                    string valor = File.ReadAllText(arquivoConfig).Trim();
                    discordHabilitado = (valor == "1");
                }
            }
            catch { }
        }

        private void SalvarConfiguracoes()
        {
            try
            {
                File.WriteAllText(arquivoConfig, discordHabilitado ? "1" : "0");
            }
            catch { }
        }

        private void LimparSessoesAntigas()
        {
            string pastaSessoes = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Sessoes");
            if (Directory.Exists(pastaSessoes))
            {
                foreach (string diretorio in Directory.GetDirectories(pastaSessoes))
                {
                    try { if (!diretorio.Contains(sessionID)) Directory.Delete(diretorio, true); }
                    catch { }
                }
            }
        }

        private void ConfigurarInterface()
        {
            // --- PAINEL LATERAL (CONTROLES) ---
            pnlMenuLateral = new Panel { Width = 180, Dock = DockStyle.Right, BackColor = Color.FromArgb(25, 25, 25), Visible = false };

            Label lblTituloMenu = new Label { Text = "CONTROLES", ForeColor = Color.White, Font = new Font("Segoe UI", 10, FontStyle.Bold), Location = new Point(40, 20), AutoSize = true };

            Label lblInstancias = new Label { Text = "Multi-Contas:", ForeColor = Color.DarkGray, Font = new Font("Segoe UI", 8), Location = new Point(15, 55), AutoSize = true };
            btnNovaTela = new Button { Text = "ABRIR NOVA TELA", Location = new Point(15, 75), Size = new Size(150, 30), BackColor = Color.FromArgb(0, 90, 158), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            btnNovaTela.Click += (s, e) => { contadorTelas++; new Form1().Show(); };

            Label lblMotor = new Label { Text = "Sistema Central:", ForeColor = Color.DarkGray, Font = new Font("Segoe UI", 8), Location = new Point(15, 125), AutoSize = true };
            btnInjetar = new Button { Text = "INJETAR MOTOR", Location = new Point(15, 145), Size = new Size(150, 30), BackColor = Color.FromArgb(120, 60, 0), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 8, FontStyle.Bold) };
            btnInjetar.Click += (s, e) => ExecutarInjecaoBase();
            lblStatusInjecao = new Label { Text = "● Aguardando...", ForeColor = Color.DimGray, Font = new Font("Segoe UI", 7), Location = new Point(15, 180), AutoSize = true };

            Label lblSpeed = new Label { Text = "Motor de Velocidade:", ForeColor = Color.DarkGray, Font = new Font("Segoe UI", 8), Location = new Point(15, 210), AutoSize = true };
            btnSpeed = new Button { Text = "VELOCIDADE: 1X", Location = new Point(15, 230), Size = new Size(150, 30), BackColor = Color.FromArgb(45, 45, 45), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            btnSpeed.Click += (s, e) => AlternarSpeed();

            Label lblServerMenu = new Label { Text = "Trocar Servidor:", ForeColor = Color.DarkGray, Font = new Font("Segoe UI", 8), Location = new Point(15, 280), AutoSize = true };
            txtServerMenu = new TextBox { Location = new Point(15, 300), Width = 60, Text = "851", TextAlign = HorizontalAlignment.Center, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.FromArgb(45, 45, 45), ForeColor = Color.White };
            btnTrocarServer = new Button { Text = "MUDAR", Location = new Point(85, 299), Size = new Size(80, 23), BackColor = Color.FromArgb(60, 60, 60), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            btnTrocarServer.Click += (s, e) => CarregarJogo(txtServerMenu.Text);

            lnkGithub = new LinkLabel { Text = "GitHub / Proteuzada", LinkColor = Color.DimGray, ActiveLinkColor = Color.White, Font = new Font("Segoe UI", 7), AutoSize = true, LinkBehavior = LinkBehavior.HoverUnderline };
            lnkGithub.Location = new Point(15, pnlMenuLateral.Height - 30);
            lnkGithub.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            lnkGithub.LinkClicked += (s, e) => Process.Start("https://github.com/Proteuzada");

            pnlMenuLateral.Controls.AddRange(new Control[] { lblTituloMenu, lblInstancias, btnNovaTela, lblMotor, btnInjetar, lblStatusInjecao, lblSpeed, btnSpeed, lblServerMenu, txtServerMenu, btnTrocarServer, lnkGithub });
            this.Controls.Add(pnlMenuLateral);

            // --- BOTÃO TOGGLE (TAB) ---
            btnToggleMenu = new Button { Text = "▶", Size = new Size(35, 35), BackColor = Color.FromArgb(40, 40, 40), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Visible = false, Font = new Font("Segoe UI", 10, FontStyle.Bold), Cursor = Cursors.Hand };
            btnToggleMenu.FlatAppearance.BorderSize = 0;
            btnToggleMenu.Location = new Point(this.ClientSize.Width - 45, this.ClientSize.Height - 45);
            btnToggleMenu.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnToggleMenu.Click += (s, e) => AlternarMenuLateral();
            this.Controls.Add(btnToggleMenu);

            // --- TELA DE LOGIN ---
            pnlLogin = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(15, 15, 15) };
            pnlCaixaLogin = new Panel { Size = new Size(300, 380), BackColor = Color.FromArgb(30, 30, 30), BorderStyle = BorderStyle.FixedSingle };

            Label lblLogo = new Label { Text = "SEVENTH HEAVEN", ForeColor = Color.Gold, Font = new Font("Segoe UI", 16, FontStyle.Bold), AutoSize = false, Width = 300, Height = 35, TextAlign = ContentAlignment.MiddleCenter, Location = new Point(0, 20) };
            Label l1 = new Label { Text = "E-MAIL:", ForeColor = Color.DarkGray, Font = new Font("Segoe UI", 8, FontStyle.Bold), Location = new Point(30, 80), AutoSize = true };
            cmbEmail = new ComboBox { Location = new Point(30, 100), Width = 240, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(45, 45, 45), ForeColor = Color.White };
            cmbEmail.SelectedIndexChanged += (s, e) => PreencherSenhaAutomatica();

            Label l2 = new Label { Text = "SENHA:", ForeColor = Color.DarkGray, Font = new Font("Segoe UI", 8, FontStyle.Bold), Location = new Point(30, 140), AutoSize = true };
            txtSenha = new TextBox { Location = new Point(30, 160), Width = 240, UseSystemPasswordChar = true, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.FromArgb(45, 45, 45), ForeColor = Color.White };

            chkLembrar = new CheckBox { Text = "Lembrar Conta", ForeColor = Color.Gray, Font = new Font("Segoe UI", 8), Location = new Point(30, 185), AutoSize = true, Checked = true };

            Label l3 = new Label { Text = "SERVIDOR (Ex: 851):", ForeColor = Color.DarkGray, Font = new Font("Segoe UI", 8, FontStyle.Bold), Location = new Point(30, 220), AutoSize = true };
            txtServerLogin = new TextBox { Location = new Point(30, 240), Width = 100, Text = "851", TextAlign = HorizontalAlignment.Center, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.FromArgb(45, 45, 45), ForeColor = Color.White };

            // BOTÃO TOGGLE DISCORD NO LOGIN COM MEMÓRIA
            btnToggleDiscord = new Button { Location = new Point(30, 280), Size = new Size(240, 25), FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 7, FontStyle.Bold), ForeColor = Color.White };

            // Aplica o visual correto baseado no que foi carregado da memória
            if (discordHabilitado)
            {
                btnToggleDiscord.Text = "DISCORD: ATIVADO";
                btnToggleDiscord.BackColor = Color.FromArgb(114, 137, 218);
            }
            else
            {
                btnToggleDiscord.Text = "DISCORD: DESATIVADO";
                btnToggleDiscord.BackColor = Color.FromArgb(60, 60, 60);
            }

            btnToggleDiscord.Click += (s, e) => AlternarPresencaDiscord();

            btnLogar = new Button { Text = "ENTRAR NO JOGO", Location = new Point(30, 315), Size = new Size(240, 35), BackColor = Color.FromArgb(0, 120, 0), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
            btnLogar.Click += async (s, e) => await ExecutarLoginProcesso();

            lblStatusLogin = new Label { Text = "", ForeColor = Color.White, Font = new Font("Segoe UI", 8), Location = new Point(30, 355), AutoSize = true };

            pnlCaixaLogin.Controls.AddRange(new Control[] { lblLogo, l1, cmbEmail, l2, txtSenha, chkLembrar, l3, txtServerLogin, btnToggleDiscord, btnLogar, lblStatusLogin });
            pnlLogin.Controls.Add(pnlCaixaLogin);

            pnlLogin.Resize += (s, e) => { pnlCaixaLogin.Left = (pnlLogin.Width - pnlCaixaLogin.Width) / 2; pnlCaixaLogin.Top = (pnlLogin.Height - pnlCaixaLogin.Height) / 2; };

            this.Controls.Add(pnlLogin);
            pnlLogin.BringToFront();
        }

        private void AlternarMenuLateral()
        {
            if (!pnlLogin.Visible) { pnlMenuLateral.Visible = !pnlMenuLateral.Visible; btnToggleMenu.Text = pnlMenuLateral.Visible ? "▶" : "◀"; }
        }

        // =========================================================================
        // LOGICA DO DISCORD DINÂMICA
        // =========================================================================
        private void AlternarPresencaDiscord()
        {
            discordHabilitado = !discordHabilitado;

            // Salva a escolha do usuário na hora!
            SalvarConfiguracoes();

            if (discordHabilitado)
            {
                btnToggleDiscord.Text = "DISCORD: ATIVADO";
                btnToggleDiscord.BackColor = Color.FromArgb(114, 137, 218);
                IniciarDiscordRPC();
            }
            else
            {
                btnToggleDiscord.Text = "DISCORD: DESATIVADO";
                btnToggleDiscord.BackColor = Color.FromArgb(60, 60, 60);
                if (discordClient != null) { discordClient.Dispose(); discordClient = null; }
            }
        }

        private void IniciarDiscordRPC()
        {
            if (!discordHabilitado) return;
            try
            {
                discordClient = new DiscordRpcClient("1493045476218507294");
                discordClient.Initialize();
                AtualizarDiscord("Aguardando Login...", "Menu Inicial");
            }
            catch { }
        }

        private void AtualizarDiscord(string jogador, string servidor)
        {
            if (discordHabilitado && discordClient != null && discordClient.IsInitialized)
            {
                string statusServer = servidor == "Menu Inicial" ? "Menu Inicial" : $"Server: S{servidor}";

                discordClient.SetPresence(new RichPresence()
                {
                    Details = $"Player: {jogador}",
                    State = statusServer,
                    Assets = new Assets()
                    {
                        LargeImageKey = "logo_naruto",
                        LargeImageText = "Seventh Heaven Launcher"
                    },
                    Timestamps = Timestamps.Now
                });
            }
        }

        private void IniciarRastreadorDeNome()
        {
            rastreadorNome = new Timer();
            rastreadorNome.Interval = 5000; // Verifica a cada 5 segundos
            rastreadorNome.Tick += async (s, e) =>
            {
                if (browser != null && browser.CanExecuteJavascriptInMainFrame)
                {
                    string scriptJS = @"
                        (function() {
                            var spanUsuario = document.querySelector('.oas-bar-user-text');
                            if (spanUsuario) {
                                var nome = spanUsuario.innerText || spanUsuario.textContent;
                                if (nome && nome.trim().length > 0) {
                                    return nome.trim();
                                }
                            }
                            return ''; 
                        })();
                    ";

                    var resposta = await browser.EvaluateScriptAsync(scriptJS);

                    if (resposta.Success && resposta.Result != null)
                    {
                        string nomeEncontrado = resposta.Result.ToString().Trim();

                        if (!string.IsNullOrEmpty(nomeEncontrado) && nomeEncontrado != nomeJogadorAtual)
                        {
                            nomeJogadorAtual = nomeEncontrado;

                            this.Invoke((MethodInvoker)delegate {
                                this.Text = $"Naruto Online - [{nomeJogadorAtual}] - [S{txtServerMenu.Text}]";
                            });

                            AtualizarDiscord(nomeJogadorAtual, txtServerMenu.Text);
                        }
                    }
                }
            };
            rastreadorNome.Start();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (discordClient != null) discordClient.Dispose();
            base.OnFormClosing(e);
        }

        // =========================================================================
        // MOTOR CENTRAL E MULTI-CONTAS
        // =========================================================================
        private void ExecutarInjecaoBase()
        {
            if (dllInjetada) return;

            meuFlashPid = EncontrarProcessoDoFlash();
            if (meuFlashPid == -1)
            {
                MessageBox.Show("Nenhum processo do jogo (Flash) livre foi encontrado. Aguarde o boneco aparecer na tela!", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string dllPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SpeedEngine.dll");
            if (!File.Exists(dllPath)) { MessageBox.Show("Arquivo 'SpeedEngine.dll' não encontrado!", "Erro"); return; }

            try
            {
                IntPtr procHandle = OpenProcess(0x001F0FFF, false, meuFlashPid);
                if (procHandle == IntPtr.Zero) throw new Exception("Acesso negado.");

                IntPtr loadLibraryAddr = GetProcAddress(GetModuleHandle("kernel32.dll"), "LoadLibraryA");
                uint size = (uint)((dllPath.Length + 1) * Marshal.SizeOf(typeof(char)));
                IntPtr allocMemAddress = VirtualAllocEx(procHandle, IntPtr.Zero, size, 0x1000 | 0x2000, 4);

                WriteProcessMemory(procHandle, allocMemAddress, Encoding.Default.GetBytes(dllPath), size, out _);
                IntPtr thread = CreateRemoteThread(procHandle, IntPtr.Zero, 0, loadLibraryAddr, allocMemAddress, 0, IntPtr.Zero);

                if (thread != IntPtr.Zero)
                {
                    dllInjetada = true;
                    pidsInjetadosGlobais.Add(meuFlashPid);

                    btnInjetar.Text = "MOTOR INJETADO"; btnInjetar.BackColor = Color.FromArgb(0, 100, 0); btnInjetar.Enabled = false;
                    lblStatusInjecao.Text = "● Conectado (PID: " + meuFlashPid + ")"; lblStatusInjecao.ForeColor = Color.LimeGreen;
                }
                else { throw new Exception("Falha ao criar Thread remota."); }
            }
            catch (Exception ex) { MessageBox.Show("Erro de Injeção: " + ex.Message, "Erro Crítico"); }
        }

        private void AlternarSpeed()
        {
            if (!dllInjetada) { MessageBox.Show("Você precisa clicar em 'INJETAR MOTOR' primeiro!", "Atenção"); return; }

            if (!speedAtivo)
            {
                if (EnviarComandoPipe("2.0")) { btnSpeed.Text = "VELOCIDADE: 2X"; btnSpeed.BackColor = Color.DarkRed; speedAtivo = true; }
            }
            else
            {
                if (EnviarComandoPipe("1.0")) { btnSpeed.Text = "VELOCIDADE: 1X"; btnSpeed.BackColor = Color.FromArgb(45, 45, 45); speedAtivo = false; }
            }
        }

        private bool EnviarComandoPipe(string comando)
        {
            try
            {
                string nomeDoTubo = "NarutoEnginePipe_" + meuFlashPid;

                using (var pipeClient = new NamedPipeClientStream(".", nomeDoTubo, PipeDirection.Out))
                {
                    pipeClient.Connect(1000);
                    using (var writer = new StreamWriter(pipeClient)) { writer.AutoFlush = true; writer.Write(comando); }
                }
                return true;
            }
            catch { MessageBox.Show("Erro de comunicação com a DLL. O jogo pode ter fechado."); return false; }
        }

        private int EncontrarProcessoDoFlash()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = 'CefSharp.BrowserSubprocess.exe'"))
                using (var results = searcher.Get())
                {
                    foreach (ManagementObject obj in results)
                    {
                        if (obj["CommandLine"] != null && obj["CommandLine"].ToString().Contains("--type=ppapi"))
                        {
                            int pid = Convert.ToInt32(obj["ProcessId"]);
                            if (!pidsInjetadosGlobais.Contains(pid)) return pid;
                        }
                    }
                }
            }
            catch { }
            return -1;
        }

        // =========================================================================
        // LOGIN E NAVEGADOR CEFSHARP
        // =========================================================================
        private async Task ExecutarLoginProcesso()
        {
            string emailAtual = cmbEmail.Text.Trim(); string senhaAtual = txtSenha.Text; string servidorAtual = txtServerLogin.Text.Trim();
            if (string.IsNullOrEmpty(emailAtual) || string.IsNullOrEmpty(senhaAtual) || string.IsNullOrEmpty(servidorAtual))
            {
                lblStatusLogin.Text = "Preencha todos os campos!"; lblStatusLogin.ForeColor = Color.Red; return;
            }
            lblStatusLogin.Text = "● Autenticando com a OAS Games..."; lblStatusLogin.ForeColor = Color.Orange; btnLogar.Enabled = false;

            try
            {
                using (var handler = new HttpClientHandler { CookieContainer = new CookieContainer() })
                using (var client = new HttpClient(handler))
                {
                    string url = $"https://passport.oasgames.com/?m=login&email={Uri.EscapeDataString(emailAtual)}&pwd={Uri.EscapeDataString(senhaAtual)}&remember=1";
                    client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 Gamebox/3.4.7.0");
                    client.DefaultRequestHeaders.Add("Referer", "https://naruto.narutowebgame.com/pt/");

                    var response = await client.GetAsync(url);
                    var content = await response.Content.ReadAsStringAsync();

                    if (content.Contains("ok") || content.Contains("success"))
                    {
                        var manager = browser.RequestContext.GetCookieManager(null);
                        CookieCollection cookies = handler.CookieContainer.GetCookies(new Uri("https://passport.oasgames.com"));
                        foreach (System.Net.Cookie cookie in cookies)
                        {
                            var cefCookie = new CefSharp.Cookie { Name = cookie.Name, Value = cookie.Value, Domain = ".oasgames.com", Path = "/" };
                            await manager.SetCookieAsync("https://oasgames.com", cefCookie);
                            cefCookie.Domain = ".narutowebgame.com";
                            await manager.SetCookieAsync("https://narutowebgame.com", cefCookie);
                        }

                        GerenciarSalvamentoDeConta(emailAtual, senhaAtual);
                        pnlLogin.Visible = false; pnlMenuLateral.Visible = true; btnToggleMenu.Visible = true; btnToggleMenu.BringToFront();
                        txtServerMenu.Text = servidorAtual;
                        CarregarJogo(servidorAtual); ReduzirConsumoRAM();
                    }
                    else { lblStatusLogin.Text = "● Senha Incorreta ou Erro!"; lblStatusLogin.ForeColor = Color.Red; btnLogar.Enabled = true; }
                }
            }
            catch { lblStatusLogin.Text = "● Servidor Off-line"; btnLogar.Enabled = true; }
        }

        private void CarregarJogo(string servidorID)
        {
            this.Text = $"Naruto Online - [Aguardando Nome...] - [S{servidorID}]";
            AtualizarDiscord("Aguardando Nome...", servidorID);
            browser.Load($"https://naruto.narutowebgame.com/pt/serverlist/s{servidorID}?leftbar_collapse=yes&logintype=4&client=2&platform=oasis_gameclient");
            browser.Focus();
        }

        public void InitBrowser()
        {
            if (!Cef.IsInitialized)
            {
                var settings = new CefSettings();
                string flashPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pepflashplayer32.dll");

                settings.CefCommandLineArgs.Add("disable-gpu-vsync", "1");
                settings.CefCommandLineArgs.Add("disable-frame-rate-limit", "1");
                settings.CefCommandLineArgs.Add("ppapi-flash-path", flashPath);
                settings.CefCommandLineArgs.Add("ppapi-flash-version", "32.0.0.465");

                settings.CefCommandLineArgs.Add("enable-system-flash", "1");
                settings.CefCommandLineArgs.Add("plugin-policy", "allow");
                settings.CefCommandLineArgs.Add("always-authorize-plugins", "1");
                settings.CefCommandLineArgs.Add("allow-outdated-plugins", "1");
                settings.CefCommandLineArgs.Add("disable-features", "PluginPowerSaver,EnableEphemeralFlashPermission");

                settings.UserAgent = "Mozilla/5.0 (Windows NT 10.0; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/86.0.4240.198 Safari/537.36 Gamebox/3.4.7.0";
                Cef.Initialize(settings);
            }

            string cacheDaSessao = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Sessoes", sessionID);
            var reqContext = new RequestContext(new RequestContextSettings { CachePath = cacheDaSessao });

            browser = new ChromiumWebBrowser("about:blank") { RequestContext = reqContext };
            browser.KeyboardHandler = new CefKeyboardHandler(AlternarMenuLateral);
            browser.Dock = DockStyle.Fill; browser.JavascriptObjectRepository.Settings.LegacyBindingEnabled = true; browser.JavascriptObjectRepository.Register("bound", new OasisProxy(), isAsync: false);
            browser.FrameLoadStart += (s, args) => { if (args.Frame.IsMain) args.Frame.ExecuteJavaScriptAsync("window.isMiniClient = true; window.gamebox = { version: '3.4.7.0', platform: 'oasis' }; if (typeof bound !== 'undefined') { window.external = bound; }"); };
            this.Controls.Add(browser); browser.SendToBack();
        }

        // =========================================================================
        // CONTAS E UTILITÁRIOS
        // =========================================================================
        private string CriptografarSenha(string senha) { try { return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(senha), null, DataProtectionScope.CurrentUser)); } catch { return ""; } }
        private string DescriptografarSenha(string textoCriptografado) { try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(textoCriptografado), null, DataProtectionScope.CurrentUser)); } catch { return ""; } }
        private void CarregarContasSalvas() { lock (lockArquivo) { if (File.Exists(arquivoContas)) { foreach (string linha in File.ReadAllLines(arquivoContas)) { string[] p = linha.Split('|'); if (p.Length == 2 && !contasSalvas.ContainsKey(p[0])) { contasSalvas.Add(p[0], p[1]); cmbEmail.Items.Add(p[0]); } } if (cmbEmail.Items.Count > 0) cmbEmail.SelectedIndex = cmbEmail.Items.Count - 1; } } }
        private void PreencherSenhaAutomatica() { if (contasSalvas.ContainsKey(cmbEmail.Text)) { txtSenha.Text = DescriptografarSenha(contasSalvas[cmbEmail.Text]); chkLembrar.Checked = true; } else { txtSenha.Text = ""; } }
        private void GerenciarSalvamentoDeConta(string email, string senha) { if (chkLembrar.Checked) { contasSalvas[email] = CriptografarSenha(senha); if (!cmbEmail.Items.Contains(email)) cmbEmail.Items.Add(email); } else { if (contasSalvas.ContainsKey(email)) contasSalvas.Remove(email); if (cmbEmail.Items.Contains(email)) cmbEmail.Items.Remove(email); } List<string> linhasSalvar = new List<string>(); foreach (var c in contasSalvas) { linhasSalvar.Add($"{c.Key}|{c.Value}"); } lock (lockArquivo) { File.WriteAllLines(arquivoContas, linhasSalvar); } }
        private void ReduzirConsumoRAM() { try { GC.Collect(); GC.WaitForPendingFinalizers(); if (Environment.OSVersion.Platform == PlatformID.Win32NT) SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, -1, -1); } catch { } }
    }

    public class OasisProxy { public string getVersion() { return "3.4.7.0"; } public void NotifyLoginStatus(string s) { } public void setWindowSize(int w, int h) { } public void CloseGame() { Application.Exit(); } }

    // ATALHO F12 MANTIDO
    public class CefKeyboardHandler : IKeyboardHandler
    {
        private Action alternarMenu;
        public CefKeyboardHandler(Action acao) { alternarMenu = acao; }
        public bool OnPreKeyEvent(IWebBrowser browserControl, IBrowser browser, KeyType type, int windowsKeyCode, int nativeKeyCode, CefEventFlags modifiers, bool isSystemKey, ref bool isKeyboardShortcut)
        {
            if (type == KeyType.RawKeyDown && windowsKeyCode == 0x09)
            {
                if (browserControl is Control control) { control.BeginInvoke(alternarMenu); }
            }
            if (type == KeyType.RawKeyDown && windowsKeyCode == 0x7B)
            {
                browserControl.ShowDevTools();
            }
            return false;
        }
        public bool OnKeyEvent(IWebBrowser browserControl, IBrowser browser, KeyType type, int windowsKeyCode, int nativeKeyCode, CefEventFlags modifiers, bool isSystemKey) { return false; }
    }
}