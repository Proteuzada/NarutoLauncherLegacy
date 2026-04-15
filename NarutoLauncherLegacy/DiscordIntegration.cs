using DiscordRPC;
using System;

namespace NarutoLauncherLegacy
{
    public class DiscordIntegration
    {
        private DiscordRpcClient discordClient;
        public bool DiscordHabilitado { get; private set; }
        private readonly string clientId = "1493045476218507294";

        public DiscordIntegration(bool habilitadoInicial)
        {
            this.DiscordHabilitado = habilitadoInicial;
        }

        public void Inicializar()
        {
            if (!DiscordHabilitado) return;
            try
            {
                discordClient = new DiscordRpcClient(clientId);
                discordClient.Initialize();
                AtualizarPresenca("Aguardando Login...", "Menu Inicial");
            }
            catch { }
        }

        public void AtualizarPresenca(string jogador, string servidor)
        {
            if (DiscordHabilitado && discordClient != null && discordClient.IsInitialized)
            {
                string statusServer = servidor == "Menu Inicial" ? "Menu Inicial" : $"Server: S{servidor}";
                discordClient.SetPresence(new RichPresence()
                {
                    Details = $"Player: {jogador}",
                    State = statusServer,
                    Assets = new Assets() { LargeImageKey = "logo_naruto", LargeImageText = "Seventh Heaven Launcher" },
                    Timestamps = Timestamps.Now
                });
            }
        }

        public void AlternarPresenca(bool habilitar)
        {
            DiscordHabilitado = habilitar;
            if (DiscordHabilitado) Inicializar();
            else Desligar();
        }

        public void Desligar()
        {
            if (discordClient != null)
            {
                discordClient.Dispose();
                discordClient = null;
            }
        }
    }
}