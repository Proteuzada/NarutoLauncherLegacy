using CefSharp;
using CefSharp.WinForms;
using System;
using System.IO;
using System.Windows.Forms;

namespace NarutoLauncherLegacy
{
    public class BrowserEngine
    {
        public ChromiumWebBrowser Browser { get; private set; }
        private readonly string sessionID;
        private readonly bool usarGPU;
        private readonly Action onFlashAcceptedCallback;
        private readonly Action onToggleMenuCallback;

        public BrowserEngine(string sessionId, bool gpuEnabled, Action onFlashAccepted, Action onToggleMenu)
        {
            this.sessionID = sessionId;
            this.usarGPU = gpuEnabled;
            this.onFlashAcceptedCallback = onFlashAccepted;
            this.onToggleMenuCallback = onToggleMenu;
        }

        public void InicializarCefSharp()
        {
            if (!Cef.IsInitialized)
            {
                var settings = new CefSettings();
                string flashPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pepflashplayer32.dll");

                // ==================================================================
                // CORREÇÃO 1: RootCachePath configurado para agrupar todas as contas
                // Resolve o erro "not a child of the root_cache_path"
                // ==================================================================
                settings.RootCachePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Sessoes");

                AddSafe(settings, "disable-web-security", "1");
                AddSafe(settings, "allow-running-insecure-content", "1");
                AddSafe(settings, "ignore-certificate-errors", "1");
                AddSafe(settings, "no-sandbox", "1");
                AddSafe(settings, "disable-site-isolation-trials", "1");

                // ==================================================================
                // CORREÇÃO 2: PluginPowerSaver inserido para impedir o bloqueio de
                // frames de Flash ocultos ou menores que 400x300 (Erro do Console)
                // ==================================================================
                AddSafe(settings, "disable-features", "IsolateOrigins,site-per-process,SameSiteByDefaultCookies,CookiesWithoutSameSiteMustBeSecure,PluginPowerSaver");

                if (!usarGPU)
                {
                    AddSafe(settings, "disable-gpu", "1");
                    AddSafe(settings, "disable-software-rasterizer", "1");
                }
                else
                {
                    AddSafe(settings, "ignore-gpu-blacklist", "1"); // Antigo
                    AddSafe(settings, "ignore-gpu-blocklist", "1"); // Novo suporte
                    AddSafe(settings, "enable-gpu-rasterization", "1");
                }

                AddSafe(settings, "ppapi-flash-path", flashPath);
                AddSafe(settings, "ppapi-flash-version", "32.0.0.465");
                AddSafe(settings, "plugin-policy", "allow");
                AddSafe(settings, "always-authorize-plugins", "1");
                AddSafe(settings, "run-all-flash-in-allow-mode", "1");

                settings.UserAgent = "Mozilla/5.0 (Windows NT 10.0; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/86.0.4240.198 Safari/537.36 Gamebox/3.4.7.0";

                Cef.Initialize(settings);
            }
        }

        public ChromiumWebBrowser CriarNavegador()
        {
            var reqContextSettings = new RequestContextSettings
            {
                CachePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Sessoes", sessionID),
                PersistSessionCookies = true
            };

            var reqContext = new RequestContext(reqContextSettings);

            Cef.UIThreadTaskFactory.StartNew(delegate {
                reqContext.SetPreference("profile.default_content_setting_values.plugins", 1, out _);
            });

            Browser = new ChromiumWebBrowser("about:blank") { RequestContext = reqContext, Dock = DockStyle.Fill };
            Browser.KeyboardHandler = new CefKeyboardHandler(onToggleMenuCallback);
            Browser.JavascriptObjectRepository.Settings.LegacyBindingEnabled = true;
            Browser.JavascriptObjectRepository.Register("bound", new OasisProxy(), isAsync: false);

            Browser.FrameLoadStart += OnFrameLoadStart;
            return Browser;
        }

        private void OnFrameLoadStart(object sender, FrameLoadStartEventArgs args)
        {
            if (args.Frame.IsMain)
            {
                string scriptJS = @"
                    try {
                        window.isMiniClient = true; 
                        window.gamebox = { version: '3.4.7.0', platform: 'oasis' }; 
                        if (typeof bound !== 'undefined') { window.external = bound; }
                        
                        document.addEventListener('DOMContentLoaded', function() {
                            try {
                                var p = document.querySelector('embed') || document.querySelector('object');
                                if(p) { p.style.display = 'block'; }
                                var box = document.querySelector('.flash-replace-box') || document.querySelector('#flash-palyer-check');
                                if(box) box.remove();
                            } catch(e2) {}
                        });
                    } catch(e) {}
                ";
                args.Frame.ExecuteJavaScriptAsync(scriptJS);
                onFlashAcceptedCallback?.Invoke();
            }
        }

        private void AddSafe(CefSettings settings, string key, string val)
        {
            if (!settings.CefCommandLineArgs.ContainsKey(key))
                settings.CefCommandLineArgs.Add(key, val);
        }
    }

    public class OasisProxy { public string getVersion() => "3.4.7.0"; public void CloseGame() => Application.Exit(); }

    public class CefKeyboardHandler : IKeyboardHandler
    {
        private Action alternarMenu;
        public CefKeyboardHandler(Action acao) { alternarMenu = acao; }
        public bool OnPreKeyEvent(IWebBrowser browserControl, IBrowser browser, KeyType type, int windowsKeyCode, int nativeKeyCode, CefEventFlags modifiers, bool isSystemKey, ref bool isKeyboardShortcut)
        {
            if (type == KeyType.RawKeyDown && windowsKeyCode == 0x09) { if (browserControl is Control control) control.BeginInvoke(alternarMenu); }
            if (type == KeyType.RawKeyDown && windowsKeyCode == 0x7B) { browserControl.ShowDevTools(); }
            if (type == KeyType.RawKeyDown && windowsKeyCode == 0x74) { browserControl.Reload(); }
            return false;
        }
        public bool OnKeyEvent(IWebBrowser browserControl, IBrowser browser, KeyType type, int windowsKeyCode, int nativeKeyCode, CefEventFlags modifiers, bool isSystemKey) => false;
    }
}