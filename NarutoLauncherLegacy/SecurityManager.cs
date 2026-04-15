using System;
using System.Management;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace NarutoLauncherLegacy
{
    public static class SecurityManager
    {
        private static readonly string urlFirebase = "https://seventh-94a18-default-rtdb.firebaseio.com/";

        public static string ObterHWID()
        {
            string hardwareInfo = "";
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_BaseBoard"))
                    foreach (ManagementObject mo in searcher.Get()) hardwareInfo += mo["SerialNumber"]?.ToString();

                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT ProcessorId FROM Win32_Processor"))
                    foreach (ManagementObject mo in searcher.Get()) hardwareInfo += mo["ProcessorId"]?.ToString();

                if (string.IsNullOrWhiteSpace(hardwareInfo) || hardwareInfo.Length < 5)
                {
                    using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_PhysicalMedia"))
                        foreach (ManagementObject mo in searcher.Get()) hardwareInfo += mo["SerialNumber"]?.ToString();
                }
            }
            catch
            {
                // CORREÇÃO: Fallback único por PC em caso de falha do WMI
                hardwareInfo = Environment.MachineName + "_" + Environment.UserName;
            }

            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(hardwareInfo));
                return BitConverter.ToString(hash).Replace("-", "").Substring(0, 12).ToUpper();
            }
        }

        public static async Task<bool> VerificarAcesso()
        {
            string meuHwid = ObterHWID();
            string urlConsulta = $"{urlFirebase}whitelist/{meuHwid}.json";

            using (HttpClient client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(15);
                try
                {
                    string resposta = await client.GetStringAsync(urlConsulta);

                    // CORREÇÃO: Bloqueia nulos, vazios ou HTML de proxy/provedor
                    if (string.IsNullOrWhiteSpace(resposta) || resposta.Trim() == "null" || resposta.Trim().StartsWith("<"))
                        return false;

                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        public static string CriptografarSenha(string senha)
        {
            try
            {
                byte[] dados = Encoding.UTF8.GetBytes(senha);
                byte[] criptografado = ProtectedData.Protect(dados, null, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(criptografado);
            }
            catch { return ""; }
        }

        public static string DescriptografarSenha(string textoCriptografado)
        {
            try
            {
                byte[] dados = Convert.FromBase64String(textoCriptografado);
                byte[] descriptografado = ProtectedData.Unprotect(dados, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(descriptografado);
            }
            catch { return ""; }
        }
    }
}