using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.Media;
using Avalonia.Styling;
using Microsoft.Data.Sqlite;
using QuestPDF.Fluent;
using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace plc_data_reader_cross_app
{
    public partial class MainWindow : Window
    {
        private List<string> liveLogs = new List<string>();
        private List<string> alarmLogs = new List<string>();
        private List<LogRecordModel> lastQueriedRecords = new List<LogRecordModel>();
        private bool isDarkMode = true;
        private bool isForceClosing = false;
        private string loggedInUser = "admin";
        private string loggedInRole = "Admin";

        private Dictionary<string, int> hourlyTotalsCache = new Dictionary<string, int>();

        public MainWindow() : this("admin", "Admin")
        {
        }

        public MainWindow(string username, string role)
        {
            InitializeComponent();
            Program.MainWindowInstance = this;
            
            loggedInUser = username;
            loggedInRole = role;

            if (TxtUserRoleDisplay != null)
            {
                TxtUserRoleDisplay.Text = $"{loggedInUser} ({loggedInRole})";
            }

            if (loggedInRole == "Admin" && BtnNavAdmin != null)
            {
                BtnNavAdmin.IsVisible = true;
            }
            
            PopulateFilterComboBoxes();
            LoadPlcDeviceDropdown();
            RefreshDeleteDropdown();
            RefreshAlarmDeviceDropdown();
            PopulateTrendComboBoxes();
            PopulateExcelReportControls();
            RefreshAdminPanelData();

            if (ListColMonth != null) ListColMonth.SelectionChanged += OnSelectedMonthChanged;
            if (ListColWeek != null) ListColWeek.SelectionChanged += OnSelectedWeekChanged;
            if (ListColDay != null) ListColDay.SelectionChanged += OnSelectedDayChanged;
            if (CmbYear != null) CmbYear.SelectionChanged += OnQueryArchiveAutoTrigger;
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            if (isForceClosing)
            {
                base.OnClosing(e);
                return;
            }

            e.Cancel = true;
            if (CloseAuthOverlay != null)
            {
                CloseAuthOverlay.IsVisible = true;
                if (TxtClosePassword != null)
                {
                    TxtClosePassword.Text = "";
                    TxtClosePassword.Focus();
                }
                if (TxtCloseError != null) TxtCloseError.Text = "";
            }
        }

        private void OnCancelCloseClicked(object? sender, RoutedEventArgs e)
        {
            if (CloseAuthOverlay != null)
            {
                CloseAuthOverlay.IsVisible = false;
            }
        }

        private void OnConfirmCloseClicked(object? sender, RoutedEventArgs e)
        {
            string pass = TxtClosePassword?.Text ?? "";
            if (Program.VerifyAdminPassword(pass))
            {
                Program.LogUserActivity(loggedInUser, "Kapatma", "Uygulama yönetici onayıyla kapatıldı.");
                isForceClosing = true;
                Close();
            }
            else
            {
                if (TxtCloseError != null) TxtCloseError.Text = "❌ Hatalı şifre!";
                if (TxtClosePassword != null) TxtClosePassword.Text = "";
            }
        }

        private void OnThemeToggleClicked(object? sender, RoutedEventArgs e)
        {
            isDarkMode = !isDarkMode;

            if (Application.Current != null)
            {
                Application.Current.RequestedThemeVariant = isDarkMode ? ThemeVariant.Dark : ThemeVariant.Light;
            }

            if (BtnThemeToggle != null)
            {
                BtnThemeToggle.Content = isDarkMode ? "☀️ Açık Tema" : "🌙 Koyu Tema";
            }
        }

        private void OnSupabaseLinkClicked(object? sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            try
            {
                string url = "[https://supabase.com/dashboard/project/uzhysodwllhgoyoytyed](https://supabase.com/dashboard/project/uzhysodwllhgoyoytyed)";
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                if (TxtSettingsStatus != null)
                {
                    TxtSettingsStatus.Text = "Tarayıcı Açılamadı: " + ex.Message;
                }
            }
        }

        private void PopulateFilterComboBoxes()
        {
            if (CmbYear != null)
            {
                CmbYear.Items.Clear();
                for (int y = 2025; y <= 2030; y++) CmbYear.Items.Add(y);
                CmbYear.SelectedItem = DateTime.Now.Year;
            }

            if (CmbMonth != null) { CmbMonth.IsVisible = false; }
            if (CmbDay != null) { CmbDay.IsVisible = false; }
            if (CmbReportPeriod != null) { CmbReportPeriod.IsVisible = false; }
        }

        private void PopulateTrendComboBoxes()
        {
            if (CmbChartType != null)
            {
                CmbChartType.Items.Add("Çizgi Grafik (Line)");
                CmbChartType.Items.Add("Alan Grafik (Area)");
                CmbChartType.Items.Add("Çoklu Seri Kıyas");
                CmbChartType.SelectedItem = "Çoklu Seri Kıyas";
            }

            if (TrendSeriesContainer != null && TrendSeriesContainer.Children.Count > 0)
            {
                if (TrendSeriesContainer.Children[0] is Border firstBorder && firstBorder.Child is WrapPanel wrap)
                {
                    foreach (var child in wrap.Children)
                    {
                        if (child is ComboBox cmb)
                        {
                            string tag = cmb.Tag?.ToString() ?? "";
                            if (tag == "Device") FillDeviceComboBox(cmb);
                            else if (tag == "Year")
                            {
                                for (int y = 2025; y <= 2030; y++) cmb.Items.Add(y);
                                cmb.SelectedItem = DateTime.Now.Year;
                            }
                            else if (tag == "Month")
                            {
                                for (int m = 1; m <= 12; m++) cmb.Items.Add(m);
                                cmb.SelectedItem = DateTime.Now.Month;
                            }
                            else if (tag == "Day")
                            {
                                for (int d = 1; d <= 31; d++) cmb.Items.Add(d);
                                cmb.SelectedItem = DateTime.Now.Day;
                            }
                        }
                    }
                }
            }
        }

        private void OnAddTrendSeriesClicked(object? sender, RoutedEventArgs e)
        {
            if (TrendSeriesContainer == null) return;

            int seriesCount = TrendSeriesContainer.Children.Count + 1;

            IBrush borderBg = (this.TryFindResource("CardAltBgColor", out var bgRes) && bgRes is IBrush bgBrush)
                ? bgBrush
                : new SolidColorBrush(Color.Parse("#141A2C"));
            IBrush borderLine = (this.TryFindResource("BorderColor", out var lineRes) && lineRes is IBrush lineBrush)
                ? lineBrush
                : new SolidColorBrush(Color.Parse("#2B3454"));

            var border = new Border
            {
                Background = borderBg,
                Padding = new Thickness(8),
                CornerRadius = new CornerRadius(6),
                BorderBrush = borderLine,
                BorderThickness = new Thickness(1)
            };

            var wrap = new WrapPanel { Orientation = Avalonia.Layout.Orientation.Horizontal };

            wrap.Children.Add(new TextBlock { Text = $"Seri {seriesCount} -> Cihaz:", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0), FontSize = 11, Foreground = Brushes.Gray });
            
            var cmbDev = new ComboBox { Width = 140, Margin = new Thickness(0, 0, 10, 0), FontSize = 11, Tag = "Device" };
            FillDeviceComboBox(cmbDev);
            wrap.Children.Add(cmbDev);

            wrap.Children.Add(new TextBlock { Text = "Yıl:", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0), FontSize = 11, Foreground = Brushes.Gray });
            var cmbYear = new ComboBox { Width = 75, Margin = new Thickness(0, 0, 10, 0), FontSize = 11, Tag = "Year" };
            for (int y = 2025; y <= 2030; y++) cmbYear.Items.Add(y);
            cmbYear.SelectedItem = DateTime.Now.Year;
            wrap.Children.Add(cmbYear);

            wrap.Children.Add(new TextBlock { Text = "Ay:", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0), FontSize = 11, Foreground = Brushes.Gray });
            var cmbMonth = new ComboBox { Width = 75, Margin = new Thickness(0, 0, 10, 0), FontSize = 11, Tag = "Month" };
            for (int m = 1; m <= 12; m++) cmbMonth.Items.Add(m);
            cmbMonth.SelectedItem = DateTime.Now.Month;
            wrap.Children.Add(cmbMonth);

            wrap.Children.Add(new TextBlock { Text = "Gün:", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0), FontSize = 11, Foreground = Brushes.Gray });
            var cmbDay = new ComboBox { Width = 60, Margin = new Thickness(0, 0, 10, 0), FontSize = 11, Tag = "Day" };
            for (int d = 1; d <= 31; d++) cmbDay.Items.Add(d);
            cmbDay.SelectedItem = DateTime.Now.Day;
            wrap.Children.Add(cmbDay);

            border.Child = wrap;
            TrendSeriesContainer.Children.Add(border);
        }

        private void FillDeviceComboBox(ComboBox cmb)
        {
            try
            {
                var devices = Program.GetActivePlcDevices();
                var list = devices.Select(d => $"{d.PlcName} (DB{d.DbNumber})").ToList();
                cmb.ItemsSource = list;
                if (list.Count > 0) cmb.SelectedIndex = 0;
            }
            catch { }
        }

        private void LoadPlcDeviceDropdown()
        {
            try
            {
                var devices = Program.GetActivePlcDevices();
                if (CmbActiveDevices != null)
                {
                    var displayList = devices.Select(d => $"{d.PlcName} (DB{d.DbNumber})").ToList();
                    CmbActiveDevices.ItemsSource = displayList;

                    if (displayList.Count > 0)
                        CmbActiveDevices.SelectedIndex = 0;
                }
            }
            catch { }
        }

        private void RefreshDeleteDropdown()
        {
            try
            {
                var devices = Program.GetActivePlcDevices();
                if (CmbDevicesToDelete != null)
                {
                    var deleteList = devices.Select(d => $"ID: {d.Id} - {d.PlcName} (DB{d.DbNumber})").ToList();
                    CmbDevicesToDelete.ItemsSource = deleteList;
                    if (deleteList.Count > 0) CmbDevicesToDelete.SelectedIndex = 0;
                }
            }
            catch { }
        }

        private void RefreshAlarmDeviceDropdown()
        {
            try
            {
                var devices = Program.GetActivePlcDevices();
                if (CmbAlarmDevice != null)
                {
                    var list = devices.Select(d => $"ID: {d.Id} - {d.PlcName} (DB{d.DbNumber})").ToList();
                    CmbAlarmDevice.ItemsSource = list;
                    if (list.Count > 0) CmbAlarmDevice.SelectedIndex = 0;
                }
            }
            catch { }
        }

        private void RefreshAdminPanelData()
        {
            try
            {
                var users = Program.GetAllUsers();
                if (CmbUsersList != null)
                {
                    CmbUsersList.ItemsSource = users.Select(u => $"{u.Id} - {u.Username} ({u.Role})").ToList();
                    if (users.Count > 0) CmbUsersList.SelectedIndex = 0;
                }

                var activities = Program.GetAllActivities();
                if (ActivityLogListBox != null)
                {
                    ActivityLogListBox.ItemsSource = activities.Select(a => $"[{a.ActionTime}] {a.Username} -> {a.ActionType}: {a.Details}").ToList();
                }
            }
            catch { }
        }

        private void OnCreateNewUserClicked(object? sender, RoutedEventArgs e)
        {
            string adminPass = TxtNewUserAdminPass?.Text ?? "";
            string newName = TxtNewUsername?.Text ?? "";
            string newPass = TxtNewUserPass?.Text ?? "";
            string role = (CmbNewUserRole?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Operator";

            if (string.IsNullOrEmpty(newName) || string.IsNullOrEmpty(newPass))
            {
                if (TxtAdminStatus != null) TxtAdminStatus.Text = "⚠️ Kullanıcı adı ve şifre boş bırakılamaz.";
                return;
            }

            bool success = Program.RegisterUser(adminPass, newName, newPass, role);
            if (success)
            {
                Program.LogUserActivity(loggedInUser, "Kullanıcı Oluşturma", $"Yeni kullanıcı eklendi: {newName} ({role})");
                if (TxtAdminStatus != null) TxtAdminStatus.Text = $"✅ Kullanıcı başarıyla oluşturuldu: {newName}";
                if (TxtNewUsername != null) TxtNewUsername.Text = "";
                if (TxtNewUserPass != null) TxtNewUserPass.Text = "";
                if (TxtNewUserAdminPass != null) TxtNewUserAdminPass.Text = "";
                RefreshAdminPanelData();
            }
            else
            {
                if (TxtAdminStatus != null) TxtAdminStatus.Text = "❌ Hatalı admin şifresi veya kullanıcı adı zaten var.";
            }
        }

        private void OnUpdateUserPassClicked(object? sender, RoutedEventArgs e)
        {
            string adminPass = TxtNewUserAdminPass?.Text ?? "";
            string selectedUserText = CmbUsersList?.SelectedItem?.ToString() ?? "";
            string newPass = TxtUpdateUserPass?.Text ?? "";

            if (string.IsNullOrEmpty(selectedUserText) || string.IsNullOrEmpty(newPass))
            {
                if (TxtAdminStatus != null) TxtAdminStatus.Text = "⚠️ Kullanıcı seçin ve yeni şifre girin.";
                return;
            }

            try
            {
                string idPart = selectedUserText.Split('-')[0].Trim();
                if (int.TryParse(idPart, out int userId))
                {
                    bool success = Program.UpdateUserPassword(adminPass, userId, newPass);
                    if (success)
                    {
                        Program.LogUserActivity(loggedInUser, "Şifre Güncelleme", $"Kullanıcı ID {userId} şifresi değiştirildi.");
                        if (TxtAdminStatus != null) TxtAdminStatus.Text = "✅ Kullanıcı şifresi güncellendi.";
                        if (TxtUpdateUserPass != null) TxtUpdateUserPass.Text = "";
                        RefreshAdminPanelData();
                    }
                    else
                    {
                        if (TxtAdminStatus != null) TxtAdminStatus.Text = "❌ Hatalı admin şifresi!";
                    }
                }
            }
            catch (Exception ex)
            {
                if (TxtAdminStatus != null) TxtAdminStatus.Text = "Hata: " + ex.Message;
            }
        }
        private void OnLogoutClicked(object? sender, RoutedEventArgs e)
        {
            Program.LogUserActivity(loggedInUser, "Çıkış Yapma", "Kullanıcı oturumu kapatarak giriş ekranına döndü.");
            
            var loginWin = new LoginWindow();
            loginWin.Show();
            
            isForceClosing = true;
            Close();
        }

        private void OnAcknowledgeAlarmClicked(object? sender, RoutedEventArgs e)
        {
            Program.LogUserActivity(loggedInUser, "Alarm Tepkisi", "Aktif alarmlar incelendi ve onaylandı/müdahale edildi.");
            if (TxtAlarmStatus != null) TxtAlarmStatus.Text = "✅ Alarmlara başarıyla tepki verildi ve loglandı.";
        }

        private void OnSaveAlarmThresholdsClicked(object? sender, RoutedEventArgs e)
        {
            string adminPass = TxtAdminPassAlarm?.Text ?? "";
            string selectedDevText = CmbAlarmDevice?.SelectedItem?.ToString() ?? "";

            if (string.IsNullOrEmpty(selectedDevText))
            {
                if (TxtAlarmStatus != null) TxtAlarmStatus.Text = "⚠️ Lütfen hedef cihaz seçin.";
                return;
            }

            if (!double.TryParse(TxtHighHigh?.Text, out double hh)) hh = 90;
            if (!double.TryParse(TxtHigh?.Text, out double h)) h = 75;
            if (!double.TryParse(TxtLowLow?.Text, out double ll)) ll = 10;

            try
            {
                string idPart = selectedDevText.Split('-')[0].Replace("ID:", "").Trim();
                if (int.TryParse(idPart, out int deviceId))
                {
                    bool success = Program.SaveAlarmThresholdsSecure(adminPass, deviceId, hh, h, ll);
                    if (success)
                    {
                        Program.LogUserActivity(loggedInUser, "Alarm Eşik", $"Cihaz ID {deviceId} eşikleri güncellendi.");
                        if (TxtAlarmStatus != null) TxtAlarmStatus.Text = $"✅ Alarm eşikleri güncellendi (Cihaz ID: {deviceId})";
                    }
                    else
                    {
                        if (TxtAlarmStatus != null) TxtAlarmStatus.Text = "❌ Yetkisiz işlem! Yönetici şifresi hatalı.";
                    }
                }
            }
            catch (Exception ex)
            {
                if (TxtAlarmStatus != null) TxtAlarmStatus.Text = "Kayıt Hatası: " + ex.Message;
            }
        }

        private void OnExportCsvClicked(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (lastQueriedRecords == null || lastQueriedRecords.Count == 0)
                {
                    if (TxtStatsSummary != null) TxtStatsSummary.Text = "⚠️ Dışa aktarılacak veri yok.";
                    return;
                }

                string downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                string fileName = $"plc_report_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
                string filePath = Path.Combine(downloadsPath, fileName);

                using (var writer = new StreamWriter(filePath))
                {
                    writer.WriteLine("Id,LogTime,PlcName,DbNumber,ProcessValue,SourceType");
                    foreach (var rec in lastQueriedRecords)
                    {
                        writer.WriteLine($"{rec.Id},{rec.LogTime:yyyy-MM-dd HH:mm:ss},{rec.PlcName},{rec.DbNumber},{rec.ProcessValue},{rec.SourceType}");
                    }
                }

                Program.LogUserActivity(loggedInUser, "CSV İndirme", $"CSV Raporu indirildi: {fileName}");
                if (TxtStatsSummary != null)
                {
                    TxtStatsSummary.Text = $"📁 İndirilenler klasörüne kaydedildi: {fileName}";
                }
            }
            catch (Exception ex)
            {
                if (TxtStatsSummary != null)
                {
                    TxtStatsSummary.Text = "CSV Kaydetme Hatası: " + ex.Message;
                }
            }
        }

        private void PopulateExcelReportControls()
        {
            if (CmbExcelPeriod != null)
            {
                CmbExcelPeriod.Items.Clear();
                CmbExcelPeriod.Items.Add("Gün");
                CmbExcelPeriod.Items.Add("Hafta");
                CmbExcelPeriod.Items.Add("Ay");
                CmbExcelPeriod.Items.Add("Yıl");
                CmbExcelPeriod.SelectedItem = "Gün";
            }

            var today = DateTimeOffset.Now;
            if (DpReportStart != null) DpReportStart.SelectedDate = today;
            if (DpReportEnd != null) DpReportEnd.SelectedDate = today;

            if (ReportDeviceRowsContainer != null && ReportDeviceRowsContainer.Children.Count > 0)
            {
                if (ReportDeviceRowsContainer.Children[0] is Border firstBorder && firstBorder.Child is WrapPanel wrap)
                {
                    foreach (var child in wrap.Children)
                    {
                        if (child is ComboBox cmb && cmb.Tag?.ToString() == "ReportDevice")
                        {
                            FillDeviceComboBox(cmb);
                        }
                    }
                }
            }
        }
        private void OnProfileButtonClicked(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                btn.ContextMenu?.Open(btn);
            }
        }

        private void OnLogoutMenuClicked(object? sender, RoutedEventArgs e)
        {
            Program.LogUserActivity(loggedInUser, "Oturum Kapatma", "Kullanıcı menüden çıkış yaptı.");
            var loginWin = new LoginWindow();
            loginWin.Show();
            isForceClosing = true;
            Close();
        }

        private void OnAddReportDeviceRowClicked(object? sender, RoutedEventArgs e)
        {
            if (ReportDeviceRowsContainer == null) return;

            int count = ReportDeviceRowsContainer.Children.Count + 1;

            var border = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#1A2036")),
                Padding = new Thickness(6),
                CornerRadius = new CornerRadius(4),
                BorderBrush = new SolidColorBrush(Color.Parse("#2B3454")),
                BorderThickness = new Thickness(1)
            };

            var wrap = new WrapPanel { Orientation = Avalonia.Layout.Orientation.Horizontal };
            wrap.Children.Add(new TextBlock { Text = $"Cihaz {count} -> Seçim:", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0), FontSize = 11, Foreground = Brushes.Gray });

            var cmbDev = new ComboBox { Width = 200, Margin = new Thickness(0, 0, 10, 0), FontSize = 11, Tag = "ReportDevice" };
            FillDeviceComboBox(cmbDev);
            wrap.Children.Add(cmbDev);

            border.Child = wrap;
            ReportDeviceRowsContainer.Children.Add(border);
        }

        private void OnExportMultiDeviceExcelClicked(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (DpReportStart?.SelectedDate == null)
                {
                    if (TxtExcelStatus != null) TxtExcelStatus.Text = "⚠️ Lütfen başlangıç tarihi seçin.";
                    return;
                }

                DateTime startDate = DpReportStart.SelectedDate.Value.Date;
                DateTime endDate = (DpReportEnd?.SelectedDate ?? DpReportStart.SelectedDate).Value.Date;

                if (endDate < startDate)
                {
                    (startDate, endDate) = (endDate, startDate);
                }

                string period = CmbExcelPeriod?.SelectedItem?.ToString() ?? "Gün";

                var selectedDevices = new List<string>();
                if (ReportDeviceRowsContainer != null)
                {
                    foreach (var child in ReportDeviceRowsContainer.Children)
                    {
                        if (child is Border border && border.Child is WrapPanel wrap)
                        {
                            foreach (var wChild in wrap.Children)
                            {
                                if (wChild is ComboBox cmb && cmb.Tag?.ToString() == "ReportDevice" && cmb.SelectedItem != null)
                                {
                                    string devVal = cmb.SelectedItem.ToString() ?? "";
                                    if (!string.IsNullOrEmpty(devVal) && !selectedDevices.Contains(devVal))
                                    {
                                        selectedDevices.Add(devVal);
                                    }
                                }
                            }
                        }
                    }
                }

                if (selectedDevices.Count == 0)
                {
                    if (TxtExcelStatus != null) TxtExcelStatus.Text = "⚠️ Lütfen en az bir cihaz seçin.";
                    return;
                }

                var allLogs = Program.GetLogsInRange(startDate, endDate);
                if (allLogs.Count == 0)
                {
                    if (TxtExcelStatus != null) TxtExcelStatus.Text = "⚠️ Seçilen tarih aralığında kayıt bulunamadı.";
                    return;
                }

                string downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                Directory.CreateDirectory(downloadsPath);
                string fileName = $"plc_coklu_cihaz_raporu_{startDate:yyyyMMdd}_{endDate:yyyyMMdd}_{DateTime.Now:HHmmss}.xlsx";
                string filePath = Path.Combine(downloadsPath, fileName);

                using (var workbook = new XLWorkbook())
                {
                    var sheet = workbook.Worksheets.Add("Çoklu Cihaz Özet");

                    sheet.Cell(1, 1).Value = "Dönem / Periyot";
                    
                    int colIndex = 2;
                    foreach (var dev in selectedDevices)
                    {
                        string shortName = dev.Split('(')[0].Trim();
                        sheet.Cell(1, colIndex).Value = $"{shortName} - Toplam";
                        sheet.Cell(1, colIndex + 1).Value = $"{shortName} - Ortalama";
                        sheet.Cell(1, colIndex + 2).Value = $"{shortName} - Max";
                        colIndex += 3;
                    }

                    var headerRange = sheet.Range(1, 1, 1, colIndex - 1);
                    headerRange.Style.Font.Bold = true;
                    headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#3CD88D");

                    var periodsList = GetPeriodLabels(startDate, endDate, period);

                    int rowIndex = 2;
                    foreach (var pLabel in periodsList.Labels)
                    {
                        sheet.Cell(rowIndex, 1).Value = pLabel.Name;

                        int cIdx = 2;
                        foreach (var dev in selectedDevices)
                        {
                            string plcNameOnly = dev.Split('(')[0].Trim();
                            var devLogs = allLogs.Where(r => r.PlcName.Contains(plcNameOnly) && r.LogTime >= pLabel.Start && r.LogTime <= pLabel.End).ToList();

                            long sum = devLogs.Sum(r => (long)r.ProcessValue);
                            double avg = devLogs.Count > 0 ? devLogs.Average(r => r.ProcessValue) : 0;
                            int max = devLogs.Count > 0 ? devLogs.Max(r => r.ProcessValue) : 0;

                            sheet.Cell(rowIndex, cIdx).Value = sum;
                            sheet.Cell(rowIndex, cIdx + 1).Value = Math.Round(avg, 2);
                            sheet.Cell(rowIndex, cIdx + 2).Value = max;

                            cIdx += 3;
                        }
                        rowIndex++;
                    }

                    sheet.Columns().AdjustToContents();
                    workbook.SaveAs(filePath);
                }

                Program.LogUserActivity(loggedInUser, "Çoklu Cihaz Excel", $"Çoklu cihaz raporu indirildi ({selectedDevices.Count} cihaz).");
                if (TxtExcelStatus != null)
                {
                    TxtExcelStatus.Text = $"✅ Çoklu cihaz Excel raporu başarıyla oluşturuldu: {fileName}";
                }
            }
            catch (Exception ex)
            {
                if (TxtExcelStatus != null) TxtExcelStatus.Text = "Excel Hatası: " + ex.Message;
            }
        }

        private class PeriodRangeHelper
        {
            public List<PeriodItem> Labels { get; set; } = new List<PeriodItem>();
        }

        private class PeriodItem
        {
            public string Name { get; set; } = string.Empty;
            public DateTime Start { get; set; }
            public DateTime End { get; set; }
        }

        private PeriodRangeHelper GetPeriodLabels(DateTime start, DateTime end, string period)
        {
            var helper = new PeriodRangeHelper();

            if (period == "Yıl")
            {
                for (int y = start.Year; y <= end.Year; y++)
                {
                    helper.Labels.Add(new PeriodItem { Name = $"{y}", Start = new DateTime(y, 1, 1), End = new DateTime(y, 12, 31, 23, 59, 59) });
                }
            }
            else if (period == "Ay")
            {
                DateTime cursor = new DateTime(start.Year, start.Month, 1);
                while (cursor <= end)
                {
                    DateTime monthEnd = new DateTime(cursor.Year, cursor.Month, DateTime.DaysInMonth(cursor.Year, cursor.Month), 23, 59, 59);
                    helper.Labels.Add(new PeriodItem { Name = $"{cursor:yyyy.MM}", Start = cursor, End = monthEnd > end ? end : monthEnd });
                    cursor = cursor.AddMonths(1);
                }
            }
            else if (period == "Hafta")
            {
                int diff = (7 + (start.DayOfWeek - DayOfWeek.Monday)) % 7;
                DateTime cursor = start.Date.AddDays(-diff);
                int w = 1;
                while (cursor <= end)
                {
                    DateTime wStart = cursor;
                    DateTime wEnd = cursor.AddDays(6).AddHours(23).AddMinutes(59).AddSeconds(59);
                    helper.Labels.Add(new PeriodItem { Name = $"Hafta {w} ({wStart:dd.MM}-{wEnd:dd.MM})", Start = wStart, End = wEnd > end ? end : wEnd });
                    cursor = cursor.AddDays(7);
                    w++;
                }
            }
            else
            {
                for (DateTime d = start.Date; d <= end.Date; d = d.AddDays(1))
                {
                    helper.Labels.Add(new PeriodItem { Name = $"{d:yyyy-MM-dd}", Start = d, End = d.AddHours(23).AddMinutes(59).AddSeconds(59) });
                }
            }

            return helper;
        }

        private void OnCompareTrendsClicked(object? sender, RoutedEventArgs e)
        {
            if (TrendSeriesContainer == null || TrendCanvas == null) return;

            TrendCanvas.Children.Clear();
            double width = TrendCanvas.Bounds.Width > 0 ? TrendCanvas.Bounds.Width : 750;
            double height = TrendCanvas.Bounds.Height > 0 ? TrendCanvas.Bounds.Height : 350;

            IBrush[] colors = { Brushes.Cyan, Brushes.LimeGreen, Brushes.Orange, Brushes.Magenta, Brushes.Yellow, Brushes.Red };
            int colorIdx = 0;
            int totalSeriesDrawn = 0;

            foreach (var child in TrendSeriesContainer.Children)
            {
                if (child is Border border && border.Child is WrapPanel wrap)
                {
                    string selectedDevice = "";
                    int year = DateTime.Now.Year;
                    int month = DateTime.Now.Month;
                    int day = DateTime.Now.Day;

                    foreach (var wChild in wrap.Children)
                    {
                        if (wChild is ComboBox cmb)
                        {
                            string tag = cmb.Tag?.ToString() ?? "";
                            if (tag == "Device" && cmb.SelectedItem != null) selectedDevice = cmb.SelectedItem.ToString() ?? "";
                            else if (tag == "Year" && cmb.SelectedItem != null) int.TryParse(cmb.SelectedItem.ToString(), out year);
                            else if (tag == "Month" && cmb.SelectedItem != null) int.TryParse(cmb.SelectedItem.ToString(), out month);
                            else if (tag == "Day" && cmb.SelectedItem != null) int.TryParse(cmb.SelectedItem.ToString(), out day);
                        }
                    }

                    var logs = Program.GetFilteredLogs(year, month, day, "00:00:00", "23:59:59")
                                      .OrderBy(r => r.LogTime).ToList();

                    if (!string.IsNullOrEmpty(selectedDevice))
                    {
                        string plcNameOnly = selectedDevice.Split('(')[0].Trim();
                        logs = logs.Where(r => r.PlcName.Contains(plcNameOnly)).ToList();
                    }

                    if (logs.Count > 0)
                    {
                        IBrush strokeColor = colors[colorIdx % colors.Length];
                        DrawTrendLine(logs, strokeColor, width, height);
                        colorIdx++;
                        totalSeriesDrawn++;
                    }
                }
            }

            Program.LogUserActivity(loggedInUser, "Trend Analizi", $"Grafik kıyaslaması yapıldı ({totalSeriesDrawn} seri).");
            if (TxtTrendStatus != null)
            {
                TxtTrendStatus.Text = $"📈 Grafik Güncellendi: Toplam {totalSeriesDrawn} seri başarıyla çizildi.";
            }
        }

        private void OnExportTrendsPdfClicked(object? sender, RoutedEventArgs e)
        {
            try
            {
                QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

                string downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                string fileName = $"plc_trend_report_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
                string filePath = Path.Combine(downloadsPath, fileName);

                QuestPDF.Fluent.Document.Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Margin(30);
                        page.Size(QuestPDF.Helpers.PageSizes.A4);
                        page.DefaultTextStyle(x => x.FontSize(11).FontColor(QuestPDF.Helpers.Colors.Grey.Darken3));

                        page.Header().Row(row =>
                        {
                            row.RelativeItem().Column(col =>
                            {
                                col.Item().Text("PLC Endüstriyel SCADA - Çoklu Kıyaslama Raporu").Bold().FontSize(16).FontColor(QuestPDF.Helpers.Colors.Blue.Medium);
                                col.Item().Text($"Rapor Oluşturma Zamanı: {DateTime.Now:yyyy-MM-dd HH:mm:ss}").FontSize(10).FontColor(QuestPDF.Helpers.Colors.Grey.Medium);
                            });
                        });

                        page.Content().PaddingVertical(15).Column(col =>
                        {
                            col.Spacing(10);
                            col.Item().Text("Grafikte Karşılaştırılan Seri Detayları:").Bold().FontSize(12);

                            if (TrendSeriesContainer != null)
                            {
                                int idx = 1;
                                foreach (var child in TrendSeriesContainer.Children)
                                {
                                    if (child is Border border && border.Child is WrapPanel wrap)
                                    {
                                        string serieInfo = $"Seri {idx} -> ";
                                        foreach (var wChild in wrap.Children)
                                        {
                                            if (wChild is ComboBox cmb && cmb.SelectedItem != null)
                                            {
                                                serieInfo += $"[{cmb.Tag}: {cmb.SelectedItem}] ";
                                            }
                                        }
                                        col.Item().Text(serieInfo).FontSize(10).FontColor(QuestPDF.Helpers.Colors.Black);
                                        idx++;
                                    }
                                }
                            }

                            col.Item().PaddingTop(25).Text("Bu rapor Özge Mühendislik SCADA sistemi tarafından otomatik olarak üretilmiştir.").Italic().FontSize(9);
                        });

                        page.Footer().AlignCenter().Text(x =>
                        {
                            x.Span("Sayfa ");
                            x.CurrentPageNumber();
                        });
                    });
                }).GeneratePdf(filePath);

                Program.LogUserActivity(loggedInUser, "PDF İndirme", $"Trend PDF raporu indirildi: {fileName}");
                if (TxtTrendStatus != null)
                {
                    TxtTrendStatus.Text = $"📄 PDF Başarıyla Kaydedildi: {fileName} (İndirilenler klasöründe)";
                }
            }
            catch (Exception ex)
            {
                if (TxtTrendStatus != null)
                {
                    TxtTrendStatus.Text = "PDF Oluşturma Hatası: " + ex.Message;
                }
            }
        }

        private void DrawTrendLine(List<LogRecordModel> records, IBrush strokeBrush, double width, double height)
        {
            if (records.Count < 2 || TrendCanvas == null) return;

            double maxVal = records.Max(r => r.ProcessValue);
            if (maxVal <= 0) maxVal = 100;

            var points = new Avalonia.Collections.AvaloniaList<Avalonia.Point>();
            double stepX = width / (records.Count - 1);

            for (int i = 0; i < records.Count; i++)
            {
                double x = i * stepX;
                double y = height - (records[i].ProcessValue / maxVal * (height - 20)) - 10;
                points.Add(new Avalonia.Point(x, y));
            }

            var polyline = new Avalonia.Controls.Shapes.Polyline
            {
                Points = points,
                Stroke = strokeBrush,
                StrokeThickness = 2
            };

            TrendCanvas.Children.Add(polyline);
        }

        private void OnAddDeviceClicked(object? sender, RoutedEventArgs e)
        {
            string adminPass = TxtAdminPass?.Text ?? "";
            string name = TxtNewName?.Text ?? "";
            string ip = TxtNewIp?.Text ?? "";
            
            if (!int.TryParse(TxtNewRack?.Text, out int rack)) rack = 0;
            if (!int.TryParse(TxtNewSlot?.Text, out int slot)) slot = 1;
            if (!int.TryParse(TxtNewDb?.Text, out int db)) db = 100;

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(ip))
            {
                if (TxtSettingsStatus != null) TxtSettingsStatus.Text = "⚠️ Lütfen PLC adı ve IP adresini boş bırakmayın.";
                return;
            }

            bool success = Program.AddPlcDeviceSecure(adminPass, name, ip, rack, slot, db);
            if (success)
            {
                Program.LogUserActivity(loggedInUser, "Cihaz Ekleme", $"Yeni PLC eklendi: {name}");
                if (TxtSettingsStatus != null) TxtSettingsStatus.Text = $"✅ Başarıyla eklendi: {name}";
                LoadPlcDeviceDropdown(); 
                RefreshDeleteDropdown(); 
                RefreshAlarmDeviceDropdown();
                
                if (TxtNewName != null) TxtNewName.Text = "";
                if (TxtNewIp != null) TxtNewIp.Text = "";
            }
            else
            {
                if (TxtSettingsStatus != null) TxtSettingsStatus.Text = "❌ Yetkisiz işlem! Yönetici şifresi hatalı.";
            }
        }

        private void OnDeleteDeviceClicked(object? sender, RoutedEventArgs e)
        {
            string adminPass = TxtAdminPass?.Text ?? "";
            string selectedText = CmbDevicesToDelete?.SelectedItem?.ToString() ?? "";

            if (string.IsNullOrEmpty(selectedText))
            {
                if (TxtSettingsStatus != null) TxtSettingsStatus.Text = "⚠️ Silinecek cihaz seçilmedi.";
                return;
            }

            try
            {
                string idPart = selectedText.Split('-')[0].Replace("ID:", "").Trim();
                if (int.TryParse(idPart, out int deviceId))
                {
                    bool success = Program.DeletePlcDeviceSecure(adminPass, deviceId);
                    if (success)
                    {
                        Program.LogUserActivity(loggedInUser, "Cihaz Silme", $"PLC silindi ID: {deviceId}");
                        if (TxtSettingsStatus != null) TxtSettingsStatus.Text = $"🗑️ Cihaz silindi (ID: {deviceId}).";
                        LoadPlcDeviceDropdown();
                        RefreshDeleteDropdown();
                        RefreshAlarmDeviceDropdown();
                    }
                    else
                    {
                        if (TxtSettingsStatus != null) TxtSettingsStatus.Text = "❌ Yetkisiz işlem! Yönetici şifresi hatalı.";
                    }
                }
            }
            catch (Exception ex)
            {
                if (TxtSettingsStatus != null) TxtSettingsStatus.Text = "Silme Hatası: " + ex.Message;
            }
        }

        private void OnSelectedDeviceChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (CmbActiveDevices?.SelectedItem is string selectedText)
            {
                if (TxtSource != null)
                {
                    TxtSource.Text = $"Seçilen Cihaz: {selectedText}";
                }
            }
        }

        private void SetActiveNavButton(Button? active)
        {
            var navButtons = new[] { BtnNavLive, BtnNavReport, BtnNavTrends, BtnNavAlarms, BtnNavSettings, BtnNavAdmin };
            foreach (var btn in navButtons)
            {
                if (btn == null) continue;
                btn.Classes.Remove("active");
            }

            if (active != null) active.Classes.Add("active");
        }

        private void OnNavLiveClicked(object? sender, RoutedEventArgs e)
        {
            if (PageLive != null) PageLive.IsVisible = true;
            if (PageReport != null) PageReport.IsVisible = false;
            if (PageTrends != null) PageTrends.IsVisible = false;
            if (PageAlarms != null) PageAlarms.IsVisible = false;
            if (PageSettings != null) PageSettings.IsVisible = false;
            if (PageAdmin != null) PageAdmin.IsVisible = false;
            SetActiveNavButton(BtnNavLive);
        }

        private void OnNavReportClicked(object? sender, RoutedEventArgs e)
        {
            if (PageLive != null) PageLive.IsVisible = false;
            if (PageReport != null) PageReport.IsVisible = true;
            if (PageTrends != null) PageTrends.IsVisible = false;
            if (PageAlarms != null) PageAlarms.IsVisible = false;
            if (PageSettings != null) PageSettings.IsVisible = false;
            if (PageAdmin != null) PageAdmin.IsVisible = false;
            SetActiveNavButton(BtnNavReport);

            GenerateReportColumns();
        }

        private void OnNavTrendsClicked(object? sender, RoutedEventArgs e)
        {
            if (PageLive != null) PageLive.IsVisible = false;
            if (PageReport != null) PageReport.IsVisible = false;
            if (PageTrends != null) PageTrends.IsVisible = true;
            if (PageAlarms != null) PageAlarms.IsVisible = false;
            if (PageSettings != null) PageSettings.IsVisible = false;
            if (PageAdmin != null) PageAdmin.IsVisible = false;
            SetActiveNavButton(BtnNavTrends);
        }

        private void OnNavAlarmsClicked(object? sender, RoutedEventArgs e)
        {
            if (PageLive != null) PageLive.IsVisible = false;
            if (PageReport != null) PageReport.IsVisible = false;
            if (PageTrends != null) PageTrends.IsVisible = false;
            if (PageAlarms != null) PageAlarms.IsVisible = true;
            if (PageSettings != null) PageSettings.IsVisible = false;
            if (PageAdmin != null) PageAdmin.IsVisible = false;
            SetActiveNavButton(BtnNavAlarms);
            RefreshAlarmDeviceDropdown();
        }

        private void OnNavSettingsClicked(object? sender, RoutedEventArgs e)
        {
            if (PageLive != null) PageLive.IsVisible = false;
            if (PageReport != null) PageReport.IsVisible = false;
            if (PageTrends != null) PageTrends.IsVisible = false;
            if (PageAlarms != null) PageAlarms.IsVisible = false;
            if (PageSettings != null) PageSettings.IsVisible = true;
            if (PageAdmin != null) PageAdmin.IsVisible = false;
            SetActiveNavButton(BtnNavSettings);
            RefreshDeleteDropdown();
        }

        private void OnNavAdminClicked(object? sender, RoutedEventArgs e)
        {
            if (PageLive != null) PageLive.IsVisible = false;
            if (PageReport != null) PageReport.IsVisible = false;
            if (PageTrends != null) PageTrends.IsVisible = false;
            if (PageAlarms != null) PageAlarms.IsVisible = false;
            if (PageSettings != null) PageSettings.IsVisible = false;
            if (PageAdmin != null) PageAdmin.IsVisible = true;
            SetActiveNavButton(BtnNavAdmin);
            RefreshAdminPanelData();
        }

        public void UpdateUI(int processValue, string source)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (TxtProcessValue != null) TxtProcessValue.Text = processValue.ToString();
                if (TxtSource != null) TxtSource.Text = source;

                string hourKey = DateTime.Now.ToString("yyyy-MM-dd HH:00");

                if (!hourlyTotalsCache.ContainsKey(hourKey))
                {
                    hourlyTotalsCache[hourKey] = 0;
                }
                hourlyTotalsCache[hourKey] += processValue;

                liveLogs.Clear();
                
                foreach (var kvp in hourlyTotalsCache.OrderByDescending(k => k.Key))
                {
                    string datePart = kvp.Key.Split(' ')[0];
                    string hourPart = kvp.Key.Split(' ')[1];
                    
                    string logLine = $"Tarih: {datePart}   |   Saat: {hourPart}   |   Toplam Değer: {kvp.Value,10}";
                    liveLogs.Add(logLine);
                }

                if (LiveLogListBox != null)
                {
                    LiveLogListBox.ItemsSource = null;
                    LiveLogListBox.ItemsSource = liveLogs;
                }
            });
        }

        public void AppendAlarmLog(string alarmMessage)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                alarmLogs.Insert(0, alarmMessage);
                if (alarmLogs.Count > 100) alarmLogs.RemoveAt(alarmLogs.Count - 1);

                if (AlarmLogListBox != null)
                {
                    AlarmLogListBox.ItemsSource = null;
                    AlarmLogListBox.ItemsSource = alarmLogs;
                }
            });
        }

        public void UpdateCloudSyncStatus(string syncStatusMessage)
        {
            Dispatcher.UIThread.InvokeAsync(() => { });
        }

        private void OnQueryArchiveAutoTrigger(object? sender, SelectionChangedEventArgs e)
        {
            if (PageReport != null && PageReport.IsVisible)
            {
                GenerateReportColumns();
            }
        }

        private void OnSelectedMonthChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (ListColMonth?.SelectedItem is string selectedMonthStr && !string.IsNullOrEmpty(selectedMonthStr))
            {
                try
                {
                    string monthNumPart = selectedMonthStr.Split('.')[0].Trim();
                    if (int.TryParse(monthNumPart, out int targetMonth))
                    {
                        LoadWeeksStrictForMonth(targetMonth);
                    }
                }
                catch { }
            }
        }

        private void LoadWeeksStrictForMonth(int month)
        {
            string yearStr = CmbYear?.SelectedItem?.ToString() ?? DateTime.Now.Year.ToString();
            int year = int.TryParse(yearStr, out int y) ? y : DateTime.Now.Year;

            var allLogs = Program.GetFilteredLogs(year, null, null, "00:00:00", "23:59:59");
            var weekItemsForMonth = new List<string>();

            DateTime startDate = new DateTime(year, month, 1);
            int daysInMonth = DateTime.DaysInMonth(year, month);
            DateTime monthEnd = new DateTime(year, month, daysInMonth);

            int weekNum = 1;
            while (startDate <= monthEnd)
            {
                DateTime weekStart = startDate;
                DateTime weekEnd = startDate.AddDays(6);

                var weekLogs = allLogs.Where(r => r.LogTime.Date >= weekStart.Date && r.LogTime.Date <= weekEnd.Date).ToList();
                int weekSum = weekLogs.Sum(r => r.ProcessValue);

                string dateRangeStr = $"{weekStart:MM.dd}-{weekEnd:MM.dd}";
                weekItemsForMonth.Add($"H.{weekNum:D2} ({dateRangeStr}): {weekSum}");

                startDate = weekEnd.AddDays(1);
                weekNum++;
            }

            if (ListColWeek != null) ListColWeek.ItemsSource = weekItemsForMonth;
        }

        private void OnSelectedWeekChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (ListColWeek?.SelectedItem is string selectedWeekStr && !string.IsNullOrEmpty(selectedWeekStr))
            {
                try
                {
                    int hIndex = selectedWeekStr.IndexOf('(');
                    int cIndex = selectedWeekStr.IndexOf(')');
                    if (hIndex != -1 && cIndex != -1)
                    {
                        string rangeStr = selectedWeekStr.Substring(hIndex + 1, cIndex - hIndex - 1);
                        string[] parts = rangeStr.Split('-');
                        string yearStr = CmbYear?.SelectedItem?.ToString() ?? DateTime.Now.Year.ToString();
                        int year = int.TryParse(yearStr, out int y) ? y : DateTime.Now.Year;

                        if (parts.Length == 2 && 
                            DateTime.TryParse($"{year}/{parts[0].Replace('.', '/')}", out DateTime startDate) && 
                            DateTime.TryParse($"{year}/{parts[1].Replace('.', '/')}", out DateTime endDate))
                        {
                            LoadDaysForWeek(startDate, endDate);
                        }
                    }
                }
                catch { }
            }
        }

        private void LoadDaysForWeek(DateTime start, DateTime end)
        {
            string yearStr = CmbYear?.SelectedItem?.ToString() ?? DateTime.Now.Year.ToString();
            int year = int.TryParse(yearStr, out int y) ? y : DateTime.Now.Year;

            var allLogs = Program.GetFilteredLogs(year, null, null, "00:00:00", "23:59:59");
            var dayItemsForWeek = new List<string>();

            for (DateTime date = start; date <= end; date = date.AddDays(1))
            {
                var dailyLogs = allLogs.Where(r => r.LogTime.Date == date.Date).ToList();
                int dailySum = dailyLogs.Sum(r => r.ProcessValue);

                dayItemsForWeek.Add($"{date:yyyy-MM-dd} ({date:ddd}): {dailySum}");
            }

            if (ListColDay != null) ListColDay.ItemsSource = dayItemsForWeek;
        }

        private void OnSelectedDayChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (ListColDay?.SelectedItem is string selectedDayStr && !string.IsNullOrEmpty(selectedDayStr))
            {
                try
                {
                    string datePart = selectedDayStr.Split(' ')[0];
                    if (DateTime.TryParse(datePart, out DateTime targetDate))
                    {
                        LoadHoursForDay(targetDate);
                    }
                }
                catch { }
            }
        }

        private void LoadHoursForDay(DateTime date)
        {
            var allLogs = Program.GetFilteredLogs(date.Year, date.Month, date.Day, "00:00:00", "23:59:59");
            var hourItemsForDay = new List<string>();

            for (int h = 0; h < 24; h++)
            {
                var hourlyLogs = allLogs.Where(r => r.LogTime.Hour == h).ToList();
                int hourlySum = hourlyLogs.Sum(r => r.ProcessValue);

                hourItemsForDay.Add($"Saat {h:D2}:00 - Topl: {hourlySum}");
            }

            if (ListColDay != null) ListColDay.ItemsSource = hourItemsForDay;
        }

        private void GenerateReportColumns()
        {
            string yearStr = CmbYear?.SelectedItem?.ToString() ?? DateTime.Now.Year.ToString();
            int year = int.TryParse(yearStr, out int y) ? y : DateTime.Now.Year;

            try
            {
                var allLogs = Program.GetFilteredLogs(year, null, null, "00:00:00", "23:59:59");
                lastQueriedRecords.Clear();
                lastQueriedRecords.AddRange(allLogs);

                var yearItems = new List<string>();
                var monthItems = new List<string>();
                var weekItems = new List<string>();
                var dayItems = new List<string>();

                long totalYearSum = 0;

                yearItems.Add($"Seçilen Yıl: {year}");
                yearItems.Add("-------------------");
                yearItems.Add($"Toplam Kayıt: {allLogs.Count}");
                yearItems.Add($"Genel Toplam: {allLogs.Sum(r => r.ProcessValue)}");

                string[] monthNames = { "", "Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran", "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık" };
                for (int mIdx = 1; mIdx <= 12; mIdx++)
                {
                    var mLogs = allLogs.Where(r => r.LogTime.Month == mIdx).ToList();
                    int mSum = mLogs.Sum(r => r.ProcessValue);
                    totalYearSum += mSum;

                    monthItems.Add($"{mIdx:D2}. Ay ({monthNames[mIdx]}): {mSum}");
                }

                for (int w = 1; w <= 52; w++)
                {
                    DateTime jan1 = new DateTime(year, 1, 1);
                    int daysOffset = DayOfWeek.Monday - jan1.DayOfWeek;
                    DateTime firstMonday = jan1.AddDays(daysOffset);
                    var weekStart = firstMonday.AddDays((w - 1) * 7);
                    var weekEnd = weekStart.AddDays(6);

                    var weekLogs = allLogs.Where(r => r.LogTime.Date >= weekStart.Date && r.LogTime.Date <= weekEnd.Date).ToList();
                    int weekSum = weekLogs.Sum(r => r.ProcessValue);

                    string dateRangeStr = $"{weekStart:MM.dd}-{weekEnd:MM.dd}";
                    weekItems.Add($"H.{w:D2} ({dateRangeStr}): {weekSum}");
                }

                DateTime defaultDay = DateTime.Now.Year == year ? DateTime.Now : new DateTime(year, 1, 1);
                var defaultDayLogs = allLogs.Where(r => r.LogTime.Date == defaultDay.Date).ToList();
                for (int h = 0; h < 24; h++)
                {
                    var hourlyLogs = defaultDayLogs.Where(r => r.LogTime.Hour == h).ToList();
                    int hourlySum = hourlyLogs.Sum(r => r.ProcessValue);

                    dayItems.Add($"Saat {h:D2}:00 - Topl: {hourlySum}");
                }

                if (ListColYear != null) ListColYear.ItemsSource = yearItems;
                if (ListColMonth != null) ListColMonth.ItemsSource = monthItems;
                if (ListColWeek != null) ListColWeek.ItemsSource = weekItems;
                if (ListColDay != null) ListColDay.ItemsSource = dayItems;

                if (TxtStatsSummary != null)
                {
                    TxtStatsSummary.Text = $"📊 Rapor Hazır. Ay/Hafta seçerek detayları inceleyebilirsiniz.";
                }
            }
            catch (Exception ex)
            {
                if (TxtStatsSummary != null)
                {
                    TxtStatsSummary.Text = "Hata: " + ex.Message;
                }
            }
        }
    }
}