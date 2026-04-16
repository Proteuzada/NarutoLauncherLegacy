using CefSharp;
using CefSharp.WinForms;
using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows.Forms;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Diagnostics;
using AutoUpdaterDotNET;

using Button = System.Windows.Forms.Button;

namespace NarutoLauncherLegacy
{
    public partial class Form1 : Form
    {
        // ==========================================
        // MÓDULOS EXTERNOS DA NOSSA ARQUITETURA
        // ==========================================
        private BrowserEngine motorNavegador;
        private DiscordIntegration discord;

        // ==========================================
        // COMPONENTES DA INTERFACE
        // ==========================================
        public ChromiumWebBrowser browser;
        public Panel pnlLogin, pnlCaixaLogin, pnlMenuLateral;
        public ComboBox cmbEmail;
        public TextBox txtSenha, txtServerLogin, txtServerMenu;
        public Button btnLogar, btnInjetar, btnSpeed, btnNovaTela, btnTrocarServer, btnToggleMenu, btnToggleDiscord, btnLimparRAM, btnToggleGPU, btnAtualizarPagina;
        public CheckBox chkLembrar;
        public Label lblStatusLogin, lblStatusInjecao;
        public LinkLabel lnkGithub;

        // ==========================================
        // VARIÁVEIS DE ESTADO E SESSÃO
        // ==========================================
        private Dictionary<string, string> contasSalvas = new Dictionary<string, string>();
        private string arquivoContas = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "contas_salvas.dat");
        private string arquivoConfig = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.dat");
        private static readonly object lockArquivo = new object();

        private string sessionID = "default_session";

        public static HashSet<int> pidsInjetadosGlobais = new HashSet<int>();
        private int meuFlashPid = 0;
        private bool dllInjetada = false;
        private bool speedAtivo = false;
        private bool isShuttingDown = false;
        private string nomeJogadorAtual = "";

        // Segurança
        private bool acessoLiberado = false;
        private bool discordHabilitado = true;
        private bool usarGPU = true;
        private Timer rastreadorNome;

