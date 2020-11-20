using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

using System.Security.Permissions;
using System.Management;
using Microsoft.Win32;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;

namespace EV_Toolbox_Crack
{
    public partial class Form1 : Form
    {
        /// <summary>
        /// Представляет сетевой интерфейс Windows. Обертка вокруг .NET API для сетевых интерфейсов,
        /// а также для неуправляемого устройства.
        /// </summary>
        public class Adapter
        {
            public ManagementObject adapter;
            public string adaptername;
            public string customname;
            public int devnum;

            public Adapter(ManagementObject a, string aname, string cname, int n)
            {
                this.adapter = a;
                this.adaptername = aname;
                this.customname = cname;
                this.devnum = n;
            }

            public Adapter(NetworkInterface i) : this(i.Description) { }

            public Adapter(string aname)
            {
                this.adaptername = aname;

                var searcher = new ManagementObjectSearcher("select * from win32_networkadapter where Name='" + adaptername + "'");
                var found = searcher.Get();
                this.adapter = found.Cast<ManagementObject>().FirstOrDefault();

                // Извлечь номер адаптера; это должно соответствовать клавишам под
                // HKEY_LOCAL_MACHINE\SYSTEM\ControlSet001\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}
                try
                {
                    var match = Regex.Match(adapter.Path.RelativePath, "\\\"(\\d+)\\\"$");
                    this.devnum = int.Parse(match.Groups[1].Value);
                }
                catch
                {
                    return;
                }

                // Найдите имя, которое дал ему пользователь, в разделе «Сетевые адаптеры».
                this.customname = NetworkInterface.GetAllNetworkInterfaces().Where(
                    i => i.Description == adaptername
                ).Select(
                    i => " (" + i.Name + ")"
                ).FirstOrDefault();
            }

            /// <summary>
            /// Получите управляемый адаптер .NET.
            /// </summary>
            public NetworkInterface ManagedAdapter
            {
                get
                {
                    return NetworkInterface.GetAllNetworkInterfaces().Where(
                        nic => nic.Description == this.adaptername
                    ).FirstOrDefault();
                }
            }


            // Форматировать MAC-адрес
            public string formatMAC(string mac)
            {

                return mac.Replace("-", "").ToUpper();

            }

            /// <summary>
            /// Получите MAC-адрес, сообщенный адаптером.
            /// </summary>
            public string Mac
            {
                get
                {
                    try
                    {
                        return this.formatMAC(BitConverter.ToString(this.ManagedAdapter.GetPhysicalAddress().GetAddressBytes()));
                    }
                    catch { return null; }
                }
            }

            /// <summary>
            /// Получите ключ реестра, связанный с этим адаптером.
            /// </summary>
            public string RegistryKey
            {
                get
                {
                    return String.Format(@"SYSTEM\ControlSet001\Control\Class\{{4D36E972-E325-11CE-BFC1-08002BE10318}}\{0:D4}", this.devnum);
                }
            }

            /// <summary>
            /// Получите значение реестра NetworkAddress для этого адаптера.
            /// </summary>
            public string RegistryMac
            {
                get
                {
                    try
                    {
                        using (RegistryKey regkey = Registry.LocalMachine.OpenSubKey(this.RegistryKey, RegistryKeyPermissionCheck.ReadWriteSubTree))
                        {
                            return this.formatMAC(regkey.GetValue("NetworkAddress").ToString());
                        }
                    }
                    catch
                    {
                        return null;
                    }
                }
            }

