using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace NarutoLauncherLegacy
{
    public static class SecurityManager
    {
        private static readonly string urlFirebase = "https://seventh-94a18-default-rtdb.firebaseio.com/";
        private static readonly string caminhoKey = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "user_identity.dat");

        public static string ObterIdentidade()
        {
            if (File.Exists(caminhoKey))
            {
                try { return File.ReadAllText(caminhoKey).Trim().ToUpper(); }
                catch { }
            }

            string novaKey = GerarKeyAleatoria(12);

            try { File.WriteAllText(caminhoKey, novaKey); }
            catch { }

            return novaKey;
        }

        private static string GerarKeyAleatoria(int tamanho)
        {
            const string caracteres = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            StringBuilder res = new StringBuilder();
            using (RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider())
            {
                byte[] uintBuffer = new byte[4];
                while (res.Length < tamanho)
                {
                    rng.GetBytes(uintBuffer);
                    uint num = BitConverter.ToUInt32(uintBuffer, 0);
                    res.Append(caracteres[(int)(num % (uint)caracteres.Length)]);
                }
            }
            return res.ToString();
        }

        public static async Task<bool> VerificarAcesso()
        {
            string minhaKey = ObterIdentidade();
            string urlConsulta = $"{urlFirebase}whitelist/{minhaKey}.json";

            using (HttpClient client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(10);
                try
                {
                    var response = await client.GetAsync(urlConsulta);
                    if (!response.IsSuccessStatusCode) return false;

                    string resposta = await response.Content.ReadAsStringAsync();

                    if (string.IsNullOrWhiteSpace(resposta) || resposta.Trim().ToLower() == "null")
                        return false;

                    return true;
                }
                catch { return false; }
            }
        }

        public static string CriptografarSenha(string senha)
        {
            try
            {
                byte[] dados = Encoding.UTF8.GetBytes(senha);
                byte[] cripto = ProtectedData.Protect(dados, null, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(cripto);
            }
            catch { return ""; }
        }

        public static string DescriptografarSenha(string texto)
        {
            try
            {
                byte[] dados = Convert.FromBase64String(texto);
                byte[] desc = ProtectedData.Unprotect(dados, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(desc);
            }
            catch { return ""; }
        }

        public static string ObterPermissoesSandbox() => "allow-scripts allow-same-origin allow-forms allow-popups allow-modals";
    }
}