        public Form1()
        {
            InitializeComponent();
            try { this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            this.Text = "Naruto Online";
            this.WindowState = FormWindowState.Maximized;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(15, 15, 15);
            this.KeyPreview = true;

            AutoUpdater.CheckForUpdateEvent += (args) =>
            {
                if (args.IsUpdateAvailable)
                {
                    if (MessageBox.Show($"Uma nova atualização foi encontrada (Versão {args.CurrentVersion}).\n\nSua versão atual: {args.InstalledVersion}\n\nEsta atualização é OBRIGATÓRIA. Baixar agora?", "Atualização", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                    {
                        try { if (AutoUpdater.DownloadUpdate(args)) { PrepararFechamentoSeguro(); Application.Exit(); } }
                        catch (Exception ex) { MessageBox.Show("Erro: " + ex.Message); }
                    }
                    else { PrepararFechamentoSeguro(); Environment.Exit(0); }
                }
            };
            AutoUpdater.Start("https://raw.githubusercontent.com/Proteuzada/NarutoOnline-Launcher/refs/heads/main/update.xml");

            CarregarConfiguracoes();
            ConfigurarInterface();
            CarregarContasSalvas();

            this.KeyDown += (s, e) => {
                if (e.KeyCode == Keys.Tab && acessoLiberado && !pnlLogin.Visible) { AlternarMenuLateral(); e.Handled = true; e.SuppressKeyPress = true; }
            };

            this.Load += Form1_Load;
            this.FormClosing += Form1_FormClosing;
        }

        private async void Form1_Load(object sender, EventArgs e)
        {
            this.Enabled = false;
            pnlLogin.Visible = false;

            bool temAcesso = await SecurityManager.VerificarAcesso();

            if (temAcesso)
            {
                FinalizarCarregamentoEAutorizar();
            }
            else
            {
                using (var telaHWID = new FormHWID())
                {
                    if (telaHWID.ShowDialog() == DialogResult.OK)
                    {
                        FinalizarCarregamentoEAutorizar();
                    }
                    else
                    {
                        Process.GetCurrentProcess().Kill();
                    }
                }
            }
        }

        private void FinalizarCarregamentoEAutorizar()
        {
            acessoLiberado = true;
            this.Enabled = true;

            discord = new DiscordIntegration(discordHabilitado);
            discord.Inicializar();

            pnlLogin.Visible = true;
            pnlLogin.BringToFront();

            IniciarRastreadorDeNome();
            MemoryInjector.OtimizarRAM(true);
        }

        private void InitBrowserModule()
        {
            motorNavegador = new BrowserEngine(sessionID, usarGPU, ResetarEstadoInjecao, () => { if (acessoLiberado) AlternarMenuLateral(); });
            motorNavegador.InicializarCefSharp();
            browser = motorNavegador.CriarNavegador();
            this.Controls.Add(browser);
            browser.SendToBack();
        }

        private async Task ExecutarLoginProcesso()
        {
            string emailAtual = cmbEmail.Text.Trim();
            string senhaAtual = txtSenha.Text;
            string servidorAtual = txtServerLogin.Text.Trim();

            if (string.IsNullOrEmpty(emailAtual) || string.IsNullOrEmpty(senhaAtual) || string.IsNullOrEmpty(servidorAtual))
            {
                lblStatusLogin.Text = "Preencha todos os campos!";
                lblStatusLogin.ForeColor = Color.Red;
                return;
            }

            lblStatusLogin.Text = "● Autenticando com a OAS Games...";
            lblStatusLogin.ForeColor = Color.Orange;
            btnLogar.Enabled = false;

            this.sessionID = "Conta_" + string.Join("_", emailAtual.Split(Path.GetInvalidFileNameChars()));

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
                        if (browser != null)
                        {
                            this.Controls.Remove(browser);
                            browser.Dispose();
                        }

                        InitBrowserModule();

                        var manager = browser.RequestContext.GetCookieManager(null);
                        foreach (System.Net.Cookie cookie in handler.CookieContainer.GetCookies(new Uri("https://passport.oasgames.com")))
                        {
                            var cefCookie = new CefSharp.Cookie { Name = cookie.Name, Value = cookie.Value, Domain = ".oasgames.com", Path = "/" };
                            await manager.SetCookieAsync("https://oasgames.com", cefCookie);
                            cefCookie.Domain = ".narutowebgame.com";
                            await manager.SetCookieAsync("https://narutowebgame.com", cefCookie);
                        }

                        GerenciarSalvamentoDeConta(emailAtual, senhaAtual);
                        pnlLogin.Visible = false;
                        pnlMenuLateral.Visible = true;
                        btnToggleMenu.Visible = true;
                        btnToggleMenu.BringToFront();

                        txtServerMenu.Text = servidorAtual;
                        CarregarJogo(servidorAtual);
                        MemoryInjector.OtimizarRAM(true);
                    }
                    else
                    {
                        lblStatusLogin.Text = "● Senha Incorreta ou Erro!";
                        lblStatusLogin.ForeColor = Color.Red;
                        btnLogar.Enabled = true;
                    }
                }
            }
            catch { lblStatusLogin.Text = "● Servidor Off-line"; btnLogar.Enabled = true; }
        }

        private void CarregarJogo(string servidorID)
        {
            this.Text = $"Naruto Online - [Aguardando Nome...] - [S{servidorID}]";
            discord.AtualizarPresenca("Aguardando Nome...", servidorID);
            ResetarEstadoInjecao();
            browser.Load($"https://naruto.narutowebgame.com/pt/serverlist/s{servidorID}?leftbar_collapse=yes&logintype=4&client=2&platform=oasis_gameclient");
            browser.Focus();
        }

        private void ResetarEstadoInjecao()
        {
            if (this.InvokeRequired) { this.Invoke(new Action(ResetarEstadoInjecao)); return; }

            if (meuFlashPid != 0 && pidsInjetadosGlobais.Contains(meuFlashPid))
            {
                lock (pidsInjetadosGlobais) { pidsInjetadosGlobais.Remove(meuFlashPid); }
            }

            dllInjetada = false; speedAtivo = false; meuFlashPid = 0;
            btnInjetar.Text = "INJETAR MOTOR"; btnInjetar.BackColor = Color.FromArgb(120, 60, 0); btnInjetar.Enabled = true;
            lblStatusInjecao.Text = "● Aguardando..."; lblStatusInjecao.ForeColor = Color.DimGray;
            btnSpeed.Text = "VELOCIDADE: 1X"; btnSpeed.BackColor = Color.FromArgb(45, 45, 45);
        }

