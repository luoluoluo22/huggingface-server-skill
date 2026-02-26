using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Quicker.Public;

public static void Exec(IStepContext context)
{
    Application.Current.Dispatcher.Invoke(() =>
    {
        string projectRoot = @"f:\Desktop\kaifa\huggingface-server-skill\example\hf-note-app";
        string dataDir = Path.Combine(projectRoot, "data");
        string localNotesFile = Path.Combine(dataDir, "notes.json");
        string datasetId = "mingyang22/huggingface-notes";
        string remoteNotesPath = "db/notes.json";
        string token = Environment.GetEnvironmentVariable("HF_TOKEN");

        Directory.CreateDirectory(dataDir);
        EnsureLocalStore(localNotesFile);

        string xaml = @"
<Window xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
        xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
        Title='HF 笔记同步客户端' Height='550' Width='750'
        WindowStartupLocation='CenterScreen' Topmost='True' Background='#1A1A1A' Foreground='White'>
    <Grid Margin='20'>
        <Grid.RowDefinitions>
            <RowDefinition Height='Auto'/>
            <RowDefinition Height='Auto'/>
            <RowDefinition Height='Auto'/>
            <RowDefinition Height='*'/>
            <RowDefinition Height='Auto'/>
        </Grid.RowDefinitions>

        <TextBlock Text='HF 笔记同步 (Pure C#)' FontSize='20' FontWeight='Bold' Margin='0,0,0,20' Foreground='#00A0FF'/>

        <Grid Grid.Row='1' Margin='0,0,0,10'>
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width='Auto'/>
                <ColumnDefinition Width='*'/>
                <ColumnDefinition Width='Auto'/>
            </Grid.ColumnDefinitions>
            <TextBlock Text='历史:' VerticalAlignment='Center' Width='40'/>
            <ComboBox x:Name='CbHistory' Grid.Column='1' Background='#2D2D2D' Foreground='White' BorderBrush='#444' Padding='5'/>
            <Button x:Name='BtnRefresh' Grid.Column='2' Content='刷新' Margin='10,0,0,0' Padding='10,5' Background='#333' Foreground='White' BorderThickness='0'/>
        </Grid>

        <StackPanel Grid.Row='2' Orientation='Horizontal' Margin='0,0,0,10'>
            <TextBlock Text='标题:' VerticalAlignment='Center' Width='40'/>
            <TextBox x:Name='TxtTitle' Width='450' Background='#2D2D2D' Foreground='White' BorderBrush='#444' Padding='5' VerticalContentAlignment='Center'/>
            <Button x:Name='BtnPull' Content='从云端恢复' Margin='10,0,0,0' Padding='10,5' Background='#333' Foreground='White' BorderThickness='0'/>
        </StackPanel>

        <TextBox x:Name='TxtContent' Grid.Row='3' AcceptsReturn='True' TextWrapping='Wrap'
                 Background='#2D2D2D' Foreground='White' BorderBrush='#444' Padding='10' Margin='0,0,0,20'
                 FontSize='14' VerticalScrollBarVisibility='Auto'/>

        <StackPanel Grid.Row='4' Orientation='Horizontal' HorizontalAlignment='Right'>
            <TextBlock x:Name='TxtStatus' Text='就绪' VerticalAlignment='Center' Margin='0,0,20,0' Foreground='#888'/>
            <Button x:Name='BtnNew' Content='新建' Width='80' Height='35' Margin='0,0,10,0' Background='#444' Foreground='White' BorderThickness='0'/>
            <Button x:Name='BtnSave' Content='仅保存本地' Width='120' Height='35' Margin='0,0,10,0' Background='#444' Foreground='White' BorderThickness='0'/>
            <Button x:Name='BtnSync' Content='同步至云端' Width='120' Height='35' Background='#00A0FF' Foreground='White' BorderThickness='0' FontWeight='Bold'/>
        </StackPanel>
    </Grid>
</Window>";

        Window win = (Window)XamlReader.Parse(xaml);
        win.Activate();
        win.Topmost = true;
        win.Topmost = false;
        win.Focus();

        TextBox txtTitle = (TextBox)win.FindName("TxtTitle");
        TextBox txtContent = (TextBox)win.FindName("TxtContent");
        Button btnSave = (Button)win.FindName("BtnSave");
        Button btnSync = (Button)win.FindName("BtnSync");
        Button btnPull = (Button)win.FindName("BtnPull");
        Button btnNew = (Button)win.FindName("BtnNew");
        Button btnRefresh = (Button)win.FindName("BtnRefresh");
        ComboBox cbHistory = (ComboBox)win.FindName("CbHistory");
        TextBlock txtStatus = (TextBlock)win.FindName("TxtStatus");

        int? currentNoteId = null;

        Action loadHistory = () =>
        {
            var notes = LoadNotes(localNotesFile);
            cbHistory.ItemsSource = notes;
            cbHistory.DisplayMemberPath = "title";
        };

        cbHistory.SelectionChanged += (s, e) =>
        {
            JObject note = cbHistory.SelectedItem as JObject;
            if (note == null)
            {
                return;
            }

            txtTitle.Text = note.Value<string>("title") ?? "";
            txtContent.Text = note.Value<string>("content") ?? "";
            currentNoteId = note.Value<int?>("id");
            txtStatus.Text = "已加载历史笔记: " + (note.Value<string>("title") ?? "未命名");
        };

        btnRefresh.Click += (s, e) => loadHistory();

        btnNew.Click += (s, e) =>
        {
            currentNoteId = null;
            txtTitle.Text = "新笔记";
            txtContent.Text = "";
            cbHistory.SelectedIndex = -1;
            txtStatus.Text = "已开启新笔记";
        };

        Action<bool> saveAction = isSync =>
        {
            string title = txtTitle.Text ?? "";
            string content = txtContent.Text ?? "";
            if (string.IsNullOrWhiteSpace(content))
            {
                MessageBox.Show("内容不能为空！");
                return;
            }

            txtStatus.Text = "正在保存本地...";
            var notes = LoadNotes(localNotesFile);

            if (currentNoteId.HasValue)
            {
                JObject existing = notes.FirstOrDefault(n => n.Value<int?>("id") == currentNoteId.Value);
                if (existing != null)
                {
                    existing["title"] = title;
                    existing["content"] = content;
                    existing["updated_at"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
                }
                else
                {
                    currentNoteId = null;
                }
            }

            if (!currentNoteId.HasValue)
            {
                int nextId = notes.Count == 0 ? 1 : notes.Max(n => n.Value<int?>("id") ?? 0) + 1;
                var item = new JObject();
                item["id"] = nextId;
                item["title"] = title;
                item["content"] = content;
                item["updated_at"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
                notes.Add(item);
                currentNoteId = nextId;
            }

            SaveNotes(localNotesFile, notes);

            if (isSync)
            {
                if (string.IsNullOrWhiteSpace(token))
                {
                    txtStatus.Text = "未设置 HF_TOKEN";
                    MessageBox.Show("未检测到 HF_TOKEN 环境变量，无法同步。", "同步失败");
                }
                else
                {
                    txtStatus.Text = "正在云端备份...";
                    string error;
                    bool ok = UploadNotesToHF(token, datasetId, remoteNotesPath, localNotesFile, out error);
                    if (ok)
                    {
                        txtStatus.Text = "云端同步成功 " + DateTime.Now.ToString("HH:mm:ss");
                        MessageBox.Show("同步成功！");
                    }
                    else
                    {
                        txtStatus.Text = "云端同步失败";
                        MessageBox.Show("同步失败: " + error, "同步失败");
                    }
                }
            }
            else
            {
                txtStatus.Text = "本地保存成功 " + DateTime.Now.ToString("HH:mm:ss");
            }

            loadHistory();
        };

        btnSave.Click += (s, e) => saveAction(false);
        btnSync.Click += (s, e) => saveAction(true);

        btnPull.Click += (s, e) =>
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                MessageBox.Show("未检测到 HF_TOKEN 环境变量，无法拉取。", "拉取失败");
                return;
            }

            var result = MessageBox.Show("确定要从云端恢复吗？这会覆盖本地 notes.json。", "确认恢复", MessageBoxButton.YesNo);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            txtStatus.Text = "正在从云端拉取...";
            string error;
            bool ok = DownloadNotesFromHF(token, datasetId, remoteNotesPath, localNotesFile, out error);
            if (ok)
            {
                txtStatus.Text = "已同步";
                loadHistory();
                MessageBox.Show("云端拉取完成。", "完成");
            }
            else
            {
                txtStatus.Text = "拉取失败";
                MessageBox.Show("云端拉取失败: " + error, "拉取失败");
            }
        };

        loadHistory();
        if (cbHistory.Items.Count > 0)
        {
            cbHistory.SelectedIndex = 0;
        }
        else
        {
            txtTitle.Text = "新笔记";
        }

        win.Show();
    });
}

