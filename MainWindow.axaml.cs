using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.Media;
using Avalonia.Styling;
using Microsoft.Data.Sqlite;
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

        public MainWindow()
        {
            InitializeComponent();
            Program.MainWindowInstance = this;
            
            PopulateFilterComboBoxes();
            LoadPlcDeviceDropdown();
            RefreshDeleteDropdown();
            RefreshAlarmDeviceDropdown();
            PopulateTrendComboBoxes();
        }

        // --- TEMA DEĞİŞTİRME (KOYU TEMA / AÇIK TEMA) ---
        private void OnThemeToggleClicked(object? sender, RoutedEventArgs e)
        {
            isDarkMode = !isDarkMode;
            if (Application.Current != null)
            {
                Application.Current.RequestedThemeVariant = isDarkMode ? ThemeVariant.Dark : ThemeVariant.Light;
            }

            if (BtnThemeToggle != null)
            {
                BtnThemeToggle.Content = isDarkMode ? "🌙 Koyu Tema" : "☀️ Açık Tema";
            }
        }

        // --- SUPABASE PROJE PANELİ LİNKİNİ TARAYICI_DA AÇMA ---
        private void OnSupabaseLinkClicked(object? sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            try
            {
                string url = "https://supabase.com/dashboard/project/uzhysodwllhgoyoytyed";
                
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
                for (int y = 2025; y <= 2030; y++) CmbYear.Items.Add(y);
                CmbYear.SelectedItem = DateTime.Now.Year;
            }

            if (CmbMonth != null)
            {
                for (int m = 1; m <= 12; m++) CmbMonth.Items.Add(m);
                CmbMonth.SelectedItem = DateTime.Now.Month;
            }

            if (CmbDay != null)
            {
                for (int d = 1; d <= 31; d++) CmbDay.Items.Add(d);
                CmbDay.SelectedItem = DateTime.Now.Day;
            }

            if (CmbStartHour != null && CmbEndHour != null)
            {
                for (int h = 0; h < 24; h++)
                {
                    string hourStr = h.ToString("D2");
                    CmbStartHour.Items.Add(hourStr);
                    CmbEndHour.Items.Add(hourStr);
                }
                CmbStartHour.SelectedItem = "00";
                CmbEndHour.SelectedItem = "23";
            }

            if (CmbStartMinute != null && CmbEndMinute != null)
            {
                for (int min = 0; min < 60; min++)
                {
                    string minStr = min.ToString("D2");
                    CmbStartMinute.Items.Add(minStr);
                    CmbEndMinute.Items.Add(minStr);
                }
                CmbStartMinute.SelectedItem = "00";
                CmbEndMinute.SelectedItem = "59";
            }
        }

        private void PopulateTrendComboBoxes()
        {
            if (CmbChartType != null)
            {
                CmbChartType.Items.Add("Çizgi Grafik (Line)");
                CmbChartType.Items.Add("Alan Grafik (Area)");
                CmbChartType.Items.Add("İkili Kıyas (Overlay Comparison)");
                CmbChartType.SelectedItem = "Çizgi Grafik (Line)";
            }

            if (CmbTrendDay1 != null && CmbTrendDay2 != null)
            {
                for (int d = 1; d <= 31; d++)
                {
                    CmbTrendDay1.Items.Add(d);
                    CmbTrendDay2.Items.Add(d);
                }
                CmbTrendDay1.SelectedItem = DateTime.Now.Day;
                CmbTrendDay2.SelectedItem = DateTime.Now.Day > 1 ? DateTime.Now.Day - 1 : 1;
            }
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
                        if (TxtAlarmStatus != null) TxtAlarmStatus.Text = $"✅ Alarm eşikleri başarıyla güncellendi (Cihaz ID: {deviceId})";
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
                    if (TxtStatsSummary != null) TxtStatsSummary.Text = "⚠️ Dışa aktarılacak veri yok. Önce filtreleyip listeleyin.";
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

        private void OnCompareTrendsClicked(object? sender, RoutedEventArgs e)
        {
            string chartType = CmbChartType?.SelectedItem?.ToString() ?? "Çizgi Grafik (Line)";
            int day1 = int.TryParse(CmbTrendDay1?.SelectedItem?.ToString(), out int d1) ? d1 : DateTime.Now.Day;
            int day2 = int.TryParse(CmbTrendDay2?.SelectedItem?.ToString(), out int d2) ? d2 : DateTime.Now.Day;
            int currentYear = DateTime.Now.Year;
            int currentMonth = DateTime.Now.Month;

            var recordsDay1 = Program.GetFilteredLogs(currentYear, currentMonth, day1, "00:00:00", "23:59:59")
                                     .OrderBy(r => r.LogTime).ToList();
            var recordsDay2 = Program.GetFilteredLogs(currentYear, currentMonth, day2, "00:00:00", "23:59:59")
                                     .OrderBy(r => r.LogTime).ToList();

            if (TrendCanvas != null)
            {
                TrendCanvas.Children.Clear();

                double width = TrendCanvas.Bounds.Width > 0 ? TrendCanvas.Bounds.Width : 750;
                double height = TrendCanvas.Bounds.Height > 0 ? TrendCanvas.Bounds.Height : 350;

                if (chartType.Contains("Alan Grafik"))
                {
                    DrawAreaChart(recordsDay1, Brushes.MediumSlateBlue, width, height);
                }
                else if (chartType.Contains("İkili Kıyas"))
                {
                    DrawTrendLine(recordsDay1, Brushes.LimeGreen, width, height);
                    DrawTrendLine(recordsDay2, Brushes.Orange, width, height);
                }
                else
                {
                    DrawTrendLine(recordsDay1, Brushes.Cyan, width, height);
                }
            }

            if (TxtTrendStatus != null)
            {
                TxtTrendStatus.Text = $"📈 Grafik Çizildi [{chartType}] -> Gün {day1}: {recordsDay1.Count} kayıt";
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

        private void DrawAreaChart(List<LogRecordModel> records, IBrush fillBrush, double width, double height)
        {
            if (records.Count < 2 || TrendCanvas == null) return;

            double maxVal = records.Max(r => r.ProcessValue);
            if (maxVal <= 0) maxVal = 100;

            var points = new Avalonia.Collections.AvaloniaList<Avalonia.Point>();
            double stepX = width / (records.Count - 1);

            points.Add(new Avalonia.Point(0, height));
            for (int i = 0; i < records.Count; i++)
            {
                double x = i * stepX;
                double y = height - (records[i].ProcessValue / maxVal * (height - 20)) - 10;
                points.Add(new Avalonia.Point(x, y));
            }
            points.Add(new Avalonia.Point(width, height));

            var polygon = new Avalonia.Controls.Shapes.Polygon
            {
                Points = points,
                Fill = fillBrush,
                Opacity = 0.5
            };

            TrendCanvas.Children.Add(polygon);
            DrawTrendLine(records, Brushes.White, width, height);
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
                        if (TxtSettingsStatus != null) TxtSettingsStatus.Text = $"🗑️ Cihaz başarıyla silindi (ID: {deviceId}).";
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

        private void OnNavLiveClicked(object? sender, RoutedEventArgs e)
        {
            if (PageLive != null) PageLive.IsVisible = true;
            if (PageReport != null) PageReport.IsVisible = false;
            if (PageTrends != null) PageTrends.IsVisible = false;
            if (PageAlarms != null) PageAlarms.IsVisible = false;
            if (PageSettings != null) PageSettings.IsVisible = false;
        }

        private void OnNavReportClicked(object? sender, RoutedEventArgs e)
        {
            if (PageLive != null) PageLive.IsVisible = false;
            if (PageReport != null) PageReport.IsVisible = true;
            if (PageTrends != null) PageTrends.IsVisible = false;
            if (PageAlarms != null) PageAlarms.IsVisible = false;
            if (PageSettings != null) PageSettings.IsVisible = false;
        }

        private void OnNavTrendsClicked(object? sender, RoutedEventArgs e)
        {
            if (PageLive != null) PageLive.IsVisible = false;
            if (PageReport != null) PageReport.IsVisible = false;
            if (PageTrends != null) PageTrends.IsVisible = true;
            if (PageAlarms != null) PageAlarms.IsVisible = false;
            if (PageSettings != null) PageSettings.IsVisible = false;
        }

        private void OnNavAlarmsClicked(object? sender, RoutedEventArgs e)
        {
            if (PageLive != null) PageLive.IsVisible = false;
            if (PageReport != null) PageReport.IsVisible = false;
            if (PageTrends != null) PageTrends.IsVisible = false;
            if (PageAlarms != null) PageAlarms.IsVisible = true;
            if (PageSettings != null) PageSettings.IsVisible = false;
            RefreshAlarmDeviceDropdown();
        }

        private void OnNavSettingsClicked(object? sender, RoutedEventArgs e)
        {
            if (PageLive != null) PageLive.IsVisible = false;
            if (PageReport != null) PageReport.IsVisible = false;
            if (PageTrends != null) PageTrends.IsVisible = false;
            if (PageAlarms != null) PageAlarms.IsVisible = false;
            if (PageSettings != null) PageSettings.IsVisible = true;
            
            RefreshDeleteDropdown();
        }

        public void UpdateUI(int processValue, string source)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (TxtProcessValue != null) TxtProcessValue.Text = processValue.ToString();
                if (TxtSource != null) TxtSource.Text = source;

                string timeNow = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string logLine = $"{timeNow}   |   Değer: {processValue,6}   |   Durum: {source}";
                
                liveLogs.Insert(0, logLine);
                if (liveLogs.Count > 100) liveLogs.RemoveAt(liveLogs.Count - 1);

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
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (TxtSource != null)
                {
                    // İstersen kaynak metnine bulut durumunu da yansıtabilirsin
                }
            });
        }

        private void OnQueryArchiveClicked(object? sender, RoutedEventArgs e)
        {
            string yearStr = CmbYear?.SelectedItem?.ToString() ?? "";
            string monthStr = CmbMonth?.SelectedItem?.ToString() ?? "";
            string dayStr = CmbDay?.SelectedItem?.ToString() ?? "";
            
            string startHour = CmbStartHour?.SelectedItem?.ToString() ?? "00";
            string startMin = CmbStartMinute?.SelectedItem?.ToString() ?? "00";
            string endHour = CmbEndHour?.SelectedItem?.ToString() ?? "23";
            string endMin = CmbEndMinute?.SelectedItem?.ToString() ?? "59";

            string startTime = $"{startHour}:{startMin}:00";
            string endTime = $"{endHour}:{endMin}:59";

            try
            {
                int? yVal = int.TryParse(yearStr, out int y) ? y : null;
                int? mVal = int.TryParse(monthStr, out int m) ? m : null;
                int? dVal = int.TryParse(dayStr, out int d) ? d : null;

                lastQueriedRecords = Program.GetFilteredLogs(yVal, mVal, dVal, startTime, endTime);
                var archiveLines = new List<string>();
                var values = new List<int>();

                foreach (var rec in lastQueriedRecords)
                {
                    values.Add(rec.ProcessValue);
                    archiveLines.Add($"{rec.LogTime:yyyy-MM-dd HH:mm:ss} | {rec.PlcName} (DB{rec.DbNumber}) | Değer: {rec.ProcessValue,6} | {rec.SourceType}");
                }

                if (ArchiveListBox != null)
                {
                    ArchiveListBox.ItemsSource = archiveLines;
                }

                if (values.Count > 0 && TxtStatsSummary != null)
                {
                    TxtStatsSummary.Text = $"📊 Arama Sonucu ({lastQueriedRecords.Count} Kayıt) -> Ortalama: {values.Average():F1} | Toplam: {values.Sum()}";
                }
                else if (TxtStatsSummary != null)
                {
                    TxtStatsSummary.Text = "⚠️ Seçilen kriterlere uygun kayıt bulunamadı.";
                }
            }
            catch (Exception ex)
            {
                if (TxtStatsSummary != null)
                {
                    TxtStatsSummary.Text = "Filtreleme Hatası: " + ex.Message;
                }
            }
        }
    }
}