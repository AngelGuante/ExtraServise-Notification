using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ExtraService_Notification
{
    class Program
    {
        private static bool _conexionPerdida { get; set; } = false;
        private static string _ipPublicaEnMemoria { get; set; } = string.Empty;
        private static string _MAC { get; } = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(x => x.OperationalStatus == OperationalStatus.Up).GetPhysicalAddress().ToString();
        private static string _appPath { get; } = Application.ExecutablePath.ToString().Replace(@"\ExtraService Notification.exe", @"\");
        private static string _iconEnabled { get; } = @"\favicon.ico";
        private static string _iconDisabled { get; } = @"\faviconDisconect.ico";
        private static string _pathConfigFile { get; } = $@"{_appPath}\config.txt";
        private static NotifyIcon _icon { get; } = new NotifyIcon();
        public static ContextMenu menu { get; } = new ContextMenu();
        private static IntPtr _procces { get; } = Process.GetCurrentProcess().MainWindowHandle;
        //private static string _serverUrl { get; } = "https://localhost:44392/API/";
        //private static string _websocketUrl { get; } = "https://localhost:5001/api/SendDataClient/";
        private static string _serverUrl { get; } = "https://moniextra.com/API/";
        private static string _websocketUrl { get; } = "https://monicawebsocketserver.azurewebsites.net/api/SendDataClient/";

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
                menu.MenuItems.Add("-");
                menu.MenuItems.Add("Cerrar conexión", CerrarAplicacion);
                menu.MenuItems.Add("Resetear conexión", CloseApp);
                _icon.ContextMenu = menu;
                #endregion

                #region SERVICIO
                var sc = new ServiceController("ExtraService");
                _icon.Visible = true;
                if (sc.Status != ServiceControllerStatus.Running)
                    sc.Start();
                if (sc.Status == ServiceControllerStatus.Running)
                {
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
                        byte[] text = new UTF8Encoding(true).GetBytes($"AllowStartupConection: N;\nempresa: --;\nipPublica: --;\nuserPass: --\n");
                        fs.Write(text, 0, text.Length);
                    }
                else
                {
                    if (ReadFileOptionConfig("AllowStartupConection: ") == "AllowStartupConection: Y;")
                        menu.MenuItems[1].Text = "Deshabilitar equipo como servidor";
                }

                Task.Run(async () =>
                {
                    await AbrirConexionRemota();
                    while (true)
                    {
                        await Task.Delay(10000);
                        await ComprobarIpPublica();
                    }
                });

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
            if (ReadFileOptionConfig("userPass: ") == "--" && ReadFileOptionConfig("empresa: ") == "--")
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
                MessageBox.Show("El Servicio ExtraServicese va a detener.",
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
            catch (Exception)
            {
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
            if (sd != null)
                return sd.Replace(configOption, "").Replace(";", "");
            else
            {
                var sw = File.AppendText(_pathConfigFile);
                sw.Write($"\n{configOption}--;");
                sw.Flush();
                return "--";
            }
        }

        private async static Task<bool> AbrirConexionRemota()
        {
            if (ReadFileOptionConfig("userPass: ") != "--" && ReadFileOptionConfig("empresa: ") != "--")
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
                        _icon.Visible = true;
                        BalloonTip("Este servidor ahora esta online para que los clientes puedan usarlo para obtener la data.", ToolTipIcon.None);
                        return true;
                    }
                }
                BalloonTip(res.Replace("\"", ""), ToolTipIcon.Error);
            }
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

                    try
                    {
                        var sc = new ServiceController("ExtraService");
                        if (sc.Status == ServiceControllerStatus.Running)
                        {
                            sc.Stop();
                            await Task.Delay(20);
                        }
                    }
                    catch (Exception)
                    { }

                    return true;
                }
            }
            BalloonTip(res.Replace("\"", ""), ToolTipIcon.Error);
            return false;
        }

        private async static Task ComprobarIpPublica()
        {
            try
            {
                #region OBTENER LA IP PUBLICA
                string address = string.Empty;

                try
                {
                    WebRequest request = WebRequest.Create("http://checkip.dyndns.org/");
                    using (WebResponse response = request.GetResponse())
                    using (StreamReader stream = new StreamReader(response.GetResponseStream()))
                        address = stream.ReadToEnd();
                    if (_conexionPerdida)
                    {
                        _conexionPerdida = false;
                        var sc = new ServiceController("ExtraService");
                        if (sc.Status == ServiceControllerStatus.Stopped)
                            sc.Start();
                    }

                }
                catch (Exception)
                {
                    if (!_conexionPerdida)
                    {
                        _conexionPerdida = true;
                        var sc = new ServiceController("ExtraService");
                        if (sc.Status == ServiceControllerStatus.Running)
                        {
                            sc.Stop();
                            sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromMilliseconds(20000));
                        }
                    }
                }
                #endregion

                if (!_conexionPerdida && address != string.Empty)
                {
                    #region VALIDAR LA IP PUBLICA CON LA ALMACENADA EN LOCAL
                    var rg = new Regex("([0-9]{1,3}[.]?){1,4}");
                    var ipPublica = rg.Matches(address)[0].ToString();

                    if (ipPublica.Split(new char[] { '.' }, StringSplitOptions.None).Length == 4)
                    {
                        if (_ipPublicaEnMemoria == string.Empty)
                            _ipPublicaEnMemoria = ReadFileOptionConfig("ipPublica: ");

                        if (ipPublica != _ipPublicaEnMemoria)
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

                            var sc = new ServiceController("ExtraService");
                            if (sc.Status == ServiceControllerStatus.Running)
                            {
                                sc.Stop();
                                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromMilliseconds(20000));
                                sc.Start();
                            }
                            else
                                sc.Start();

                            using (var client = new HttpClient())
                            using (await client.PostAsync($"{_serverUrl}CONEXIONREMOTA/CAMBIARIP", content)) { }

                            var newData = File.ReadAllText(_pathConfigFile);
                            newData = newData.Replace($"ipPublica: {_ipPublicaEnMemoria};", $"ipPublica: {ipPublica};");
                            File.WriteAllText(_pathConfigFile, newData);

                            _ipPublicaEnMemoria = ipPublica;
                        }
                    }
                    #endregion

                    #region Validar la IP en el servidor de websocket
                    WebRequest request = WebRequest.Create($"{_websocketUrl}GetClients");
                    using (WebResponse response = request.GetResponse())
                    using (StreamReader stream = new StreamReader(response.GetResponseStream()))
                    {
                        var IPS = stream.ReadToEnd();
                        if (IPS.IndexOf(_ipPublicaEnMemoria) == -1)
                        {
                            var sc = new ServiceController("ExtraService");
                            if (sc.Status == ServiceControllerStatus.Running)
                            {
                                sc.Stop();
                                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromMilliseconds(20000));
                                Thread.Sleep(5000);
                                sc.Start();
                            }
                            else
                                sc.Start();
                        }
                    }
                    #endregion
                }
            }
            catch (Exception)
            { }
        }

        static async void CloseApp(object sender, EventArgs e)
        {
            if (ReadFileOptionConfig("userPass: ") == "userPass: --" && ReadFileOptionConfig("empresa: ") == "empresa: --;")
            {
                BalloonTip("Débe habilitar el equipo como servidor para poder realizar esta acción.", ToolTipIcon.Error);
                return;
            }

            try
            {
                var sc = new ServiceController("ExtraService");
                if (sc.Status == ServiceControllerStatus.Running)
                {
                    sc.Stop();
                    await Task.Delay(20);
                }
            }
            catch (Exception)
            { }

            await CerrarConexionRemota();
            await AbrirConexionRemota();
        }
    }
}