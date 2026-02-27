using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Markup;
using System.Diagnostics;
using Quicker.Public;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;
using System.Globalization;

public static void Exec(IStepContext context)
{
    Application.Current.Dispatcher.Invoke(() =>
    {
        // --- 1. 基础配置与路径 ---
        string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HFNoteSync");
        string localNotesFile = Path.Combine(dataDir, "notes.json");
        string configFile = Path.Combine(dataDir, ".config");
        string logDir = Path.Combine(dataDir, "logs");
        string datasetId = "mingyang22/huggingface-notes";
        string remoteNotesPath = "db/notes.json";
        string token = Environment.GetEnvironmentVariable("HF_TOKEN");

        Directory.CreateDirectory(dataDir);
        Directory.CreateDirectory(logDir);

        void WriteLog(string level, string message, Exception ex = null) {
            // Logging disabled by configuration.
        }

        void WriteJsonSnapshot(string syncId, string tag, string jsonText) {
            try {
                WriteLog("INFO", $"[{syncId}] {tag}_BEGIN");
                WriteLog("INFO", $"[{syncId}] {tag}:\n{jsonText}");
                WriteLog("INFO", $"[{syncId}] {tag}_END");
            } catch (Exception ex) {
                WriteLog("WARN", $"[{syncId}] WriteJsonSnapshot failed for {tag}.", ex);
            }
        }
        bool IsTcpOpen(string host, int port, int timeoutMs = 800) {
            try {
                using (var tcp = new System.Net.Sockets.TcpClient()) {
                    var task = tcp.ConnectAsync(host, port);
                    return task.Wait(timeoutMs) && tcp.Connected;
                }
            } catch {
                return false;
            }
        }

        // --- 2. 状态变量 ---
        var noteData = new ObservableCollection<NoteModel>();
        string currentFilter = "all"; // all, pinned, trash
        bool isNavCollapsed = false;
        double winWidth = 1000, winHeight = 650;
        double winLeft = double.NaN, winTop = double.NaN;
        
        // 从配置加载窗口状态
        if (File.Exists(configFile)) {
            try {
                var lines = File.ReadAllLines(configFile);
                foreach(var l in lines) {
                    var parts = l.Split('=');
                    if(parts.Length == 2) {
                        string key = parts[0].Trim();
                        string val = parts[1].Trim();
                        if(key == "Width") double.TryParse(val, out winWidth);
                        if(key == "Height") double.TryParse(val, out winHeight);
                        if(key == "Left") double.TryParse(val, out winLeft);
                        if(key == "Top") double.TryParse(val, out winTop);
                        if(key == "IsNavCollapsed") bool.TryParse(val, out isNavCollapsed);
                    }
                }
            } catch {}
        }

        // --- 3. 核心 XAML 还原 (1:1 ClassNote 玻璃风格) ---
        string xaml = @"
<Window xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
        xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
        Title='HF Note Sync' Height='650' Width='1000'
        AllowsTransparency='True' WindowStyle='None' 
        ResizeMode='CanResize' Background='Transparent'
        WindowStartupLocation='CenterScreen'>
    <Window.Resources>
        <BooleanToVisibilityConverter x:Key='BoolToVis'/>
        <SolidColorBrush x:Key='BgDark' Color='#0A0A0A'/>
        <SolidColorBrush x:Key='AccentBlue' Color='#3B82F6'/>
        <LinearGradientBrush x:Key='GradientAI' StartPoint='0,0' EndPoint='1,1'>
            <GradientStop Color='#7C3AED' Offset='0.0'/><GradientStop Color='#DB2777' Offset='1.0'/>
        </LinearGradientBrush>
        <!-- Icons -->
        <Geometry x:Key='Icon_All'>M14,17H7V15H14M17,13H7V11H17M17,9H7V7H17M19,3H5C3.89,3 3,3.89 3,5V19A2,2 0 0,0 5,21H19A2,2 0 0,0 21,19V5C21,3.89 20.1,3 19,3Z</Geometry>
        <Geometry x:Key='Icon_Star'>M12,17.27L18.18,21L16.54,13.97L22,9.24L14.81,8.62L12,2L9.19,8.62L2,9.24L7.45,13.97L5.82,21L12,17.27Z</Geometry>
        <Geometry x:Key='Icon_Trash'>M19,4H15.5L14.5,3H9.5L8.5,4H5V6H19M6,19A2,2 0 0,0 8,21H16A2,2 0 0,0 18,19V7H6V19Z</Geometry>
        <Geometry x:Key='Icon_Pin'>M16,12V4H17V2H7V4H8V12L6,14V16H11.2V22H12.8V16H18V14L16,12Z</Geometry>
        <Geometry x:Key='Icon_Sync'>M12,18A6,6 0 0,1 6,12C6,11 6.25,10.03 6.7,9.2L5.24,7.74C4.46,8.97 4,10.43 4,12A8,8 0 0,0 12,20V23L16,19L12,15V18M12,4V1L8,5L12,9V6A6,6 0 0,1 18,12C18,13 17.75,13.97 17.3,14.8L18.76,16.26C19.54,15.03 20,13.57 20,12A8,8 0 0,0 12,4Z</Geometry>
        <Geometry x:Key='Icon_Magic'>M12.5,5.6L10,0L7.5,5.6L1.9,8.1L7.5,10.6L10,16.2L12.5,10.6L18.1,8.1L12.5,5.6M19.1,11.9L17.5,8.1L15.9,11.9L12.1,13.5L15.9,15.1L17.5,18.9L19.1,15.1L22.9,13.5L19.1,11.9Z</Geometry>
        <Geometry x:Key='Icon_Plus'>M19,13H13V19H11V13H5V11H11V5H13V11H19V13Z</Geometry>
        <Geometry x:Key='Icon_Close'>M19,6.41L17.59,5L12,10.59L6.41,5L5,6.41L10.59,12L5,17.59L6.41,19L12,13.41L17.59,19L19,17.59L13.41,12L19,6.41Z</Geometry>

        <!-- Styles -->
        <Style x:Key='NavButtonStyle' TargetType='RadioButton'>
            <Setter Property='Background' Value='Transparent'/><Setter Property='Foreground' Value='#888'/><Setter Property='Height' Value='40'/><Setter Property='Margin' Value='0,0,0,5'/>
            <Setter Property='Template'>
                <Setter.Value>
                    <ControlTemplate TargetType='RadioButton'>
                        <Border x:Name='BtnBorder' Background='{TemplateBinding Background}' CornerRadius='8'>
                            <Grid>
                                <Border x:Name='ActiveBar' Width='3' Background='#3B82F6' HorizontalAlignment='Left' Visibility='Collapsed' Margin='0,8,0,8'/>
                                <StackPanel Orientation='Horizontal' Margin='15,0,0,0' VerticalAlignment='Center' x:Name='BtnStack'>
                                    <Viewbox Width='16' Height='16'><Path Data='{Binding Tag, RelativeSource={RelativeSource TemplatedParent}}' Fill='{TemplateBinding Foreground}' Stretch='Uniform'/></Viewbox>
                                    <TextBlock x:Name='BtnText' Text='{TemplateBinding Content}' Margin='15,0,0,0' FontSize='14' VerticalAlignment='Center' Foreground='{TemplateBinding Foreground}' FontWeight='Medium'/>
                                </StackPanel>
                            </Grid>
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property='IsMouseOver' Value='True'><Setter Property='Background' Value='#1A1A1A'/><Setter Property='Foreground' Value='White'/></Trigger>
                            <Trigger Property='IsChecked' Value='True'><Setter Property='Background' Value='#252525'/><Setter Property='Foreground' Value='White'/><Setter TargetName='ActiveBar' Property='Visibility' Value='Visible'/></Trigger>
                            <DataTrigger Binding='{Binding Content, RelativeSource={RelativeSource Self}}' Value=''>
                                <Setter TargetName='BtnText' Property='Visibility' Value='Collapsed'/>
                                <Setter TargetName='BtnStack' Property='Margin' Value='0,0,0,0'/>
                                <Setter TargetName='BtnStack' Property='HorizontalAlignment' Value='Center'/>
                            </DataTrigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>

        <Style x:Key='NoteItemStyle' TargetType='ListBoxItem'>
            <Setter Property='Background' Value='Transparent'/><Setter Property='Margin' Value='0,0,0,10'/><Setter Property='Padding' Value='0'/><Setter Property='BorderThickness' Value='0'/><Setter Property='Template'>
                <Setter.Value>
                    <ControlTemplate TargetType='ListBoxItem'>
                        <Border x:Name='ItemBorder' Background='{TemplateBinding Background}' CornerRadius='10' Padding='15'>
                            <Grid>
                                <Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions>
                                <StackPanel>
                                    <TextBlock Text='{Binding Title}' Foreground='White' FontWeight='Bold' FontSize='13' Margin='0,0,0,5'/>
                                    <TextBlock Text='{Binding Preview}' Foreground='#888' FontSize='12' TextTrimming='CharacterEllipsis'/>
                                </StackPanel>
                                <Viewbox Grid.Column='1' Width='12' Height='12' VerticalAlignment='Top' Visibility='{Binding IsPinned, Converter={StaticResource BoolToVis}}'>
                                    <Path Data='{StaticResource Icon_Star}' Fill='#E0AB2B' Stretch='Uniform'/>
                                </Viewbox>
                            </Grid>
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property='IsSelected' Value='True'><Setter Property='Background' Value='#252525'/></Trigger>
                            <Trigger Property='IsMouseOver' Value='True'><Setter Property='Background' Value='#1A1A1A'/></Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
    </Window.Resources>

    <Border CornerRadius='16' Background='{StaticResource BgDark}' BorderBrush='#333' BorderThickness='1'>
        <Grid>
            <Grid.ColumnDefinitions>
                <ColumnDefinition x:Name='ColNav' Width='200'/><ColumnDefinition Width='250'/><ColumnDefinition Width='*'/>
            </Grid.ColumnDefinitions>

            <!-- Column 0: Navigation -->
            <Grid x:Name='NavGrid' Grid.Column='0' Margin='5,20'>
                <Grid.RowDefinitions><RowDefinition Height='Auto'/><RowDefinition Height='*'/><RowDefinition Height='Auto'/></Grid.RowDefinitions>
                <StackPanel Orientation='Horizontal' Margin='10,0,0,30' x:Name='TitlePanel'>
                    <TextBlock x:Name='AppTitle' Text='HF Note' Foreground='White' FontSize='18' FontWeight='Bold' VerticalAlignment='Center'/>
                    <Button x:Name='BtnNavToggle' Width='28' Height='28' Background='Transparent' BorderThickness='0' Foreground='#888' Margin='12,0,0,0' Cursor='Hand' VerticalAlignment='Center' Padding='0'>
                        <Viewbox Width='16' Height='16'>
                            <Path Data='M3,6H21V8H3V6M3,11H21V13H3V11M3,16H21V18H3V16Z' Fill='#888' Stretch='Uniform'/>
                        </Viewbox>
                    </Button>
                </StackPanel>
                <StackPanel Grid.Row='1'>
                    <RadioButton x:Name='NavAll' Content='全部笔记' Tag='{StaticResource Icon_All}' Style='{StaticResource NavButtonStyle}' IsChecked='True'/>
                    <RadioButton x:Name='NavPinned' Content='已置顶' Tag='{StaticResource Icon_Star}' Style='{StaticResource NavButtonStyle}'/>
                    <RadioButton x:Name='NavTrash' Content='回收站' Tag='{StaticResource Icon_Trash}' Style='{StaticResource NavButtonStyle}'/>
                </StackPanel>
                <Button x:Name='BtnNewNote' Grid.Row='2' Height='40' Background='#252525' Foreground='White' BorderThickness='0' HorizontalAlignment='Stretch' Margin='0,0,0,5'>
                    <Button.Template>
                        <ControlTemplate TargetType='Button'>
                            <Border Background='{TemplateBinding Background}' CornerRadius='10'>
                                <StackPanel x:Name='NewNoteStack' Orientation='Horizontal' HorizontalAlignment='Left' Margin='15,0,0,0'>
                                    <Viewbox Width='16' Height='16' x:Name='NewNoteIcon'><Path Data='{StaticResource Icon_Plus}' Fill='White' Stretch='Uniform'/></Viewbox>
                                    <TextBlock x:Name='TxtNewNote' Text='新建笔记' VerticalAlignment='Center' Margin='8,0,0,0'/>
                                </StackPanel>
                            </Border>
                            <ControlTemplate.Triggers>
                                <Trigger Property='IsMouseOver' Value='True'><Setter Property='Background' Value='#333'/></Trigger>
                                <Trigger Property='Tag' Value='collapsed'>
                                    <Setter TargetName='TxtNewNote' Property='Visibility' Value='Collapsed'/>
                                    <Setter TargetName='NewNoteStack' Property='HorizontalAlignment' Value='Center'/>
                                    <Setter TargetName='NewNoteStack' Property='Margin' Value='0'/>
                                </Trigger>
                            </ControlTemplate.Triggers>
                        </ControlTemplate>
                    </Button.Template>
                </Button>
            </Grid>

            <!-- Column 1: List -->
            <Grid Grid.Column='1' Background='#0A0A0A'>
                <Grid.RowDefinitions><RowDefinition Height='Auto'/><RowDefinition Height='*'/></Grid.RowDefinitions>
                <Border Grid.Row='0' Background='#1A1A1A' CornerRadius='8' Height='36' Margin='15,20,15,10'>
                    <Grid>
                        <TextBlock Text='搜索笔记...' Foreground='#444' Margin='10,0' VerticalAlignment='Center' IsHitTestVisible='False'>
                            <TextBlock.Style>
                                <Style TargetType='TextBlock'>
                                    <Setter Property='Visibility' Value='Collapsed'/>
                                    <Style.Triggers><DataTrigger Binding='{Binding Text, ElementName=SearchBox}' Value=''><Setter Property='Visibility' Value='Visible'/></DataTrigger></Style.Triggers>
                                </Style>
                            </TextBlock.Style>
                        </TextBlock>
                        <TextBox x:Name='SearchBox' Background='Transparent' BorderThickness='0' Foreground='#CCC' VerticalContentAlignment='Center' Margin='10,0' FontSize='13' CaretBrush='White'/>
                    </Grid>
                </Border>
                <ListBox x:Name='NotesList' Grid.Row='1' Background='Transparent' BorderThickness='0' ItemContainerStyle='{StaticResource NoteItemStyle}' Margin='10,0' ScrollViewer.HorizontalScrollBarVisibility='Disabled'/>
            </Grid>

            <!-- Column 2: Editor -->
            <Grid Grid.Column='2' x:Name='EditorPanel'>
                <Grid.RowDefinitions><RowDefinition Height='60'/><RowDefinition Height='Auto'/><RowDefinition Height='*'/><RowDefinition Height='Auto'/></Grid.RowDefinitions>
                
                <!-- Toolbar -->
                <Grid Grid.Row='0' Margin='30,20,20,0'>
                    <StackPanel Orientation='Horizontal' HorizontalAlignment='Left'>
                        <TextBlock x:Name='SaveStatusText' Foreground='#999' FontSize='12' VerticalAlignment='Center' Text='就绪'/>
                    </StackPanel>
                    <StackPanel Orientation='Horizontal' HorizontalAlignment='Right'>
                        <Button x:Name='BtnSync' Background='Transparent' BorderThickness='0' Margin='0,0,10,0' ToolTip='同步到云端' Width='28' Height='28' Cursor='Hand'>
                            <Viewbox Width='16' Height='16'><Path Data='{StaticResource Icon_Sync}' Fill='#888' Stretch='Uniform'/></Viewbox>
                        </Button>
                        <Button x:Name='BtnPin' Background='Transparent' BorderThickness='0' Margin='0,0,10,0' Width='28' Height='28' Cursor='Hand'>
                            <Viewbox Width='14' Height='14'><Path x:Name='PinIcon' Data='{StaticResource Icon_Pin}' Fill='#888' Stretch='Uniform'/></Viewbox>
                        </Button>
                        <Button x:Name='BtnDelete' Background='Transparent' BorderThickness='0' Margin='0,0,10,0' Width='28' Height='28' Cursor='Hand' ToolTip='删除'>
                            <Viewbox Width='14' Height='14'><Path Data='{StaticResource Icon_Trash}' Fill='#888' Stretch='Uniform'/></Viewbox>
                        </Button>
                        <Button x:Name='BtnAI' Content='AI 润色' Foreground='White' Padding='12,5' BorderThickness='0' Cursor='Hand' FontWeight='SemiBold' FontSize='12' MinWidth='72' Height='30'>
                            <Button.Background><StaticResource ResourceKey='GradientAI'/></Button.Background>
                            <Button.Template>
                                <ControlTemplate TargetType='Button'>
                                    <Border Background='{TemplateBinding Background}' CornerRadius='8'>
                                        <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
                                    </Border>
                                </ControlTemplate>
                            </Button.Template>
                        </Button>
                        <Button x:Name='BtnClose' Margin='10,0,0,0' Background='Transparent' BorderThickness='0' Width='28' Height='28' Cursor='Hand'>
                            <Viewbox Width='12' Height='12'><Path Data='{StaticResource Icon_Close}' Fill='#666' Stretch='Uniform'/></Viewbox>
                        </Button>
                    </StackPanel>
                </Grid>

                <TextBox x:Name='TxtTitle' Grid.Row='1' FontSize='24' FontWeight='Bold' Background='Transparent' BorderThickness='0' Foreground='#E0E0E0' Margin='30,0,30,15' CaretBrush='White'/>
                
                <Grid Grid.Row='2' Margin='30,0,30,10'>
                    <TextBox x:Name='TxtContent' AcceptsReturn='True' TextWrapping='Wrap' Background='Transparent' BorderThickness='0' Foreground='#CCC' FontSize='15' VerticalScrollBarVisibility='Auto' CaretBrush='White'/>
                    <!-- Inner Search Ctrl+F Panel -->
                    <Border x:Name='InNoteSearchPanel' Visibility='Collapsed' VerticalAlignment='Top' HorizontalAlignment='Right' Background='#2A2A2A' CornerRadius='6' Padding='8' Margin='0,0,20,0'>
                        <StackPanel Orientation='Horizontal'>
                            <TextBox x:Name='InNoteSearchBox' Width='150' Background='#1A1A1A' Foreground='White' BorderThickness='0' Padding='5'/>
                            <Button x:Name='BtnCloseSearch' Content='×' Foreground='#888' Margin='5,0,0,0' Background='Transparent' BorderThickness='0' Width='20'/>
                        </StackPanel>
                    </Border>
                </Grid>
                
                <TextBlock x:Name='TxtDate' Grid.Row='3' FontSize='12' Foreground='#666' Margin='30,0,30,10' HorizontalAlignment='Right'/>
            </Grid>
            <!-- Resize Grip -->
            <Rectangle x:Name='ResizeGrip' Grid.Column='2' Width='20' Height='20' HorizontalAlignment='Right' VerticalAlignment='Bottom' Fill='Transparent' Cursor='SizeNWSE'/>
        </Grid>
    </Border>
</Window>";

        // --- 4. 初始化窗口与控件引用 ---
        Window win = (Window)XamlReader.Parse(xaml);
        win.Width = winWidth; win.Height = winHeight;
        if (!double.IsNaN(winLeft)) win.Left = winLeft;
        if (!double.IsNaN(winTop)) win.Top = winTop;
        
        var colNav = (ColumnDefinition)win.FindName("ColNav");
        var appTitle = (TextBlock)win.FindName("AppTitle");
        var btnNavToggle = (Button)win.FindName("BtnNavToggle");
        var btnNewNote = (Button)win.FindName("BtnNewNote");
        var titlePanel = (StackPanel)win.FindName("TitlePanel");
        
        var list = (ListBox)win.FindName("NotesList");
        var txtTitle = (TextBox)win.FindName("TxtTitle");
        var txtContent = (TextBox)win.FindName("TxtContent");
        var txtDate = (TextBlock)win.FindName("TxtDate");
        var saveStatusText = (TextBlock)win.FindName("SaveStatusText");
        var pinIcon = (System.Windows.Shapes.Path)win.FindName("PinIcon");
        var searchBox = (TextBox)win.FindName("SearchBox");
        var inNoteSearchPanel = (Border)win.FindName("InNoteSearchPanel");
        var inNoteSearchBox = (TextBox)win.FindName("InNoteSearchBox");

        // 应用初始折叠状态
        void UpdateNavVisual(bool collapsed) {
            colNav.Width = new GridLength(collapsed ? 60 : 200);
            appTitle.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
            btnNewNote.Tag = collapsed ? "collapsed" : "expanded";
            btnNewNote.HorizontalAlignment = collapsed ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
            btnNewNote.Width = collapsed ? 44 : double.NaN;
            btnNewNote.Margin = collapsed ? new Thickness(8, 0, 8, 5) : new Thickness(0, 0, 0, 5);
            titlePanel.HorizontalAlignment = collapsed ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
            titlePanel.Margin = new Thickness(collapsed ? 0 : 10, 0, 0, 30);
        }
        UpdateNavVisual(isNavCollapsed);

        // --- 5. 核心逻辑功能 ---

        bool _isInternalUpdate = false;
        bool _editorDirty = false;
        bool _hasPendingSync = false;
        bool _isSyncRunning = false;
        TimeSpan autoSyncInterval = TimeSpan.FromSeconds(45);
        bool _closingSyncStarted = false;

        // 加载数据
        void LoadData() {
            if (!File.Exists(localNotesFile)) File.WriteAllText(localNotesFile, "[]");
            var json = File.ReadAllText(localNotesFile, Encoding.UTF8);
            var notes = ParseNotesFlexible(json, false);
            noteData.Clear();
            foreach(var n in notes) noteData.Add(n);
            RefreshList();
        }

        // 刷新列表
        void RefreshList() {
            var query = (searchBox.Text ?? "").ToLower();
            var filtered = noteData.Where(n => {
                bool tabMatch = currentFilter == "trash" ? n.IsDeleted : (!n.IsDeleted && (currentFilter == "all" || (currentFilter == "pinned" && n.IsPinned)));
                bool searchMatch = string.IsNullOrEmpty(query) || (n.Title ?? "").ToLower().Contains(query) || (n.Content ?? "").ToLower().Contains(query);
                return tabMatch && searchMatch;
            }).OrderByDescending(n => n.IsPinned).ThenByDescending(n => n.UpdatedAt).ToList();
            list.ItemsSource = filtered;
        }

        // 保存逻辑
        var debounceTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        var autoSyncTimer = new System.Windows.Threading.DispatcherTimer { Interval = autoSyncInterval };

        void MarkPendingSyncAndRestartTimer() {
            _hasPendingSync = true;
            autoSyncTimer.Stop();
            autoSyncTimer.Start();
            saveStatusText.Text = $"Local changed, auto sync in {(int)autoSyncInterval.TotalSeconds} sec";
        }

        void StopAutoSyncTimer() {
            autoSyncTimer.Stop();
        }

        debounceTimer.Tick += (s, e) => {
            debounceTimer.Stop();
            bool changed = SaveToLocal();
            if (changed) {
                MarkPendingSyncAndRestartTimer();
            }
        };

        autoSyncTimer.Tick += async (s, e) => {
            autoSyncTimer.Stop();
            if (_isSyncRunning) return;
            if (!_hasPendingSync) return;
            if (_editorDirty) {
                bool changed = SaveToLocal();
                if (changed) {
                    MarkPendingSyncAndRestartTimer();
                    return;
                }
            }
            saveStatusText.Text = "Auto syncing...";
            await SyncToHF();
        };

        string NormalizeLineEndings(string value) {
            return (value ?? "").Replace("\r\n", "\n").Replace("\r", "\n");
        }

        string SerializeNotesUnified(IEnumerable<NoteModel> notes) {
            var data = notes.Select(n => new {
                id = n.Id ?? "",
                title = n.Title ?? "",
                content = NormalizeLineEndings(n.Content ?? ""),
                updated_at = n.UpdatedAt == DateTime.MinValue ? "" : new DateTimeOffset(n.UpdatedAt).ToString("o"),
                is_pinned = n.IsPinned,
                is_deleted = n.IsDeleted
            }).ToList();
            return JsonConvert.SerializeObject(data, Formatting.Indented);
        }

        bool SaveToLocal(bool touchTimestamp = true) {
            if (_isInternalUpdate) return false;

            bool changed = false;
            var selected = list.SelectedItem as NoteModel;
            if (selected != null && !selected.IsDeleted) {
                string newTitle = txtTitle.Text ?? "";
                string newContent = NormalizeLineEndings(txtContent.Text ?? "");
                string oldTitle = selected.Title ?? "";
                string oldContent = NormalizeLineEndings(selected.Content ?? "");

                changed = !string.Equals(oldTitle, newTitle, StringComparison.Ordinal) ||
                          !string.Equals(oldContent, newContent, StringComparison.Ordinal);

                selected.Title = newTitle;
                selected.Content = newContent;
                if (changed && touchTimestamp) {
                    selected.UpdatedAt = DateTime.Now;
                    WriteLog("INFO", $"[LOCAL] Note changed, touch UpdatedAt. id={selected.Id}");
                }
            }

            string before = File.Exists(localNotesFile) ? File.ReadAllText(localNotesFile, Encoding.UTF8) : "";
            string after = SerializeNotesUnified(noteData);
            bool fileChanged = !string.Equals(before, after, StringComparison.Ordinal);
            changed = changed || fileChanged;

            File.WriteAllText(localNotesFile, after, new UTF8Encoding(false));
            _editorDirty = false;
            saveStatusText.Text = "Local saved " + DateTime.Now.ToString("HH:mm:ss");
            return changed;
        }

        DateTime ParseDateSafe(string raw, bool assumeUtcForPlain) {
            if (string.IsNullOrWhiteSpace(raw)) return DateTime.MinValue;

            raw = raw.Trim();
            DateTime dt;

            // Plain datetime has no timezone. For remote payload use UTC; for local file use local.
            if (Regex.IsMatch(raw, "^\\d{4}-\\d{2}-\\d{2} \\d{2}:\\d{2}:\\d{2}$")) {
                if (DateTime.TryParseExact(raw, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out dt)) {
                    if (assumeUtcForPlain) {
                        return DateTime.SpecifyKind(dt, DateTimeKind.Utc).ToLocalTime();
                    }
                    return DateTime.SpecifyKind(dt, DateTimeKind.Local);
                }
            }

            // Local Quicker writes "yyyy-MM-ddTHH:mm:ss" without offset; treat as local.
            if (Regex.IsMatch(raw, "^\\d{4}-\\d{2}-\\d{2}T\\d{2}:\\d{2}:\\d{2}(\\.\\d+)?$")) {
                if (DateTime.TryParseExact(raw, new[] { "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ss.fff" }, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out dt)) {
                    return dt;
                }
            }

            // ISO8601 with offset / Z
            DateTimeOffset dto;
            if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out dto)) {
                return dto.LocalDateTime;
            }

            if (DateTime.TryParse(raw, out dt)) return dt;
            return DateTime.MinValue;
        }

        NoteModel CloneNote(NoteModel n) {
            return new NoteModel {
                Id = n.Id ?? "",
                Title = n.Title ?? "",
                Content = n.Content ?? "",
                UpdatedAt = n.UpdatedAt,
                IsPinned = n.IsPinned,
                IsDeleted = n.IsDeleted
            };
        }

        List<NoteModel> ParseNotesFlexible(string json, bool assumeUtcForPlain = false) {
            var result = new List<NoteModel>();
            try {
                var arr = JToken.Parse(json) as JArray;
                if (arr == null) return result;
                foreach (var t in arr) {
                    var o = t as JObject;
                    if (o == null) continue;

                    string id = (o["Id"] ?? o["id"])?.ToString() ?? "";
                    if (string.IsNullOrWhiteSpace(id)) continue;

                    string title = (o["Title"] ?? o["title"])?.ToString() ?? "";
                    string content = (o["Content"] ?? o["content"])?.ToString() ?? "";
                    string updatedRaw = (o["UpdatedAt"] ?? o["updated_at"])?.ToString() ?? "";

                    bool isPinned = false;
                    var p1 = o["IsPinned"]; var p2 = o["is_pinned"];
                    if (p1 != null) isPinned = p1.Value<bool>();
                    else if (p2 != null) isPinned = p2.Value<bool>();

                    bool isDeleted = false;
                    var d1 = o["IsDeleted"]; var d2 = o["is_deleted"];
                    if (d1 != null) isDeleted = d1.Value<bool>();
                    else if (d2 != null) isDeleted = d2.Value<bool>();

                    result.Add(new NoteModel {
                        Id = id,
                        Title = title,
                        Content = content,
                        UpdatedAt = ParseDateSafe(updatedRaw, assumeUtcForPlain),
                        IsPinned = isPinned,
                        IsDeleted = isDeleted
                    });
                }
            } catch (Exception ex) {
                WriteLog("WARN", "ParseNotesFlexible failed.", ex);
            }
            return result;
        }

        List<NoteModel> MergeNotesByLatest(List<NoteModel> localNotes, List<NoteModel> remoteNotes, string syncId) {
            var map = new Dictionary<string, NoteModel>(StringComparer.OrdinalIgnoreCase);

            foreach (var n in localNotes) {
                if (n == null || string.IsNullOrWhiteSpace(n.Id)) continue;
                map[n.Id] = CloneNote(n);
            }

            foreach (var r in remoteNotes) {
                if (r == null || string.IsNullOrWhiteSpace(r.Id)) continue;

                if (!map.ContainsKey(r.Id)) {
                    map[r.Id] = CloneNote(r);
                    WriteLog("INFO", $"[{syncId}] MERGE_DECISION id={r.Id} winner=remote reason=remote_only remoteUpdated={r.UpdatedAt:O}");
                    continue;
                }

                var l = map[r.Id];
                string lc = NormalizeLineEndings(l.Content ?? "");
                string rc = NormalizeLineEndings(r.Content ?? "");

                string winner = "local";
                string reason = "local_newer";

                if (r.UpdatedAt > l.UpdatedAt) {
                    winner = "remote";
                    reason = "remote_newer";
                    map[r.Id] = CloneNote(r);
                } else if (r.UpdatedAt == l.UpdatedAt) {
                    bool sameTitle = string.Equals(l.Title ?? "", r.Title ?? "", StringComparison.Ordinal);
                    bool sameContent = string.Equals(lc, rc, StringComparison.Ordinal);
                    bool samePinned = l.IsPinned == r.IsPinned;
                    bool sameDeleted = l.IsDeleted == r.IsDeleted;

                    if (!(sameTitle && sameContent && samePinned && sameDeleted)) {
                        winner = "local";
                        reason = "same_time_local_preferred";
                    } else {
                        winner = "local";
                        reason = "same_time_same_content";
                    }
                }

                WriteLog(
                    "INFO",
                    $"[{syncId}] MERGE_DECISION id={r.Id} winner={winner} reason={reason} localUpdated={l.UpdatedAt:O} remoteUpdated={r.UpdatedAt:O} localLen={lc.Length} remoteLen={rc.Length}"
                );
            }

            return map.Values
                .OrderByDescending(n => n.IsPinned)
                .ThenByDescending(n => n.UpdatedAt)
                .ToList();
        }

        async Task<Tuple<List<NoteModel>, string>> PullRemoteNotes(HttpClient client, string syncId) {
            string pathEscaped = string.Join("/", remoteNotesPath.Split('/').Select(Uri.EscapeDataString));
            string pullUrl = $"https://huggingface.co/datasets/{datasetId}/resolve/main/{pathEscaped}";
            WriteLog("INFO", $"[{syncId}] GET {pullUrl}");

            var resp = await client.GetAsync(pullUrl);
            if (resp.StatusCode == HttpStatusCode.NotFound) {
                WriteLog("WARN", $"[{syncId}] Remote notes not found. Treat as empty.");
                return Tuple.Create(new List<NoteModel>(), "[]");
            }

            string body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) {
                throw new Exception($"Failed to pull remote notes ({(int)resp.StatusCode} {resp.StatusCode}): {body}");
            }

            var notes = ParseNotesFlexible(body, true);
            WriteLog("INFO", $"[{syncId}] Pull success. remoteCount={notes.Count}");
            return Tuple.Create(notes, body);
        }

        async Task SyncToHF() {
            if (_isSyncRunning) {
                WriteLog("INFO", "[SYNC] Skip, another sync is running.");
                return;
            }
            _isSyncRunning = true;
            string syncId = Guid.NewGuid().ToString("N").Substring(0, 8);
            WriteLog("INFO", $"[{syncId}] Sync start. dataset={datasetId}, remotePath={remoteNotesPath}, localFile={localNotesFile}");
            if (string.IsNullOrEmpty(token)) {
                WriteLog("WARN", $"[{syncId}] HF_TOKEN missing.");
                MessageBox.Show("HF_TOKEN is missing. Please set environment variable HF_TOKEN.");
                _isSyncRunning = false;
                return;
            }

            debounceTimer.Stop();
            if (_editorDirty) {
                bool touched = SaveToLocal(true);
                WriteLog("INFO", $"[{syncId}] Pre-sync flush from editor. changed={touched}");
            } else {
                File.WriteAllText(localNotesFile, SerializeNotesUnified(noteData), new UTF8Encoding(false));
                WriteLog("INFO", $"[{syncId}] Pre-sync flush without editor changes.");
            }
            string selectedId = (list.SelectedItem as NoteModel)?.Id;
            saveStatusText.Text = "Syncing (pull + merge + push)...";

            try {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                ServicePointManager.Expect100Continue = false;
                WriteLog("INFO", $"[{syncId}] SecurityProtocol set to TLS1.2.");

                try {
                    var addrs = Dns.GetHostAddresses("huggingface.co");
                    WriteLog("INFO", $"[{syncId}] DNS huggingface.co => {string.Join(", ", addrs.Select(a => a.ToString()))}");
                } catch (Exception dnsEx) {
                    WriteLog("WARN", $"[{syncId}] DNS resolve failed for huggingface.co.", dnsEx);
                }

                var handler = new HttpClientHandler {
                    UseProxy = true,
                    UseDefaultCredentials = true,
                    PreAuthenticate = true
                };

                string proxyFromEnv = Environment.GetEnvironmentVariable("HF_PROXY_URL");
                if (!string.IsNullOrWhiteSpace(proxyFromEnv)) {
                    handler.Proxy = new WebProxy(proxyFromEnv);
                    WriteLog("INFO", $"[{syncId}] Proxy from HF_PROXY_URL: {proxyFromEnv}");
                } else if (IsTcpOpen("127.0.0.1", 7890)) {
                    handler.Proxy = new WebProxy("http://127.0.0.1:7890");
                    WriteLog("INFO", $"[{syncId}] Proxy fallback selected: http://127.0.0.1:7890");
                } else {
                    handler.Proxy = WebRequest.DefaultWebProxy;
                    if (handler.Proxy != null) {
                        WriteLog("INFO", $"[{syncId}] Proxy from system: {handler.Proxy.GetProxy(new Uri("https://huggingface.co"))}");
                    } else {
                        WriteLog("INFO", $"[{syncId}] No proxy configured.");
                    }
                }
                if (handler.Proxy != null) {
                    handler.Proxy.Credentials = CredentialCache.DefaultCredentials;
                }

                using (var client = new HttpClient(handler)) {
                    client.Timeout = TimeSpan.FromSeconds(90);
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("HFNoteSync/3.0");
                    WriteLog("INFO", $"[{syncId}] HttpClient ready. timeout={client.Timeout.TotalSeconds}s");

                    var localJson = File.Exists(localNotesFile) ? File.ReadAllText(localNotesFile, Encoding.UTF8) : "[]";
                    var localNotes = ParseNotesFlexible(localJson, false);
                    var pullResult = await PullRemoteNotes(client, syncId);
                    var remoteNotes = pullResult.Item1;
                    var remoteRawJson = pullResult.Item2;

                    WriteJsonSnapshot(syncId, "LOCAL_JSON_BEFORE_MERGE", localJson);
                    WriteJsonSnapshot(syncId, "REMOTE_JSON_PULLED", remoteRawJson);

                    var mergedNotes = MergeNotesByLatest(localNotes, remoteNotes, syncId);
                    WriteLog("INFO", $"[{syncId}] Merge done. local={localNotes.Count}, remote={remoteNotes.Count}, merged={mergedNotes.Count}");

                    var mergedJson = SerializeNotesUnified(mergedNotes);
                    WriteJsonSnapshot(syncId, "MERGED_JSON", mergedJson);
                    File.WriteAllText(localNotesFile, mergedJson, new UTF8Encoding(false));

                    LoadData();
                    if (!string.IsNullOrWhiteSpace(selectedId)) {
                        var selected = noteData.FirstOrDefault(n => n.Id == selectedId);
                        if (selected != null) list.SelectedItem = selected;
                    }

                    if (list.SelectedItem is NoteModel currentNote) {
                        _isInternalUpdate = true;
                        txtTitle.Text = currentNote.Title;
                        txtContent.Text = currentNote.Content;
                        txtDate.Text = currentNote.UpdatedAt.ToString("yyyy/MM/dd HH:mm");
                        pinIcon.Fill = currentNote.IsPinned ? (Brush)new BrushConverter().ConvertFromString("#E0AB2B") : Brushes.Gray;
                        _isInternalUpdate = false;
                    }

                    byte[] bytes = File.ReadAllBytes(localNotesFile);
                    string b64 = Convert.ToBase64String(bytes);
                    string now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
                    WriteLog("INFO", $"[{syncId}] Upload merged bytes={bytes.Length}, base64Length={b64.Length}");

                    string[] lines = new[] {
                        JsonConvert.SerializeObject(new { key = "header", value = new { summary = "Bi-directional sync from HF Note Sync", description = "Sync at " + now } }),
                        JsonConvert.SerializeObject(new { key = "file", value = new { path = remoteNotesPath, content = b64, encoding = "base64" } })
                    };
                    string payload = string.Join("\n", lines) + "\n";

                    string commitUrl = $"https://huggingface.co/api/datasets/{datasetId}/commit/main";
                    WriteLog("INFO", $"[{syncId}] POST {commitUrl}");

                    HttpResponseMessage resp = null;
                    Exception lastEx = null;
                    for (int attempt = 1; attempt <= 2; attempt++) {
                        try {
                            var sw = Stopwatch.StartNew();
                            var contentAttempt = new ByteArrayContent(Encoding.UTF8.GetBytes(payload));
                            contentAttempt.Headers.ContentType = new MediaTypeHeaderValue("application/x-ndjson");
                            resp = await client.PostAsync(commitUrl, contentAttempt);
                            sw.Stop();
                            WriteLog("INFO", $"[{syncId}] Upload attempt {attempt} completed in {sw.ElapsedMilliseconds} ms.");
                            break;
                        } catch (Exception ex) {
                            lastEx = ex;
                            WriteLog("WARN", $"[{syncId}] Upload attempt {attempt} failed.", ex);
                            if (attempt < 2) await Task.Delay(1500);
                        }
                    }
                    if (resp == null && lastEx != null) throw lastEx;

                    if (resp.IsSuccessStatusCode) {
                        string body = await resp.Content.ReadAsStringAsync();
                        WriteLog("INFO", $"[{syncId}] Sync success. status={(int)resp.StatusCode} body={body}");
                        saveStatusText.Text = "Bi-directional sync success " + DateTime.Now.ToString("HH:mm:ss");
                        _hasPendingSync = false;
                        StopAutoSyncTimer();
                    } else {
                        string body = await resp.Content.ReadAsStringAsync();
                        saveStatusText.Text = "Cloud sync failed";
                        WriteLog("WARN", $"[{syncId}] Sync failed. status={(int)resp.StatusCode} {resp.StatusCode}, body={body}");
                        MessageBox.Show($"Sync failed ({(int)resp.StatusCode} {resp.StatusCode}):\n{body}");
                    }
                }
            } catch (HttpRequestException ex) {
                saveStatusText.Text = "Sync error";
                var detail = ex.InnerException != null ? $"{ex.Message}\nInner: {ex.InnerException.Message}" : ex.Message;
                WriteLog("ERROR", $"[{syncId}] HttpRequestException.", ex);
                MessageBox.Show("Request failed. Check network/proxy access to huggingface.co.\n\n" + detail);
            } catch (TaskCanceledException ex) {
                saveStatusText.Text = "Sync error";
                WriteLog("ERROR", $"[{syncId}] TaskCanceledException (timeout or cancel).", ex);
                MessageBox.Show("Sync timed out (90s). Please retry.\n\n" + ex.Message);
            } catch (Exception ex) {
                saveStatusText.Text = "Sync error";
                WriteLog("ERROR", $"[{syncId}] Unexpected exception.", ex);
                MessageBox.Show(ex.Message);
            } finally {
                _isSyncRunning = false;
            }
        }


        async Task SyncToHFInBackgroundOnClose() {
            string syncId = Guid.NewGuid().ToString("N").Substring(0, 8);
            WriteLog("INFO", $"[{syncId}] Close-triggered background sync start.");
            if (string.IsNullOrEmpty(token)) {
                WriteLog("WARN", $"[{syncId}] Skip close background sync: HF_TOKEN missing.");
                return;
            }

            try {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                ServicePointManager.Expect100Continue = false;

                var handler = new HttpClientHandler {
                    UseProxy = true,
                    UseDefaultCredentials = true,
                    PreAuthenticate = true
                };

                string proxyFromEnv = Environment.GetEnvironmentVariable("HF_PROXY_URL");
                if (!string.IsNullOrWhiteSpace(proxyFromEnv)) {
                    handler.Proxy = new WebProxy(proxyFromEnv);
                } else if (IsTcpOpen("127.0.0.1", 7890)) {
                    handler.Proxy = new WebProxy("http://127.0.0.1:7890");
                } else {
                    handler.Proxy = WebRequest.DefaultWebProxy;
                }
                if (handler.Proxy != null) {
                    handler.Proxy.Credentials = CredentialCache.DefaultCredentials;
                }

                using (var client = new HttpClient(handler)) {
                    client.Timeout = TimeSpan.FromSeconds(90);
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("HFNoteSync/3.0");

                    var localJson = File.Exists(localNotesFile) ? File.ReadAllText(localNotesFile, Encoding.UTF8) : "[]";
                    var localNotes = ParseNotesFlexible(localJson, false);
                    var pullResult = await PullRemoteNotes(client, syncId);
                    var remoteNotes = pullResult.Item1;
                    var remoteRawJson = pullResult.Item2;

                    var mergedNotes = MergeNotesByLatest(localNotes, remoteNotes, syncId);
                    var mergedJson = SerializeNotesUnified(mergedNotes);
                    WriteJsonSnapshot(syncId, "CLOSE_SYNC_LOCAL_JSON", localJson);
                    WriteJsonSnapshot(syncId, "CLOSE_SYNC_REMOTE_JSON", remoteRawJson);
                    WriteJsonSnapshot(syncId, "CLOSE_SYNC_MERGED_JSON", mergedJson);

                    File.WriteAllText(localNotesFile, mergedJson, new UTF8Encoding(false));

                    byte[] bytes = Encoding.UTF8.GetBytes(mergedJson);
                    string b64 = Convert.ToBase64String(bytes);
                    string now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
                    string[] linesCommit = new[] {
                        JsonConvert.SerializeObject(new { key = "header", value = new { summary = "Close background sync from HF Note Sync", description = "Sync at " + now } }),
                        JsonConvert.SerializeObject(new { key = "file", value = new { path = remoteNotesPath, content = b64, encoding = "base64" } })
                    };
                    string payload = string.Join("\n", linesCommit) + "\n";

                    string commitUrl = $"https://huggingface.co/api/datasets/{datasetId}/commit/main";
                    HttpResponseMessage resp = null;
                    Exception lastEx = null;
                    for (int attempt = 1; attempt <= 2; attempt++) {
                        try {
                            var contentAttempt = new ByteArrayContent(Encoding.UTF8.GetBytes(payload));
                            contentAttempt.Headers.ContentType = new MediaTypeHeaderValue("application/x-ndjson");
                            resp = await client.PostAsync(commitUrl, contentAttempt);
                            break;
                        } catch (Exception ex) {
                            lastEx = ex;
                            WriteLog("WARN", $"[{syncId}] Close-sync upload attempt {attempt} failed.", ex);
                            if (attempt < 2) await Task.Delay(1200);
                        }
                    }
                    if (resp == null && lastEx != null) throw lastEx;

                    string body = await resp.Content.ReadAsStringAsync();
                    if (resp.IsSuccessStatusCode) {
                        WriteLog("INFO", $"[{syncId}] Close-triggered background sync success. status={(int)resp.StatusCode} body={body}");
                    } else {
                        WriteLog("WARN", $"[{syncId}] Close-triggered background sync failed. status={(int)resp.StatusCode} {resp.StatusCode}, body={body}");
                    }
                }
            } catch (Exception ex) {
                WriteLog("ERROR", $"[{syncId}] Close-triggered background sync exception.", ex);
            }
        }

        // --- 6. 事件绑定 ---

        btnNavToggle.Click += (s, e) => {
            isNavCollapsed = !isNavCollapsed;
            UpdateNavVisual(isNavCollapsed);
        };

        ((RadioButton)win.FindName("NavAll")).Checked += (s, e) => { currentFilter = "all"; RefreshList(); };
        ((RadioButton)win.FindName("NavPinned")).Checked += (s, e) => { currentFilter = "pinned"; RefreshList(); };
        ((RadioButton)win.FindName("NavTrash")).Checked += (s, e) => { currentFilter = "trash"; RefreshList(); };

        searchBox.TextChanged += (s, e) => RefreshList();

        list.SelectionChanged += (s, e) => {
            _isInternalUpdate = true;
            if (list.SelectedItem is NoteModel note) {
                txtTitle.Text = note.Title;
                txtContent.Text = note.Content;
                txtDate.Text = note.UpdatedAt.ToString("yyyy/MM/dd HH:mm");
                pinIcon.Fill = note.IsPinned ? (Brush)new BrushConverter().ConvertFromString("#E0AB2B") : Brushes.Gray;
                txtTitle.IsEnabled = true; txtContent.IsEnabled = true;
            } else {
                txtTitle.Text = ""; txtContent.Text = ""; txtDate.Text = "";
                txtTitle.IsEnabled = false; txtContent.IsEnabled = false;
            }
            _isInternalUpdate = false;
            _editorDirty = false;
        };

        txtTitle.TextChanged += (s, e) => {
            if(!_isInternalUpdate) {
                _editorDirty = true;
                _hasPendingSync = true;
                debounceTimer.Stop();
                debounceTimer.Start();
                autoSyncTimer.Stop();
                autoSyncTimer.Start();
            }
        };
        txtContent.TextChanged += (s, e) => {
            if(!_isInternalUpdate) {
                _editorDirty = true;
                _hasPendingSync = true;
                debounceTimer.Stop();
                debounceTimer.Start();
                autoSyncTimer.Stop();
                autoSyncTimer.Start();
            }
        };

        btnNewNote.Click += (s, e) => {
            var newNote = new NoteModel { Title = "新笔记", Content = "", UpdatedAt = DateTime.Now, Id = Guid.NewGuid().ToString("N") };
            noteData.Insert(0, newNote);
            currentFilter = "all";
            RefreshList();
            list.SelectedItem = newNote;
            txtTitle.Focus();
            if (SaveToLocal()) {
                MarkPendingSyncAndRestartTimer();
            }
        };

        ((Button)win.FindName("BtnSync")).Click += async (s, e) => await SyncToHF();
        
        ((Button)win.FindName("BtnPin")).Click += (s, e) => {
            if (list.SelectedItem is NoteModel note) {
                note.IsPinned = !note.IsPinned;
                note.UpdatedAt = DateTime.Now;
                pinIcon.Fill = note.IsPinned ? (Brush)new BrushConverter().ConvertFromString("#E0AB2B") : Brushes.Gray;
                if (SaveToLocal()) {
                    MarkPendingSyncAndRestartTimer();
                }
                RefreshList();
                list.SelectedItem = note;
            }
        };

        ((Button)win.FindName("BtnDelete")).Click += (s, e) => {
            if (list.SelectedItem is NoteModel note) {
                if (currentFilter == "trash") {
                    if (MessageBox.Show("彻底删除笔记？", "确认", MessageBoxButton.YesNo) == MessageBoxResult.Yes) {
                        noteData.Remove(note);
                    }
                } else {
                    note.IsDeleted = true;
                    note.IsPinned = false;
                }
                if (SaveToLocal()) {
                    MarkPendingSyncAndRestartTimer();
                }
                RefreshList();
            }
        };

        ((Button)win.FindName("BtnAI")).Click += async (s, e) => {
            var btn = (Button)s;
            btn.IsEnabled = false; btn.Content = "正在润色...";
            try {
                using (var client = new HttpClient()) {
                    client.Timeout = TimeSpan.FromSeconds(60);
                    var payload = new {
                        model = "deepseek-chat",
                        messages = new[] { 
                            new { role = "system", content = "你是一个专业的文字润色助手。直接返回润色后的文本，不要解释。" },
                            new { role = "user", content = txtContent.Text }
                        }
                    };
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "sk-any");
                    var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
                    var resp = await client.PostAsync("http://127.0.0.1:55555/v1/chat/completions", content);
                    if (resp.IsSuccessStatusCode) {
                        var resJson = JObject.Parse(await resp.Content.ReadAsStringAsync());
                        txtContent.Text = resJson["choices"]?[0]?["message"]?["content"]?.ToString();
                        if (SaveToLocal()) {
                            MarkPendingSyncAndRestartTimer();
                        }
                    }
                }
            } catch { MessageBox.Show("AI 接口连接失败。请确保 Quicker 转发器已开启。"); }
            finally { btn.IsEnabled = true; btn.Content = "AI 润色"; }
        };

        // 窗口管理
        win.PreviewKeyDown += (s, e) => {
            if (e.Key == Key.F && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control) {
                inNoteSearchPanel.Visibility = Visibility.Visible;
                inNoteSearchBox.Focus();
                e.Handled = true;
            } else if (e.Key == Key.Escape) {
                inNoteSearchPanel.Visibility = Visibility.Collapsed;
            }
        };
        ((Button)win.FindName("BtnCloseSearch")).Click += (s, e) => inNoteSearchPanel.Visibility = Visibility.Collapsed;
        ((Button)win.FindName("BtnClose")).Click += (s, e) => win.Close();
        win.MouseLeftButtonDown += (s, e) => { if (e.OriginalSource is Border) win.DragMove(); };

        // Resize
        var resizeGrip = (System.Windows.Shapes.Rectangle)win.FindName("ResizeGrip");
        bool isResizing = false; Point resizeStart = new Point(); Size startSize = new Size();
        resizeGrip.MouseLeftButtonDown += (s, e) => { isResizing = true; resizeStart = e.GetPosition(win); startSize = new Size(win.Width, win.Height); resizeGrip.CaptureMouse(); e.Handled = true; };
        resizeGrip.MouseMove += (s, e) => { if (isResizing) { var p = e.GetPosition(win); double w = startSize.Width + (p.X - resizeStart.X); double h = startSize.Height + (p.Y - resizeStart.Y); if (w > 600) win.Width = w; if (h > 400) win.Height = h; } };
        resizeGrip.MouseLeftButtonUp += (s, e) => { isResizing = false; resizeGrip.ReleaseMouseCapture(); };

        win.Closing += (s, e) => {
            StopAutoSyncTimer();
            File.WriteAllText(configFile, $"Width={win.Width}\nHeight={win.Height}\nLeft={win.Left}\nTop={win.Top}\nIsNavCollapsed={isNavCollapsed}");
            SaveToLocal();

            if (!_closingSyncStarted) {
                _closingSyncStarted = true;
                _hasPendingSync = true;
                _ = Task.Run(async () => await SyncToHFInBackgroundOnClose());
            }
        };

        // 初始化
        LoadData();
        if (noteData.Count > 0) list.SelectedIndex = 0;
        else txtTitle.IsEnabled = false; txtContent.IsEnabled = false;
        
        win.Show();
        win.Activate();
    });
}

public class NoteModel : INotifyPropertyChanged {
    public string Id { get; set; }
    public string Title { get; set; }
    public string Content { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsPinned { get; set; }
    public bool IsDeleted { get; set; }
    [JsonIgnore] public string Preview => string.IsNullOrWhiteSpace(Content) ? "暂无内容" : (Content.Length > 40 ? Content.Substring(0, 40).Replace("\r","").Replace("\n", " ") + "..." : Content.Replace("\r","").Replace("\n", " "));
    public event PropertyChangedEventHandler PropertyChanged;
}