private static void EnsureLocalStore(string localNotesFile)
{
    if (File.Exists(localNotesFile))
    {
        return;
    }

    File.WriteAllText(localNotesFile, "[]", new UTF8Encoding(false));
}

private static List<JObject> LoadNotes(string localNotesFile)
{
    EnsureLocalStore(localNotesFile);

    try
    {
        string json = File.ReadAllText(localNotesFile, Encoding.UTF8);
        JArray arr = JArray.Parse(string.IsNullOrWhiteSpace(json) ? "[]" : json);
        return arr
            .Children<JObject>()
            .OrderByDescending(n => ParseUpdatedAt(n.Value<string>("updated_at")))
            .ToList();
    }
    catch
    {
        return new List<JObject>();
    }
}

private static DateTime ParseUpdatedAt(string value)
{
    DateTime dt;
    if (DateTime.TryParse(value, out dt))
    {
        return dt;
    }

    return DateTime.MinValue;
}

private static void SaveNotes(string localNotesFile, List<JObject> notes)
{
    var ordered = notes
        .OrderByDescending(n => ParseUpdatedAt(n.Value<string>("updated_at")))
        .ToList();

    string json = JsonConvert.SerializeObject(ordered, Formatting.Indented);
    File.WriteAllText(localNotesFile, json, new UTF8Encoding(false));
}