            /// <summary>
            /// Устанавливает значение реестра NetworkAddress для этого адаптера.
            /// </summary>
            /// <param name="value">Значение. Должен быть ЛИБО строкой из 12 шестнадцатеричных цифр в верхнем регистре без тире, точек или чего-либо еще, ИЛИ пустой строкой (очищает значение реестра).</param>
            /// <returns>true в случае успеха, false в противном случае</returns>
            public bool SetRegistryMac(string value)
            {
                bool shouldReenable = false;

                try
                {
                    // Если значение не является пустой строкой, мы хотим установить для него NetworkAddress,
                    // чтобы оно было действительным.
                    if (value.Length > 0 && !Adapter.IsValidMac(value))
                        throw new Exception(value + " не действительный MAC-адрес");

                    using (RegistryKey regkey = Registry.LocalMachine.OpenSubKey(this.RegistryKey, RegistryKeyPermissionCheck.ReadWriteSubTree))
                    {
                        if (regkey == null)
                            throw new Exception("Не удалось открыть раздел реестра");


                        // Спросите, действительно ли мы хотим это сделать
                        string question = value.Length > 0 ?
                            "Изменение MAC-адреса адаптера {0} с {1} на {2}. Продолжить?" :
                            "Очистка пользовательского MAC-адреса адаптера {0}. Продолжить?";
                        DialogResult proceed = MessageBox.Show(
                            String.Format(question, this.ToString(), this.Mac, value),
                            "Сменить MAC-адрес?", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                        if (proceed != DialogResult.Yes)
                            return false;

                        // Попытка отключить адаптер
                        var result = (uint)adapter.InvokeMethod("Disable", null);
                        if (result != 0)
                            throw new Exception("Не удалось отключить сетевой адаптер.");

                        // Если мы здесь, адаптер был отключен, поэтому мы устанавливаем флаг, который снова включит его в блоке finally
                        shouldReenable = true;

                        // Если мы здесь, все в порядке; обновить или очистить значение реестра
                        if (value.Length > 0)
                            regkey.SetValue("NetworkAddress", value, RegistryValueKind.String);
                        else
                            regkey.DeleteValue("NetworkAddress");


                        return true;
                    }
                }

                catch (Exception ex)
                {
                    MessageBox.Show(ex.ToString());
                    return false;
                }

                finally
                {
                    if (shouldReenable)
                    {
                        uint result = (uint)adapter.InvokeMethod("Enable", null);
                        if (result != 0)
                            MessageBox.Show("Не удалось повторно включить сетевой адаптер.");
                    }
                }
            }

            public override string ToString()
            {
                return this.adaptername + this.customname;
            }

            /// <summary>
            /// Получите случайный (локально управляемый) MAC-адрес.
            /// </summary>
            /// <returns>MAC-адрес, имеющий 01 как младшие биты первого байта, но в остальном случайный.</returns>
            public static string GetNewMac()
            {
                Random r = new Random();

                byte[] bytes = new byte[6];
                r.NextBytes(bytes);

                // Установите второй бит в 1
                bytes[0] = (byte)(bytes[0] | 0x02);
                // Установите первый бит в 0
                bytes[0] = (byte)(bytes[0] & 0xfe);

                return MacToString(bytes);
            }

            /// <summary>
            /// Проверяет, является ли данная строка допустимым MAC-адресом.
            /// </summary>
            /// <param name="mac">Строка.</param>
            /// <param name="actual">false, если адрес является локально управляемым, в противном случае - true.</param>
            /// <returns>Значение true, если строка является допустимым MAC-адресом, в противном случае - false.</returns>
            public static bool IsValidMac(string mac)
            {
                // 6 bytes == 12 hex символов (без тире / точек / всего остального)
                if (mac.Length != 12)
                    return false;

                // Должен быть в верхнем регистре
                if (mac != mac.ToUpper())
                    return false;

                // Не должно содержать ничего, кроме шестнадцатеричных цифр
                if (!Regex.IsMatch(mac, "^[0-9A-F]*$"))
                    return false;

                // Если мы здесь, то вторым символом должно быть 2, 6, A или E.
                /* char c = mac[1];
                 return (c == '2' || c == '6' || c == 'A' || c == 'E');*/
                return true;
            }

            /// <summary>
            /// Проверяет действительность данного MAC-адреса.
            /// </summary>
            /// <param name="mac">Адрес.</param>
            /// <param name="actual">false, если адрес является локально управляемым, в противном случае - true.</param>
            /// <returns>true, если действительна, в противном случае - false.</returns>
            public static bool IsValidMac(byte[] bytes)
            {
                return IsValidMac(Adapter.MacToString(bytes));
            }

            /// <summary>
            /// Преобразует массив байтов длиной 6 в MAC-адрес (т.е. строку шестнадцатеричных цифр).
            /// </summary>
            /// <param name="bytes">Байты для преобразования.</param>
            /// <returns>MAC-адрес.</returns>
            public static string MacToString(byte[] bytes)
            {
                return BitConverter.ToString(bytes).Replace("-", "").ToUpper();
            }
        }

        public Form1()
        {
            InitializeComponent();

            // Повышение привилегий на запись в HKLM.
            new RegistryPermission(PermissionState.Unrestricted).Assert();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            /* Windows обычно добавляет несколько нефизических устройств,
             * адреса которых мы не хотели бы менять.
             * Большинство из них имеют невозможный MAC-адрес. */
            foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces().Where(
                    a => Adapter.IsValidMac(a.GetPhysicalAddress().GetAddressBytes())
                ).OrderByDescending(a => a.Speed))
            {
                AdaptersComboBox.Items.Add(new Adapter(adapter));
            }

            AdaptersComboBox.SelectedIndex = 0;
        }

        /// <summary>
        /// Обновите пользовательский интерфейс, чтобы отобразить текущие адреса.
        /// </summary>
        private void UpdateAddresses()
        {
            Adapter a = AdaptersComboBox.SelectedItem as Adapter;
            this.CurrentMacTextBox.Text = a.RegistryMac;
            this.ActualMacLabel.Text = a.Mac;
        }

        private void AdaptersComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateAddresses();
        }

        private void RandomButton_Click(object sender, EventArgs e)
        {
            CurrentMacTextBox.Text = Adapter.GetNewMac();
        }

        private void UpdateButton_Click(object sender, EventArgs e)
        {
            if (!Adapter.IsValidMac(CurrentMacTextBox.Text))
            {
                MessageBox.Show("Введенный MAC-адрес недействителен; обновляться не будет.", "Указан неверный MAC-адрес", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            SetRegistryMac(CurrentMacTextBox.Text);
        }

        private void ClearButton_Click(object sender, EventArgs e)
        {
            SetRegistryMac("");
        }

        /// <summary>
        /// Установите для адреса выбранного адаптера заданное значение и обновите пользовательский интерфейс.
        /// </summary>
        /// <param name="mac">MAC-адрес, который нужно установить.</param>
        private void SetRegistryMac(string mac)
        {
            // отключить "кнопку обновления"
            UpdateButton.Enabled = false;
            progressBar.Visible = true;
            progressBar.Value = 1;

            Adapter a = AdaptersComboBox.SelectedItem as Adapter;

            if (a.SetRegistryMac(mac))
            {
                System.Threading.Thread.Sleep(100);
                UpdateAddresses();
                progressBar.Value = 100;
                MessageBox.Show("Выполнено!", "Успешно", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            // повторно включить "кнопку обновления"
            UpdateButton.Enabled = true;
            progressBar.Visible = false;
        }

        private void RereadButton_Click(object sender, EventArgs e)
        {
            UpdateAddresses();
        }

        private void CurrentMacTextBox_TextChanged(object sender, EventArgs e)
        {
            this.UpdateButton.Enabled = Adapter.IsValidMac(this.CurrentMacTextBox.Text);
        }
    }
}
