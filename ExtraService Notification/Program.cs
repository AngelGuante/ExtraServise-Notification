using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ExtraService_Notification
{
    class Program
    {
        private static string _MAC { get; } = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(x => x.OperationalStatus == OperationalStatus.Up).GetPhysicalAddress().ToString();
        private static string _appPath { get; } = Application.ExecutablePath.ToString().Replace(@"\ExtraService Notification.exe", @"\");
        private static string _iconEnabled { get; } = @"\favicon.ico";
        private static string _iconDisabled { get; } = @"\faviconDisconect.ico";
        private static string _pathConfigFile { get; } = $@"{_appPath}\config.txt";
        private static NotifyIcon _icon { get; } = new NotifyIcon();
        public static ContextMenu menu { get; } = new ContextMenu();
        private static IntPtr _procces { get; } = Process.GetCurrentProcess().MainWindowHandle;
#if DEBUG
        private static string _serverUrl { get; } = "https://localhost:44392/API/";
#else
        private static string _serverUrl { get; } = "https://moniextra.com/API/";
#endif

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        static void Main()
        {
            ShowWindow(_procces, 0);

            try
            {
                #region MENU
                menu.MenuItems.Add("Iniciar/Detener Servicio", IniciarDetenerServicio);
                menu.MenuItems.Add("Habilitar equipo como servidor", HabilitarComoServidor);
                menu.MenuItems.Add("Ocultar icono", CerrarAplicacion);
                menu.MenuItems.Add("-");
                menu.MenuItems.Add("Resetear conexión", CloseApp);
                _icon.ContextMenu = menu;
                #endregion

                #region SERVICIO
                var sc = new ServiceController("ExtraService");
                _icon.Visible = true;
                if (sc.Status != ServiceControllerStatus.Running)
                {
                    sc.Start();
                    _icon.Icon = new System.Drawing.Icon($@"{_appPath}{_iconEnabled}");
                    BalloonTip("Servicio En Ejecución.", ToolTipIcon.None);
                }
                else
                {
                    _icon.Icon = new System.Drawing.Icon($@"{_appPath}{_iconDisabled}");
                    BalloonTip("Servicio Detenido", ToolTipIcon.None);
                }
                #endregion

                if (!File.Exists(_pathConfigFile))
                    using (var fs = File.Create(_pathConfigFile))
                    {
                        byte[] text = new UTF8Encoding(true).GetBytes("AllowStartupConection: N;\nempresa: --;\nuserPass: --\n");
                        fs.Write(text, 0, text.Length);
                    }
                else
                {
                    if (ReadFileOptionConfig("AllowStartupConection: ") == "AllowStartupConection: Y;")
                    {
                        Task.Run(async () =>
                        {
                            await AbrirConexionRemota();
                        });
                        menu.MenuItems[1].Text = "Deshabilitar equipo como servidor";
                    }
                }
                Application.Run();
            }
            catch (Exception)
            {
                BalloonTip("Algo ha ocurrido al momento de iniciar el ExtraService Notification.", ToolTipIcon.Error);
            }
        }

        private static async void HabilitarComoServidor(object sender, EventArgs e)
        {
            string password = null;

            #region VALIDAR LA CONTRASEÑA DEL PROGRAMADOR
            Console.Clear();
            ShowWindow(_procces, 1);
            Console.WriteLine("---------------------------------------------------------------------");
            Console.WriteLine("-ExtraService Notification Console - Ingrese la contraseña de acceso-");
            Console.WriteLine("---------------------------------------------------------------------");
            Console.Write("password: ");

            while (true)
            {
                var key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Enter)
                    break;
                password += key.KeyChar;
            }

            Console.Clear();
            Console.Write("Cargando...");

            using (var client = new HttpClient())
            using (var response = await client.GetAsync($"{_serverUrl}Login/ClientAppLogin?pass={password}"))
            using (var content = response.Content)
            {
                if (await content.ReadAsStringAsync() != "true")
                {
                    Console.Clear();
                    Console.WriteLine("Contraseña Incorrecta.");
                    Console.Write("Presione ENTER para continuar.");
                    Console.ReadLine();
                    ShowWindow(_procces, 0);
                    return;
                }
            }
            #endregion

            #region VALIDAR LOS CREDENCIALES DE LA EMPRESA
            if (ReadFileOptionConfig("userPass: ") == "userPass: --" && ReadFileOptionConfig("empresa: ") == "empresa: --;")
            {
                password = string.Empty;
                string empresa;
                Console.Clear();
                ShowWindow(_procces, 1);
                Console.WriteLine("------------------------------------------------------------------------------------------------------");
                Console.WriteLine("-ExtraService Notification Console - Ingrese sus credenciales para con los que accede a Moniextra.com-");
                Console.WriteLine("------------------------------------------------------------------------------------------------------");
                Console.Write("Identificador de empresa: ");
                empresa = Console.ReadLine();
                Console.Write("Contraseña de usuario: ");
                while (true)
                {
                    var key = Console.ReadKey(true);
                    if (key.Key == ConsoleKey.Enter)
                        break;
                    password += key.KeyChar;
                }

                Console.Clear();
                Console.WriteLine("-----------------------------------");
                Console.WriteLine("-ExtraService Notification Console-");
                Console.WriteLine("-----------------------------------");
                Console.WriteLine("Validando sus credenciales...");

                if (password == "123456abc!")
                {
                    Console.Clear();
                    Console.WriteLine("La contraseña ingresada es la de por defecto, debe ir a moniextra.com y cambiar su clave.");
                    Console.WriteLine("Presione ENTER para continuar..");
                    Console.ReadLine();
                    ShowWindow(_procces, 0);
                    return;
                }

                var content = new StringContent(
                    JsonConvert.SerializeObject(new
                    {
                        IdEmpresa = empresa,
                        Username = "Remoto",
                        Password = password,
                        mac = _MAC
                    })
                    , Encoding.UTF8
                    , "application/json"
                    );

                using (var client = new HttpClient())
                using (var response = await client.PostAsync($"{_serverUrl}Login/authenticate", content))
                    try
                    {
                        JsonConvert.DeserializeObject<JObject>(await response.Content.ReadAsStringAsync());
                    }
                    catch (Exception)
                    {
                        Console.Clear();
                        Console.WriteLine("Credenciales incorrectos o equipo registrado.");
                        Console.Write("Presione ENTER para continuar.");
                        Console.ReadLine();
                        ShowWindow(_procces, 0);
                        return;
                    }

                var newData = File.ReadAllText(_pathConfigFile);
                newData = newData.Replace("empresa: --;", $"empresa: {empresa};");
                newData = newData.Replace("userPass: --", $"userPass: {BCrypt.Net.BCrypt.HashPassword(password)}");

                File.WriteAllText(_pathConfigFile, newData);
            }
            #endregion

            ShowWindow(_procces, 0);

            if (menu.MenuItems[1].Text == "Habilitar equipo como servidor")
            {
                if (await AbrirConexionRemota())
                {
                    using (var fs = File.Open(_pathConfigFile, FileMode.Open))
                    {
                        var text = new UTF8Encoding(true).GetBytes("AllowStartupConection: Y;\n");
                        fs.Write(text, 0, text.Length);

                        BalloonTip("Login exitoso!", ToolTipIcon.Info);
                        menu.MenuItems[1].Text = "Deshabilitar equipo como servidor";
                        BalloonTip("Este equipo ahora servirá como servidor.", ToolTipIcon.Info);
                        return;
                    }
                }
            }
            else if (menu.MenuItems[1].Text == "Deshabilitar equipo como servidor")
            {
                if (await CerrarConexionRemota())
                {
                    using (var fs = File.Open(_pathConfigFile, FileMode.Open))
                    {
                        var text = new UTF8Encoding(true).GetBytes("AllowStartupConection: N;\n");
                        fs.Write(text, 0, text.Length);
                    }
                    BalloonTip("Este equipo ya no servirá como servidor.", ToolTipIcon.Info);
                    menu.MenuItems[1].Text = "Habilitar equipo como servidor";
                }
            }
        }

        private async static void CerrarAplicacion(object sender, EventArgs e)
        {
            if (
                MessageBox.Show("El Servicio ExtraService seguirá en ejecición pero se quitará el icono de la barra de tareas.",
                "Cerrar Tray Icon?",
                MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                await CerrarConexionRemota();
                Environment.Exit(0);
            }
        }

        private static void IniciarDetenerServicio(object sender, EventArgs e)
        {
            try
            {
                var sc = new ServiceController("ExtraService");
                if (sc.Status == ServiceControllerStatus.Running)
                {
                    sc.Stop();
                    _icon.Icon = new System.Drawing.Icon($@"{_appPath}{_iconDisabled}");
                    BalloonTip("Servicio Detenido.", ToolTipIcon.None);
                }
                else
                {
                    sc.Start();
                    _icon.Icon = new System.Drawing.Icon($@"{_appPath}{_iconEnabled}");
                    BalloonTip("Servicio Iniciado.", ToolTipIcon.None);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                Console.ReadLine();
                BalloonTip("Algo ha ocurrido, intentelo más tarde.", ToolTipIcon.Error);
            }
        }

        private static void BalloonTip(string text, ToolTipIcon ttIcon)
        {
            _icon.BalloonTipTitle = "Notification ExtraService";
            _icon.BalloonTipText = text;
            _icon.BalloonTipIcon = ttIcon;
            _icon.ShowBalloonTip(2000);
        }

        private static string ReadFileOptionConfig(string configOption)
        {
            var sd = string.Empty;
            using (var sr = File.OpenText(_pathConfigFile))
            {
                while ((sd = sr.ReadLine()) != null)
                    if (sd.StartsWith(configOption))
                        break;
            }
            return sd;
        }

        private async static Task<bool> AbrirConexionRemota()
        {
            var content = new StringContent(
                           JsonConvert.SerializeObject(new
                           {
                               IdEmpresa = ReadFileOptionConfig("empresa: ").Replace("empresa: ", "").Replace(";", ""),
                               Username = "Remoto",
                               Password = ReadFileOptionConfig("userPass: ").Replace("userPass: ", ""),
                               passwordEncriptado = true
                           })
                           , Encoding.UTF8
                           , "application/json"
                           );

            var res = string.Empty;
            using (var client = new HttpClient())
            using (var response = await client.PostAsync($"{_serverUrl}CONEXIONREMOTA/ESTABLECERSERVIDOR", content))
            {
                res = await response.Content.ReadAsStringAsync();
                if (res == "true")
                {
                    BalloonTip("Este servidor ahora esta online para que los clientes puedan usarlo para obtener la data.", ToolTipIcon.None);
                    return true;
                }
            }
            BalloonTip(res.Replace("\"", ""), ToolTipIcon.Error);
            return false;
        }

        private async static Task<bool> CerrarConexionRemota()
        {
            var content = new StringContent(
               JsonConvert.SerializeObject(new
               {
                   IdEmpresa = ReadFileOptionConfig("empresa: ").Replace("empresa: ", "").Replace(";", ""),
                   Username = "Remoto",
                   Password = ReadFileOptionConfig("userPass: ").Replace("userPass: ", ""),
                   passwordEncriptado = true
               })
               , Encoding.UTF8
               , "application/json"
               );

            var res = string.Empty;
            using (var client = new HttpClient())
            using (var response = await client.PostAsync($"{_serverUrl}CONEXIONREMOTA/CERRARSERVIDOR", content))
            {
                res = await response.Content.ReadAsStringAsync();
                if (res == "true")
                {
                    _icon.Visible = false;
                    return true;
                }
            }
            BalloonTip(res.Replace("\"", ""), ToolTipIcon.Error);
            return false;
        }

        static async void CloseApp(object sender, EventArgs e)
        {
            if (ReadFileOptionConfig("userPass: ") == "userPass: --" && ReadFileOptionConfig("empresa: ") == "empresa: --;")
            {
                BalloonTip("Débe habilitar el equipo como servidor para poder realizar esta acción.", ToolTipIcon.Error);
                return;
            }
            await CerrarConexionRemota();
            await AbrirConexionRemota();
        }
    }
}