private static bool DownloadNotesFromHF(string token, string datasetId, string remotePath, string localNotesFile, out string error)
{
    error = "";
    try
    {
        string url = "https://huggingface.co/datasets/" + datasetId + "/resolve/main/" + remotePath;
        using (var client = new HttpClient())
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("HFNoteSync/2.0");
            var response = client.GetAsync(url).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                error = ((int)response.StatusCode) + " " + response.ReasonPhrase;
                return false;
            }

            byte[] content = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            File.WriteAllBytes(localNotesFile, content);
            return true;
        }
    }
    catch (Exception ex)
    {
        error = ex.Message;
        return false;
    }
}

private static bool UploadNotesToHF(string token, string datasetId, string remotePath, string localNotesFile, out string error)
{
    error = "";
    try
    {
        string url = "https://huggingface.co/api/datasets/" + datasetId + "/commit/main";
        using (var client = new HttpClient())
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("HFNoteSync/2.0");

            byte[] bytes = File.ReadAllBytes(localNotesFile);
            string b64 = Convert.ToBase64String(bytes);
            string now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

            // Use Hub commit API (NDJSON) to avoid deprecated /upload endpoint.
            string[] lines = new[]
            {
                JsonConvert.SerializeObject(new
                {
                    key = "header",
                    value = new
                    {
                        summary = "Update notes.json from Quicker",
                        description = "HFNoteSync C# commit at " + now
                    }
                }),
                JsonConvert.SerializeObject(new
                {
                    key = "file",
                    value = new
                    {
                        path = remotePath,
                        content = b64,
                        encoding = "base64"
                    }
                })
            };
            string payload = string.Join("\n", lines) + "\n";

            var raw = new ByteArrayContent(Encoding.UTF8.GetBytes(payload));
            raw.Headers.ContentType = new MediaTypeHeaderValue("application/x-ndjson");
            var response = client.PostAsync(url, raw).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                error = ((int)response.StatusCode) + " " + response.ReasonPhrase + " " + body;
                return false;
            }

            return true;
        }
    }
    catch (Exception ex)
    {
        error = ex.Message;
        return false;
    }
}