        private void ExecutarInjecaoBase()
        {
            if (dllInjetada) return;
            meuFlashPid = MemoryInjector.EncontrarProcessoDoFlash(pidsInjetadosGlobais);
            if (meuFlashPid == -1) { MessageBox.Show("Nenhum processo do jogo livre foi encontrado nesta janela. Aguarde o boneco aparecer na tela!", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }

            if (MemoryInjector.InjetarDLL(meuFlashPid))
            {
                dllInjetada = true;
                lock (pidsInjetadosGlobais) { pidsInjetadosGlobais.Add(meuFlashPid); }
                btnInjetar.Text = "MOTOR INJETADO"; btnInjetar.BackColor = Color.FromArgb(0, 100, 0); btnInjetar.Enabled = false;
                lblStatusInjecao.Text = "● Conectado (PID: " + meuFlashPid + ")"; lblStatusInjecao.ForeColor = Color.LimeGreen;
            }
            else { MessageBox.Show("Erro de Injeção. A DLL SpeedEngine está na pasta?", "Erro Crítico"); }
        }

        private void AlternarSpeed()
        {
            if (!dllInjetada) { MessageBox.Show("Você precisa clicar em 'INJETAR MOTOR' primeiro!", "Atenção"); return; }
            string cmd = speedAtivo ? "1.0" : "2.0";
            if (MemoryInjector.EnviarComandoVelocidade(meuFlashPid, cmd))
            {
                speedAtivo = !speedAtivo;
                btnSpeed.Text = speedAtivo ? "VELOCIDADE: 2X" : "VELOCIDADE: 1X";
                btnSpeed.BackColor = speedAtivo ? Color.DarkRed : Color.FromArgb(45, 45, 45);
            }
            else { if (!isShuttingDown) { MessageBox.Show("Conexão perdida com o motor. O sistema será redefinido.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning); ResetarEstadoInjecao(); } }
        }

        private void ExecutarLimpezaRAM_Manual()
        {
            btnLimparRAM.Enabled = false;
            Task.Run(() => {
                MemoryInjector.OtimizarRAM(true);
                this.Invoke((MethodInvoker)delegate { btnLimparRAM.Text = "✓ RAM OTIMIZADA"; btnLimparRAM.BackColor = Color.DarkGreen; });
                Task.Delay(2000).Wait();
                this.Invoke((MethodInvoker)delegate { btnLimparRAM.Text = "OTIMIZAR RAM"; btnLimparRAM.BackColor = Color.FromArgb(70, 30, 100); btnLimparRAM.Enabled = true; });
            });
        }

        private void IniciarRastreadorDeNome()
        {
            rastreadorNome = new Timer(); rastreadorNome.Interval = 5000;
            rastreadorNome.Tick += async (s, e) =>
            {
                if (isShuttingDown || this.IsDisposed || !this.IsHandleCreated) return;
                if (browser != null && browser.CanExecuteJavascriptInMainFrame)
                {
                    string scriptJS = @"(function() { var spanUsuario = document.querySelector('.oas-bar-user-text'); if (spanUsuario) { var nome = spanUsuario.innerText || spanUsuario.textContent; if (nome && nome.trim().length > 0) { return nome.trim(); } } return ''; })();";
                    var resposta = await browser.EvaluateScriptAsync(scriptJS);
                    if (isShuttingDown || this.IsDisposed || !this.IsHandleCreated) return;
                    if (resposta.Success && resposta.Result != null)
                    {
                        string nomeEncontrado = resposta.Result.ToString().Trim();
                        if (!string.IsNullOrEmpty(nomeEncontrado) && nomeEncontrado != nomeJogadorAtual)
                        {
                            nomeJogadorAtual = nomeEncontrado;
                            this.Invoke((MethodInvoker)delegate { this.Text = $"Naruto Online - [{nomeJogadorAtual}] - [S{txtServerMenu.Text}]"; });
                            discord.AtualizarPresenca(nomeJogadorAtual, txtServerMenu.Text);
                        }
                    }
                }
            };
            rastreadorNome.Start();
        }

        private void CarregarConfiguracoes() { try { if (File.Exists(arquivoConfig)) { string[] v = File.ReadAllText(arquivoConfig).Trim().Split(','); if (v.Length >= 1) discordHabilitado = (v[0] == "1"); if (v.Length >= 2) usarGPU = (v[1] == "1"); } } catch { } }
        private void SalvarConfiguracoes() { try { File.WriteAllText(arquivoConfig, $"{(discordHabilitado ? "1" : "0")},{(usarGPU ? "1" : "0")}"); } catch { } }

        private void ConfigurarInterface()
        {
            pnlMenuLateral = new Panel { Width = 180, Dock = DockStyle.Right, BackColor = Color.FromArgb(25, 25, 25), Visible = false };
            Label lblTituloMenu = new Label { Text = "CONTROLES", ForeColor = Color.White, Font = new Font("Segoe UI", 10, FontStyle.Bold), Location = new Point(40, 20), AutoSize = true };
            Label lblInstancias = new Label { Text = "Multi-Contas:", ForeColor = Color.DarkGray, Font = new Font("Segoe UI", 8), Location = new Point(15, 55), AutoSize = true };

            btnNovaTela = new Button { Text = "ABRIR NOVA TELA", Location = new Point(15, 75), Size = new Size(150, 30), BackColor = Color.FromArgb(0, 90, 158), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            btnNovaTela.Click += (s, e) => { new Form1().Show(); };

            Label lblMotor = new Label { Text = "Sistema Central:", ForeColor = Color.DarkGray, Font = new Font("Segoe UI", 8), Location = new Point(15, 120), AutoSize = true };
            btnInjetar = new Button { Text = "INJETAR MOTOR", Location = new Point(15, 140), Size = new Size(150, 30), BackColor = Color.FromArgb(120, 60, 0), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 8, FontStyle.Bold) };
            btnInjetar.Click += (s, e) => ExecutarInjecaoBase();
            lblStatusInjecao = new Label { Text = "● Aguardando...", ForeColor = Color.DimGray, Font = new Font("Segoe UI", 7), Location = new Point(15, 175), AutoSize = true };
            Label lblSpeed = new Label { Text = "Motor de Velocidade:", ForeColor = Color.DarkGray, Font = new Font("Segoe UI", 8), Location = new Point(15, 200), AutoSize = true };
            btnSpeed = new Button { Text = "VELOCIDADE: 1X", Location = new Point(15, 220), Size = new Size(150, 30), BackColor = Color.FromArgb(45, 45, 45), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            btnSpeed.Click += (s, e) => AlternarSpeed();
            Label lblPerformance = new Label { Text = "Performance Máxima:", ForeColor = Color.DarkGray, Font = new Font("Segoe UI", 8), Location = new Point(15, 265), AutoSize = true };
            btnLimparRAM = new Button { Text = "OTIMIZAR RAM", Location = new Point(15, 285), Size = new Size(150, 30), BackColor = Color.FromArgb(70, 30, 100), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 8, FontStyle.Bold) };
            btnLimparRAM.Click += (s, e) => ExecutarLimpezaRAM_Manual();
            Label lblServerMenu = new Label { Text = "Trocar Servidor:", ForeColor = Color.DarkGray, Font = new Font("Segoe UI", 8), Location = new Point(15, 330), AutoSize = true };
            txtServerMenu = new TextBox { Location = new Point(15, 350), Width = 60, Text = "851", TextAlign = HorizontalAlignment.Center, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.FromArgb(45, 45, 45), ForeColor = Color.White };
            btnTrocarServer = new Button { Text = "MUDAR", Location = new Point(85, 349), Size = new Size(80, 23), BackColor = Color.FromArgb(60, 60, 60), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            btnTrocarServer.Click += (s, e) => CarregarJogo(txtServerMenu.Text);
            btnAtualizarPagina = new Button { Text = "ATUALIZAR PÁGINA", Location = new Point(15, 395), Size = new Size(150, 30), BackColor = Color.FromArgb(100, 30, 30), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 8, FontStyle.Bold) };
            btnAtualizarPagina.Click += (s, e) => { ResetarEstadoInjecao(); if (browser != null && browser.IsBrowserInitialized) browser.Reload(true); };
            lnkGithub = new LinkLabel { Text = "GitHub / Proteuzada", LinkColor = Color.DimGray, ActiveLinkColor = Color.White, Font = new Font("Segoe UI", 7), AutoSize = true, LinkBehavior = LinkBehavior.HoverUnderline };
            lnkGithub.Location = new Point(15, pnlMenuLateral.Height - 30); lnkGithub.Anchor = AnchorStyles.Bottom | AnchorStyles.Left; lnkGithub.LinkClicked += (s, e) => Process.Start("https://github.com/Proteuzada");
            pnlMenuLateral.Controls.AddRange(new Control[] { lblTituloMenu, lblInstancias, btnNovaTela, lblMotor, btnInjetar, lblStatusInjecao, lblSpeed, btnSpeed, lblPerformance, btnLimparRAM, lblServerMenu, txtServerMenu, btnTrocarServer, btnAtualizarPagina, lnkGithub });
            this.Controls.Add(pnlMenuLateral);

            btnToggleMenu = new Button { Text = "▶", Size = new Size(35, 35), BackColor = Color.FromArgb(40, 40, 40), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Visible = false, Font = new Font("Segoe UI", 10, FontStyle.Bold), Cursor = Cursors.Hand };
            btnToggleMenu.FlatAppearance.BorderSize = 0; btnToggleMenu.Location = new Point(this.ClientSize.Width - 45, this.ClientSize.Height - 45); btnToggleMenu.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnToggleMenu.Click += (s, e) => AlternarMenuLateral();
            this.Controls.Add(btnToggleMenu);

            pnlLogin = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(15, 15, 15), Visible = false };
            pnlCaixaLogin = new Panel { Size = new Size(300, 430), BackColor = Color.FromArgb(30, 30, 30), BorderStyle = BorderStyle.FixedSingle };
            Label lblLogo = new Label { Text = "SEVENTH HEAVEN", ForeColor = Color.Gold, Font = new Font("Segoe UI", 16, FontStyle.Bold), AutoSize = false, Width = 300, Height = 35, TextAlign = ContentAlignment.MiddleCenter, Location = new Point(0, 20) };
            Label l1 = new Label { Text = "E-MAIL:", ForeColor = Color.DarkGray, Font = new Font("Segoe UI", 8, FontStyle.Bold), Location = new Point(30, 80), AutoSize = true };
            cmbEmail = new ComboBox { Location = new Point(30, 100), Width = 240, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(45, 45, 45), ForeColor = Color.White };
            cmbEmail.SelectedIndexChanged += (s, e) => PreencherSenhaAutomatica();
            cmbEmail.KeyDown += async (s, e) => { if (e.KeyCode == Keys.Enter) { e.Handled = true; e.SuppressKeyPress = true; await ExecutarLoginProcesso(); } };
            Label l2 = new Label { Text = "SENHA:", ForeColor = Color.DarkGray, Font = new Font("Segoe UI", 8, FontStyle.Bold), Location = new Point(30, 140), AutoSize = true };
            txtSenha = new TextBox { Location = new Point(30, 160), Width = 240, UseSystemPasswordChar = true, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.FromArgb(45, 45, 45), ForeColor = Color.White };
            txtSenha.KeyDown += async (s, e) => { if (e.KeyCode == Keys.Enter) { e.Handled = true; e.SuppressKeyPress = true; await ExecutarLoginProcesso(); } };
            chkLembrar = new CheckBox { Text = "Lembrar Conta", ForeColor = Color.Gray, Font = new Font("Segoe UI", 8), Location = new Point(30, 185), AutoSize = true, Checked = true };
            Label l3 = new Label { Text = "SERVIDOR (Ex: 851):", ForeColor = Color.DarkGray, Font = new Font("Segoe UI", 8, FontStyle.Bold), Location = new Point(30, 220), AutoSize = true };
            txtServerLogin = new TextBox { Location = new Point(30, 240), Width = 100, Text = "851", TextAlign = HorizontalAlignment.Center, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.FromArgb(45, 45, 45), ForeColor = Color.White };
            txtServerLogin.KeyDown += async (s, e) => { if (e.KeyCode == Keys.Enter) { e.Handled = true; e.SuppressKeyPress = true; await ExecutarLoginProcesso(); } };

            btnToggleGPU = new Button { Location = new Point(30, 275), Size = new Size(240, 25), FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 7, FontStyle.Bold), ForeColor = Color.White };
            if (usarGPU) { btnToggleGPU.Text = "USAR PLACA DE VÍDEO: LIGADO"; btnToggleGPU.BackColor = Color.FromArgb(0, 90, 158); } else { btnToggleGPU.Text = "MODO PC FRACO (GPU OFF)"; btnToggleGPU.BackColor = Color.DarkOrange; }
            btnToggleGPU.Click += (s, e) => { usarGPU = !usarGPU; SalvarConfiguracoes(); MessageBox.Show("Configuração gráfica alterada!\n\nO Launcher será fechado para aplicar as mudanças de Performance. Por favor, abra-o novamente.", "Configuração Aplicada", MessageBoxButtons.OK, MessageBoxIcon.Information); Environment.Exit(0); };

            btnToggleDiscord = new Button { Location = new Point(30, 310), Size = new Size(240, 25), FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 7, FontStyle.Bold), ForeColor = Color.White };
            if (discordHabilitado) { btnToggleDiscord.Text = "DISCORD: ATIVADO"; btnToggleDiscord.BackColor = Color.FromArgb(114, 137, 218); } else { btnToggleDiscord.Text = "DISCORD: DESATIVADO"; btnToggleDiscord.BackColor = Color.FromArgb(60, 60, 60); }
            btnToggleDiscord.Click += (s, e) => AlternarPresencaDiscord();

            btnLogar = new Button { Text = "ENTRAR NO JOGO", Location = new Point(30, 345), Size = new Size(240, 35), BackColor = Color.FromArgb(0, 120, 0), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
            btnLogar.Click += async (s, e) => await ExecutarLoginProcesso();

            lblStatusLogin = new Label { Text = "", ForeColor = Color.White, Font = new Font("Segoe UI", 8), Location = new Point(30, 390), AutoSize = true };

            pnlCaixaLogin.Controls.AddRange(new Control[] { lblLogo, l1, cmbEmail, l2, txtSenha, chkLembrar, l3, txtServerLogin, btnToggleGPU, btnToggleDiscord, btnLogar, lblStatusLogin });
            pnlLogin.Controls.Add(pnlCaixaLogin);
            pnlLogin.Resize += (s, e) => { pnlCaixaLogin.Left = (pnlLogin.Width - pnlCaixaLogin.Width) / 2; pnlCaixaLogin.Top = (pnlLogin.Height - pnlCaixaLogin.Height) / 2; };
            this.Controls.Add(pnlLogin);
        }

        private void AlternarMenuLateral() { if (!pnlLogin.Visible) { pnlMenuLateral.Visible = !pnlMenuLateral.Visible; btnToggleMenu.Text = pnlMenuLateral.Visible ? "▶" : "◀"; } }

        private void CarregarContasSalvas() { lock (lockArquivo) { if (File.Exists(arquivoContas)) { foreach (string linha in File.ReadAllLines(arquivoContas)) { string[] p = linha.Split('|'); if (p.Length == 2 && !contasSalvas.ContainsKey(p[0])) { contasSalvas.Add(p[0], p[1]); cmbEmail.Items.Add(p[0]); } } if (cmbEmail.Items.Count > 0) cmbEmail.SelectedIndex = cmbEmail.Items.Count - 1; } } }
        private void PreencherSenhaAutomatica() { if (contasSalvas.ContainsKey(cmbEmail.Text)) { txtSenha.Text = SecurityManager.DescriptografarSenha(contasSalvas[cmbEmail.Text]); chkLembrar.Checked = true; } else { txtSenha.Text = ""; } }
        private void GerenciarSalvamentoDeConta(string email, string senha) { if (chkLembrar.Checked) { contasSalvas[email] = SecurityManager.CriptografarSenha(senha); if (!cmbEmail.Items.Contains(email)) cmbEmail.Items.Add(email); } else { if (contasSalvas.ContainsKey(email)) contasSalvas.Remove(email); if (cmbEmail.Items.Contains(email)) cmbEmail.Items.Remove(email); } List<string> linhasSalvar = new List<string>(); foreach (var c in contasSalvas) { linhasSalvar.Add($"{c.Key}|{c.Value}"); } lock (lockArquivo) { File.WriteAllLines(arquivoContas, linhasSalvar); } }

        private void AlternarPresencaDiscord()
        {
            discordHabilitado = !discordHabilitado;
            SalvarConfiguracoes();
            discord.AlternarPresenca(discordHabilitado);
            if (discordHabilitado) { btnToggleDiscord.Text = "DISCORD: ATIVADO"; btnToggleDiscord.BackColor = Color.FromArgb(114, 137, 218); }
            else { btnToggleDiscord.Text = "DISCORD: DESATIVADO"; btnToggleDiscord.BackColor = Color.FromArgb(60, 60, 60); }
        }

        private void PrepararFechamentoSeguro()
        {
            isShuttingDown = true;
            if (rastreadorNome != null) { rastreadorNome.Stop(); rastreadorNome.Dispose(); }
            if (discord != null) { discord.Desligar(); }

            if (meuFlashPid != 0 && pidsInjetadosGlobais.Contains(meuFlashPid))
            {
                lock (pidsInjetadosGlobais) { pidsInjetadosGlobais.Remove(meuFlashPid); }
            }

            if (browser != null) { browser.Dispose(); }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            PrepararFechamentoSeguro();

            if (Application.OpenForms.Count <= 1)
            {
                if (Cef.IsInitialized) { Cef.Shutdown(); }
            }
        }
    }
}