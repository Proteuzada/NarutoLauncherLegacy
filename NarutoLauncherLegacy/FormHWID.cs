using System;
using System.Drawing;
using System.Windows.Forms;
using System.Threading.Tasks;

namespace NarutoLauncherLegacy
{
    public partial class FormHWID : Form
    {
        private TextBox txtKey;
        private Button btnVerificar;
        private Button btnCopiar;
        private Label lblStatus;

        public FormHWID()
        {
            InitializeComponent();
            ConfigurarEstiloNativo();
            txtKey.Text = SecurityManager.ObterIdentidade();
        }

        private void ConfigurarEstiloNativo()
        {
            this.Text = "Autenticação de Acesso";
            this.Size = new Size(400, 300);
            this.BackColor = Color.FromArgb(20, 20, 20);
            this.ForeColor = Color.White;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            Label lblInfo = new Label
            {
                Text = "Sua Key de acesso não está autorizada.\nEnvie o código abaixo no Discord para liberação:",
                Location = new Point(20, 20),
                Size = new Size(350, 45),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 9, FontStyle.Regular)
            };

            txtKey = new TextBox
            {
                Location = new Point(45, 80),
                Size = new Size(300, 30),
                ReadOnly = true,
                BackColor = Color.Black,
                ForeColor = Color.FromArgb(241, 196, 15),
                Font = new Font("Consolas", 13, FontStyle.Bold),
                TextAlign = HorizontalAlignment.Center,
                BorderStyle = BorderStyle.FixedSingle
            };

            btnCopiar = new Button
            {
                Text = "COPIAR MINHA KEY",
                Location = new Point(45, 120),
                Size = new Size(300, 35),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(45, 45, 48),
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 9, FontStyle.Bold)
            };
            btnCopiar.FlatAppearance.BorderSize = 0;
            btnCopiar.Click += (s, e) =>
            {
                if (!string.IsNullOrEmpty(txtKey.Text))
                {
                    Clipboard.SetText(txtKey.Text);
                    lblStatus.Text = "✓ Key copiada com sucesso!";
                    lblStatus.ForeColor = Color.DeepSkyBlue;
                }
            };

            btnVerificar = new Button
            {
                Text = "JÁ REGISTREI, ENTRAR AGORA!",
                Location = new Point(45, 170),
                Size = new Size(300, 50),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(39, 174, 96),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnVerificar.FlatAppearance.BorderSize = 0;
            btnVerificar.Click += async (s, e) => await ExecutarVerificacao();

            lblStatus = new Label
            {
                Text = "Aguardando registro...",
                Location = new Point(20, 230),
                Size = new Size(350, 25),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.Gray,
                Font = new Font("Segoe UI", 8, FontStyle.Italic)
            };

            this.Controls.AddRange(new Control[] { lblInfo, txtKey, btnCopiar, btnVerificar, lblStatus });
        }

        private async Task ExecutarVerificacao()
        {
            btnVerificar.Enabled = false;
            btnCopiar.Enabled = false;
            lblStatus.Text = "Consultando banco de dados...";
            lblStatus.ForeColor = Color.White;

            bool autorizado = await SecurityManager.VerificarAcesso();

            if (autorizado)
            {
                lblStatus.Text = "ACESSO LIBERADO!";
                lblStatus.ForeColor = Color.Lime;
                await Task.Delay(800);
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
            else
            {
                lblStatus.Text = "Erro: Esta Key ainda não foi autorizada.";
                lblStatus.ForeColor = Color.FromArgb(231, 76, 60);
                btnVerificar.Enabled = true;
                btnCopiar.Enabled = true;
            }
        }
    }
}