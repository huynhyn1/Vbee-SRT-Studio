using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace VbeeSrtStudio;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
            return SelfTests.Run();

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }
}

internal sealed record SubtitleCue(int Index, TimeSpan Start, TimeSpan End, string Text);
internal sealed record Voice(string Name, string Code)
{
    public override string ToString() => $"{Name}  ·  {Code}";
}
internal sealed record VoiceCatalogPage(List<Voice> Voices, bool HasNextPage, string? NextCursor);

internal sealed class MainForm : Form
{
    private const int OutputSampleRate = 44100;
    private static readonly System.Drawing.Color Ink = System.Drawing.Color.FromArgb(25, 39, 57);
    private static readonly System.Drawing.Color Muted = System.Drawing.Color.FromArgb(103, 119, 139);
    private static readonly System.Drawing.Color Accent = System.Drawing.Color.FromArgb(0, 157, 145);
    private static readonly System.Drawing.Color Canvas = System.Drawing.Color.FromArgb(243, 246, 250);
    private readonly TextBox appIdBox = new() { PlaceholderText = "Nhập App ID của bạn", Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle };
    private readonly TextBox tokenBox = new() { PlaceholderText = "Dán token Vbee tại đây", UseSystemPasswordChar = true, Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle };
    private readonly Button loadVoicesButton = new() { Text = "Tải danh sách giọng", AutoSize = true };
    private readonly Button clearTokenButton = new() { Text = "Xóa token", AutoSize = true };
    private readonly ComboBox voiceBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat };
    private readonly Button copyVoiceCodeButton = new() { Text = "Sao chép ID", AutoSize = true, Enabled = false };
    private readonly Button loadSrtButton = new() { Text = "Mở phụ đề SRT", AutoSize = true };
    private readonly Label srtLabel = new() { Text = "Chưa chọn tệp — hỗ trợ SRT, UTF-8 và tiếng Việt", AutoEllipsis = true, Dock = DockStyle.Fill, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
    private readonly ListBox previewList = new() { Dock = DockStyle.Fill, HorizontalScrollbar = true, BorderStyle = BorderStyle.None, IntegralHeight = false };
    private readonly Label previewCountLabel = new() { Text = "Chưa chọn phụ đề", Dock = DockStyle.Fill, ForeColor = Muted, TextAlign = System.Drawing.ContentAlignment.MiddleRight };
    private readonly NumericUpDown speedBox = new()
    {
        Minimum = 0.25m,
        Maximum = 1.9m,
        Increment = 0.05m,
        DecimalPlaces = 2,
        Value = 1.00m,
        Width = 90
    };
    private readonly TextBox outputBox = new() { ReadOnly = true, Dock = DockStyle.Fill };
    private readonly Button outputButton = new() { Text = "Lưu thành...", AutoSize = true };
    private readonly Button generateButton = new() { Text = "Tạo file WAV", AutoSize = true, Enabled = false };
    private readonly Button cancelButton = new() { Text = "Hủy", AutoSize = true, Enabled = false };
    private readonly ProgressBar progressBar = new() { Minimum = 0, Maximum = 100, Value = 0, Dock = DockStyle.Fill };
    private readonly Label statusLabel = new() { Text = "Chọn SRT và nhập thông tin ứng dụng Vbee.", Dock = DockStyle.Fill, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
    private readonly HttpClient httpClient = new(new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(30) })
    {
        Timeout = Timeout.InfiniteTimeSpan
    };

    private List<SubtitleCue> cues = [];
    private string? srtPath;
    private CancellationTokenSource? activeCancellation;

    public MainForm()
    {
        Text = "Vbee SRT Studio";
        Width = 1080;
        Height = 940;
        MinimumSize = new System.Drawing.Size(820, 640);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Canvas;
        ForeColor = Ink;
        Font = new System.Drawing.Font("Segoe UI", 9.5F);
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        AutoScaleMode = AutoScaleMode.Dpi;
        appIdBox.Font = new System.Drawing.Font("Segoe UI", 9.5F);
        tokenBox.Font = new System.Drawing.Font("Segoe UI", 9.5F);
        outputBox.Font = new System.Drawing.Font("Segoe UI", 9.5F);
        previewList.Font = new System.Drawing.Font("Segoe UI", 9.5F);
        previewList.BackColor = System.Drawing.Color.White;
        statusLabel.ForeColor = Muted;

        BuildLayout();
        FormClosing += (_, _) => activeCancellation?.Cancel();

        loadVoicesButton.Click += async (_, _) => await LoadVoicesAsync();
        clearTokenButton.Click += (_, _) =>
        {
            tokenBox.Clear();
            SetStatus("Token đã xóa khỏi phiên ứng dụng.");
            UpdateGenerateEnabled();
        };
        loadSrtButton.Click += (_, _) => ChooseSrt();
        outputButton.Click += (_, _) => ChooseOutput();
        generateButton.Click += async (_, _) => await GenerateAsync();
        cancelButton.Click += (_, _) => activeCancellation?.Cancel();
        appIdBox.TextChanged += (_, _) => UpdateGenerateEnabled();
        tokenBox.TextChanged += (_, _) => UpdateGenerateEnabled();
        voiceBox.SelectedIndexChanged += (_, _) => UpdateGenerateEnabled();
        voiceBox.SelectedIndexChanged += (_, _) => copyVoiceCodeButton.Enabled = voiceBox.SelectedItem is Voice;
        copyVoiceCodeButton.Click += (_, _) =>
        {
            if (voiceBox.SelectedItem is Voice voice)
            {
                Clipboard.SetText(voice.Code);
                SetStatus($"Đã sao chép ID giọng: {voice.Code}", success: true);
            }
        };
    }

    private void BuildLayout()
    {
        var scrollArea = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Canvas
        };
        var root = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            Padding = new Padding(22),
            ColumnCount = 1,
            RowCount = 6,
            BackColor = Canvas
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 162));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 250));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 162));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 116));
        scrollArea.Controls.Add(root);
        Controls.Add(scrollArea);

        var header = new Panel { Dock = DockStyle.Fill, BackColor = Ink, Padding = new Padding(20, 10, 20, 10) };
        var headerGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, BackColor = Ink };
        headerGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        headerGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        headerGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
        headerGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        headerGrid.Controls.Add(new Label
        {
            Text = "Vbee SRT Studio",
            Dock = DockStyle.Fill,
            ForeColor = System.Drawing.Color.White,
            Font = new System.Drawing.Font("Segoe UI Semibold", 21F, System.Drawing.FontStyle.Bold),
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft
        }, 0, 0);
        headerGrid.Controls.Add(new Label
        {
            Text = "Chuyển phụ đề thành giọng nói · nhanh gọn trên Windows",
            Dock = DockStyle.Fill,
            ForeColor = System.Drawing.Color.FromArgb(199, 212, 227),
            Font = new System.Drawing.Font("Segoe UI", 9.5F),
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft
        }, 0, 1);
        var badge = new Label
        {
            Text = "PORTABLE  ·  WINDOWS",
            AutoSize = true,
            ForeColor = System.Drawing.Color.White,
            BackColor = Accent,
            Font = new System.Drawing.Font("Segoe UI Semibold", 8.5F, System.Drawing.FontStyle.Bold),
            Padding = new Padding(10, 7, 10, 7),
            Anchor = AnchorStyles.Right
        };
        headerGrid.Controls.Add(badge, 1, 0);
        headerGrid.SetRowSpan(badge, 2);
        header.Controls.Add(headerGrid);
        root.Controls.Add(header, 0, 0);

        var credentials = CreateCard("01   KẾT NỐI VỚI VBEE");
        credentials.ColumnCount = 3;
        credentials.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        credentials.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        credentials.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        credentials.RowCount = 4;
        credentials.RowStyles.Clear();
        credentials.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        credentials.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        credentials.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        credentials.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        credentials.SetColumnSpan(credentials.GetControlFromPosition(0, 0)!, 3);
        AddField(credentials, 1, "App ID", appIdBox);
        AddField(credentials, 2, "Token", tokenBox);
        credentials.Controls.Add(loadVoicesButton, 2, 1);
        credentials.Controls.Add(clearTokenButton, 2, 2);
        var privacy = new Label
        {
            Text = "Bảo mật: App ID và token chỉ dùng trong phiên này; không được lưu vào tệp.",
            Dock = DockStyle.Fill,
            ForeColor = Muted,
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft
        };
        credentials.Controls.Add(privacy, 1, 3);
        credentials.SetColumnSpan(privacy, 2);
        root.Controls.Add(credentials, 0, 1);

        var srtRow = CreateCard("");
        srtRow.Padding = new Padding(12);
        srtRow.ColumnCount = 2;
        srtRow.RowCount = 1;
        srtRow.RowStyles.Clear();
        srtRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        srtRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        srtRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        srtRow.Controls.Add(loadSrtButton, 0, 0);
        srtRow.Controls.Add(srtLabel, 1, 0);
        root.Controls.Add(srtRow, 0, 2);

        var preview = CreateCard("02   NỘI DUNG PHỤ ĐỀ");
        preview.ColumnCount = 2;
        preview.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        preview.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        preview.RowCount = 3;
        preview.RowStyles.Clear();
        preview.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        preview.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        preview.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        preview.SetColumnSpan(preview.GetControlFromPosition(0, 0)!, 2);
        var previewHeader = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = System.Drawing.Color.White };
        previewHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        previewHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        previewHeader.Controls.Add(new Label
        {
            Text = "Xem trước các câu thoại",
            Dock = DockStyle.Fill,
            ForeColor = Ink,
            Font = new System.Drawing.Font("Segoe UI Semibold", 10F, System.Drawing.FontStyle.Bold),
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft
        }, 0, 0);
        previewHeader.Controls.Add(previewCountLabel, 1, 0);
        preview.Controls.Add(previewHeader, 0, 1);
        preview.SetColumnSpan(previewHeader, 2);
        preview.Controls.Add(previewList, 0, 2);
        preview.SetColumnSpan(previewList, 2);
        previewList.Items.Add("Chưa có phụ đề. Chọn “Mở phụ đề SRT” để xem trước nội dung.");
        root.Controls.Add(preview, 0, 3);

        var options = CreateCard("03   THIẾT LẬP ÂM THANH");
        options.ColumnCount = 5;
        options.RowCount = 4;
        options.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        options.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        options.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        options.RowStyles.Clear();
        options.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        options.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        options.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        options.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        options.SetColumnSpan(options.GetControlFromPosition(0, 0)!, 5);
        AddField(options, 1, "Giọng", voiceBox);
        options.Controls.Add(copyVoiceCodeButton, 2, 1);
        AddField(options, 1, "Tốc độ", speedBox, 3);
        options.Controls.Add(new Label
        {
            Text = "Tệp WAV",
            Dock = DockStyle.Fill,
            ForeColor = Muted,
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft
        }, 0, 2);
        options.Controls.Add(outputBox, 1, 2);
        options.SetColumnSpan(outputBox, 3);
        options.Controls.Add(outputButton, 4, 2);
        var timingNote = new Label
        {
            Text = "Khoảng lặng theo SRT; câu đọc dài hoặc chồng lấn được nối tiếp, không cắt lời.",
            Dock = DockStyle.Fill,
            ForeColor = Muted,
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft
        };
        options.Controls.Add(timingNote, 0, 3);
        options.SetColumnSpan(timingNote, 5);
        root.Controls.Add(options, 0, 4);

        var actions = CreateCard("");
        actions.Padding = new Padding(12);
        actions.ColumnCount = 3;
        actions.RowCount = 2;
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actions.RowStyles.Clear();
        actions.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        actions.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        actions.Controls.Add(progressBar, 0, 0);
        actions.Controls.Add(generateButton, 1, 0);
        actions.Controls.Add(cancelButton, 2, 0);
        actions.Controls.Add(statusLabel, 0, 1);
        actions.SetColumnSpan(statusLabel, 3);
        root.Controls.Add(actions, 0, 5);

        StyleButton(loadVoicesButton, false);
        StyleButton(clearTokenButton, false);
        StyleButton(loadSrtButton, true);
        StyleButton(outputButton, false);
        StyleButton(copyVoiceCodeButton, false);
        StyleButton(generateButton, true);
        StyleButton(cancelButton, false);
        progressBar.ForeColor = Accent;
        progressBar.Margin = new Padding(0, 8, 14, 8);
        AcceptButton = generateButton;
    }

    private static TableLayoutPanel CreateCard(string title)
    {
        var card = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = System.Drawing.Color.White,
            Padding = new Padding(14),
            ColumnCount = 1,
            RowCount = title.Length > 0 ? 2 : 1,
            Margin = new Padding(0, 0, 0, 8)
        };
        if (title.Length > 0)
        {
            card.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            card.Controls.Add(new Label
            {
                Text = title,
                Dock = DockStyle.Fill,
                ForeColor = Accent,
                Font = new System.Drawing.Font("Segoe UI Semibold", 9F, System.Drawing.FontStyle.Bold),
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft
            }, 0, 0);
            card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        }
        else
            card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        return card;
    }

    private static void StyleButton(Button button, bool primary)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = primary ? 0 : 1;
        button.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(211, 220, 230);
        button.FlatAppearance.MouseOverBackColor = primary
            ? System.Drawing.Color.FromArgb(0, 139, 128)
            : System.Drawing.Color.FromArgb(245, 248, 251);
        button.FlatAppearance.MouseDownBackColor = primary
            ? System.Drawing.Color.FromArgb(0, 119, 110)
            : System.Drawing.Color.FromArgb(234, 240, 246);
        button.BackColor = primary ? Accent : System.Drawing.Color.White;
        button.ForeColor = primary ? System.Drawing.Color.White : Ink;
        button.Font = new System.Drawing.Font("Segoe UI Semibold", 9F, System.Drawing.FontStyle.Bold);
        button.Padding = new Padding(12, 4, 12, 4);
        button.Margin = new Padding(5, 3, 5, 3);
        button.Cursor = Cursors.Hand;
    }

    private static void AddField(TableLayoutPanel table, int row, string labelText, Control field, int labelColumn = 0)
    {
        var label = new Label
        {
            Text = labelText,
            AutoSize = true,
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
            Dock = DockStyle.Fill,
            ForeColor = Muted
        };
        table.Controls.Add(label, labelColumn, row);
        table.Controls.Add(field, labelColumn + 1, row);
    }

    private void ChooseSrt()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "SubRip subtitle (*.srt)|*.srt|All files (*.*)|*.*",
            Title = "Chọn file phụ đề SRT",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            cues = SrtParser.ParseFile(dialog.FileName);
            srtPath = dialog.FileName;
            srtLabel.Text = $"{Path.GetFileName(dialog.FileName)} — {cues.Count:N0} đoạn";
            previewCountLabel.Text = $"{cues.Count:N0} đoạn · tối đa 2.000 dòng";
            outputBox.Text = Path.Combine(Path.GetDirectoryName(dialog.FileName)!, Path.GetFileNameWithoutExtension(dialog.FileName) + "_voice.wav");
            previewList.BeginUpdate();
            previewList.Items.Clear();
            foreach (var cue in cues.Take(2000))
                previewList.Items.Add($"{cue.Index}  {cue.Start:hh\\:mm\\:ss\\.fff}  {cue.Text}");
            previewList.EndUpdate();
            if (cues.Count > 2000)
                previewList.Items.Add($"… còn {cues.Count - 2000:N0} đoạn");
            SetStatus($"Đã tải {cues.Count:N0} đoạn phụ đề.", success: true);
            UpdateGenerateEnabled();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException or FormatException)
        {
            cues = [];
            srtPath = null;
            srtLabel.Text = "Không đọc được file SRT";
            previewCountLabel.Text = "Không đọc được phụ đề";
            previewList.Items.Clear();
            SetStatus("Không đọc được file SRT: " + ex.Message, error: true);
            UpdateGenerateEnabled();
        }
    }

    private void ChooseOutput()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "WAV audio (*.wav)|*.wav",
            DefaultExt = "wav",
            AddExtension = true,
            FileName = Path.GetFileName(outputBox.Text.Length == 0 ? "speech.wav" : outputBox.Text),
            Title = "Chọn file WAV đầu ra"
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            outputBox.Text = dialog.FileName;
    }

    private async Task LoadVoicesAsync()
    {
        if (!TryValidateCredentials())
            return;
        loadVoicesButton.Enabled = false;
        SetStatus("Đang tải danh sách giọng đọc từ Vbee…");
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var voices = new List<Voice>();
            var seenCursors = new HashSet<string>(StringComparer.Ordinal);
            string? cursor = null;
            var pageNumber = 1;
            while (true)
            {
                SetStatus($"Đang tải danh sách giọng đọc từ Vbee (trang {pageNumber})…");
                var url = "https://vbee.vn/api/public/v1/voices?voiceOwnership=VBEE&languageCode=vi-VN&limit=100";
                if (cursor is not null)
                    url += "&cursor=" + Uri.EscapeDataString(cursor);
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                AddAuthHeaders(request);
                using var response = await httpClient.SendAsync(request, timeout.Token);
                var body = await response.Content.ReadAsStringAsync(timeout.Token);
                if (!response.IsSuccessStatusCode)
                    throw new VbeeApiException(FormatApiError(response.StatusCode, body));

                var page = VoiceParser.ParsePage(body);
                voices.AddRange(page.Voices);
                if (!page.HasNextPage)
                    break;
                cursor = page.NextCursor!;
                if (!seenCursors.Add(cursor))
                    throw new InvalidDataException("Vbee lặp lại con trỏ phân trang; không thể tải đầy đủ danh sách giọng.");
                pageNumber++;
            }
            if (voices.Count == 0)
                throw new InvalidDataException("Danh mục giọng Vbee trả về rỗng. Kiểm tra quyền App, ngôn ngữ và bộ lọc giọng.");
            voiceBox.BeginUpdate();
            voiceBox.Items.Clear();
            foreach (var voice in voices)
                voiceBox.Items.Add(voice);
            voiceBox.SelectedIndex = 0;
            voiceBox.EndUpdate();
            SetStatus($"Đã tải {voices.Count} giọng đọc.", success: true);
            UpdateGenerateEnabled();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidDataException or VbeeApiException)
        {
            SetStatus(FriendlyError(ex), error: true);
        }
        catch (JsonException)
        {
            SetStatus("Phản hồi danh sách giọng của Vbee không đúng định dạng JSON. Hãy thử lại sau.", error: true);
        }
        finally
        {
            loadVoicesButton.Enabled = true;
        }
    }

    private async Task GenerateAsync()
    {
        if (cues.Count == 0 || !TryValidateCredentials() || voiceBox.SelectedItem is not Voice voice)
            return;
        var outputPath = outputBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            SetStatus("Chọn nơi lưu file WAV.", error: true);
            return;
        }
        if (string.Equals(Path.GetFullPath(outputPath), Path.GetFullPath(srtPath!), StringComparison.OrdinalIgnoreCase))
        {
            SetStatus("File đầu ra không thể ghi đè lên SRT.", error: true);
            return;
        }

        activeCancellation = new CancellationTokenSource();
        SetBusy(true);
        progressBar.Value = 0;
        var tempPath = Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(outputPath))!,
            $".{Path.GetFileName(outputPath)}.{Guid.NewGuid():N}.tmp");
        var completedSamples = 0L;
        var delayedCueCount = 0;
        var totalChunks = cues.Sum(cue => TextChunker.Split(cue.Text).Count);
        var currentChunk = 0;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            {
                WavWriter.WriteHeader(stream, OutputSampleRate, 0);
                foreach (var cue in cues)
                {
                    activeCancellation.Token.ThrowIfCancellationRequested();
                    var startSample = (long)Math.Round(cue.Start.TotalSeconds * OutputSampleRate);
                    var delayedCue = startSample < completedSamples;
                    if (startSample > completedSamples)
                        completedSamples += WavWriter.WriteSilence(stream, startSample - completedSamples);

                    foreach (var chunk in TextChunker.Split(cue.Text))
                    {
                        activeCancellation.Token.ThrowIfCancellationRequested();
                        currentChunk++;
                        SetStatus($"Đang tạo: đoạn {cue.Index}/{cues.Count} ({currentChunk}/{totalChunks} phần)…");
                        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.vbee.vn/v1/tts");
                        AddAuthHeaders(request);
                        request.Content = JsonContent.Create(new
                        {
                            text = chunk,
                            voiceCode = voice.Code,
                            mode = "sync",
                            outputFormat = "wav",
                            speed = (double)speedBox.Value
                        });
                        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, activeCancellation.Token);
                        if (!response.IsSuccessStatusCode)
                        {
                            var errorBody = await response.Content.ReadAsStringAsync(activeCancellation.Token);
                            throw new VbeeApiException(FormatApiError(response.StatusCode, errorBody));
                        }
                        var bytes = await response.Content.ReadAsByteArrayAsync(activeCancellation.Token);
                        var samples = WavReader.ReadMonoPcm16(bytes, OutputSampleRate);
                        WavWriter.WriteSamples(stream, samples);
                        completedSamples += samples.Length;
                        progressBar.Value = totalChunks == 0 ? 100 : Math.Min(100, currentChunk * 100 / totalChunks);
                    }
                    if (completedSamples > Math.Round(cue.End.TotalSeconds * OutputSampleRate))
                        delayedCue = true;
                    if (delayedCue)
                        delayedCueCount++;
                }
                var subtitleEnd = (long)Math.Round(cues.Max(cue => cue.End).TotalSeconds * OutputSampleRate);
                if (subtitleEnd > completedSamples)
                    completedSamples += WavWriter.WriteSilence(stream, subtitleEnd - completedSamples);
                WavWriter.WriteHeader(stream, OutputSampleRate, completedSamples);
                await stream.FlushAsync(activeCancellation.Token);
            }

            File.Move(tempPath, outputPath, true);
            progressBar.Value = 100;
            SetStatus(delayedCueCount == 0
                ? $"Hoàn tất: {Path.GetFileName(outputPath)}. Khoảng lặng theo mốc SRT được giữ."
                : $"Hoàn tất: {Path.GetFileName(outputPath)}. {delayedCueCount} đoạn chồng lấn/đọc dài đã được nối tiếp, không cắt audio.",
                success: true);
        }
        catch (OperationCanceledException)
        {
            SetStatus("Đã hủy tạo âm thanh.");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or VbeeApiException or UnauthorizedAccessException or ArgumentException)
        {
            SetStatus(FriendlyError(ex), error: true);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
            activeCancellation.Dispose();
            activeCancellation = null;
            SetBusy(false);
        }
    }

    private void AddAuthHeaders(HttpRequestMessage request)
    {
        var token = tokenBox.Text.Trim();
        var authorization = token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? token : "Bearer " + token;
        request.Headers.TryAddWithoutValidation("Authorization", authorization);
        request.Headers.TryAddWithoutValidation("App-Id", appIdBox.Text.Trim());
    }

    private bool TryValidateCredentials()
    {
        if (string.IsNullOrWhiteSpace(appIdBox.Text) || string.IsNullOrWhiteSpace(tokenBox.Text))
        {
            SetStatus("Nhập App ID và token trong Vbee Studio.", error: true);
            return false;
        }
        return true;
    }

    private void SetStatus(string message, bool error = false, bool success = false)
    {
        statusLabel.Text = message;
        statusLabel.ForeColor = error
            ? System.Drawing.Color.FromArgb(180, 52, 59)
            : success ? System.Drawing.Color.FromArgb(0, 123, 108) : Muted;
    }

    private static string FriendlyError(Exception exception) =>
        exception is HttpRequestException or TaskCanceledException
            ? "Không thể kết nối tới Vbee. Kiểm tra kết nối Internet rồi thử lại."
            : exception.Message;

    private void UpdateGenerateEnabled() =>
        generateButton.Enabled = activeCancellation is null && cues.Count > 0 && !string.IsNullOrWhiteSpace(appIdBox.Text) &&
                                 !string.IsNullOrWhiteSpace(tokenBox.Text) && voiceBox.SelectedItem is Voice;

    private void SetBusy(bool busy)
    {
        cancelButton.Enabled = busy;
        loadVoicesButton.Enabled = !busy;
        loadSrtButton.Enabled = !busy;
        outputButton.Enabled = !busy;
        appIdBox.Enabled = !busy;
        tokenBox.Enabled = !busy;
        voiceBox.Enabled = !busy;
        speedBox.Enabled = !busy;
        clearTokenButton.Enabled = !busy;
        copyVoiceCodeButton.Enabled = !busy && voiceBox.SelectedItem is Voice;
        UpdateGenerateEnabled();
    }

    private string FormatApiError(HttpStatusCode status, string body)
    {
        var message = body;
        try
        {
            using var json = JsonDocument.Parse(body);
            if (VoiceParser.TryGet(json.RootElement, "error", out var error) &&
                VoiceParser.TryGet(error, "message", out var errorMessage) && errorMessage.ValueKind == JsonValueKind.String)
                message = errorMessage.GetString()!;
            else if (VoiceParser.TryGet(json.RootElement, "message", out var rootMessage) && rootMessage.ValueKind == JsonValueKind.String)
                message = rootMessage.GetString()!;
        }
        catch (JsonException)
        {
            message = body;
        }

        if (status == HttpStatusCode.Unauthorized)
            return "Vbee từ chối xác thực (401). Kiểm tra App ID và token trong Vbee Studio; token có thể hết hạn.";
        if (status == HttpStatusCode.Forbidden)
            return "Vbee không cho phép tải danh sách giọng (403). Kiểm tra quyền API của ứng dụng trong Vbee Studio.";
        if (status == HttpStatusCode.NotFound)
            return "Vbee không tìm thấy danh mục giọng (404). Hãy cập nhật ứng dụng hoặc liên hệ hỗ trợ Vbee.";
        if ((int)status == 429 || (message.Contains("rate", StringComparison.OrdinalIgnoreCase) && message.Contains("limit", StringComparison.OrdinalIgnoreCase)))
            return "Vbee đang giới hạn yêu cầu. Chờ một chút rồi thử tải danh sách giọng lại.";
        if (string.IsNullOrWhiteSpace(message))
            message = "Vbee trả về lỗi HTTP " + (int)status + ".";
        message = message.Replace(tokenBox.Text.Trim(), "[token]", StringComparison.Ordinal)
            .Replace(appIdBox.Text.Trim(), "[App ID]", StringComparison.Ordinal);
        return $"Vbee trả lỗi HTTP {(int)status}: {message}";
    }
}

internal static class SrtParser
{
    private static readonly Regex TimestampPattern = new(
        @"(?<start>\d{1,3}:\d{2}:\d{2}[,.]\d{1,3})\s*-->\s*(?<end>\d{1,3}:\d{2}:\d{2}[,.]\d{1,3})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex MarkupPattern = new(@"<[^>]*>|\{\\[^}]*\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static List<SubtitleCue> ParseFile(string path)
    {
        var bytes = File.ReadAllBytes(path);
        string text;
        if (bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble))
            text = new UTF8Encoding(false, true).GetString(bytes, Encoding.UTF8.Preamble.Length, bytes.Length - Encoding.UTF8.Preamble.Length);
        else if (bytes.AsSpan().StartsWith(Encoding.Unicode.Preamble))
            text = Encoding.Unicode.GetString(bytes, Encoding.Unicode.Preamble.Length, bytes.Length - Encoding.Unicode.Preamble.Length);
        else if (bytes.AsSpan().StartsWith(Encoding.BigEndianUnicode.Preamble))
            text = Encoding.BigEndianUnicode.GetString(bytes, Encoding.BigEndianUnicode.Preamble.Length, bytes.Length - Encoding.BigEndianUnicode.Preamble.Length);
        else
        {
            try { text = new UTF8Encoding(false, true).GetString(bytes); }
            catch (DecoderFallbackException)
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                text = Encoding.GetEncoding(1258).GetString(bytes);
            }
        }
        return Parse(text);
    }

    public static List<SubtitleCue> Parse(string text)
    {
        var result = new List<SubtitleCue>();
        var blocks = Regex.Split(text.TrimStart('\uFEFF').Replace("\r\n", "\n").Replace('\r', '\n'), @"\n\s*\n");
        var fallbackIndex = 0;
        foreach (var block in blocks)
        {
            var lines = block.Split('\n');
            var timeLineIndex = Array.FindIndex(lines, line => TimestampPattern.IsMatch(line));
            if (timeLineIndex < 0)
                continue;
            var match = TimestampPattern.Match(lines[timeLineIndex]);
            if (!TryParseTime(match.Groups["start"].Value, out var start) ||
                !TryParseTime(match.Groups["end"].Value, out var end) || end < start)
                continue;

            var content = string.Join(' ', lines.Skip(timeLineIndex + 1).Select(line => line.Trim()).Where(line => line.Length > 0));
            content = WebUtility.HtmlDecode(MarkupPattern.Replace(content, "")).Trim();
            if (content.Length == 0)
                continue;
            var index = int.TryParse(lines[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedIndex)
                ? parsedIndex
                : fallbackIndex + 1;
            result.Add(new SubtitleCue(index, start, end, content));
            fallbackIndex++;
        }
        if (result.Count == 0)
            throw new FormatException("Không tìm thấy đoạn phụ đề hợp lệ trong file SRT.");
        return result.OrderBy(cue => cue.Start).ThenBy(cue => cue.Index).ToList();
    }

    private static bool TryParseTime(string value, out TimeSpan time)
    {
        var normalized = value.Replace(',', '.');
        var parts = normalized.Split(':');
        var secondsParts = parts[2].Split('.');
        var fraction = secondsParts[1].PadRight(3, '0')[..3];
        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var hours) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) ||
            !int.TryParse(secondsParts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) ||
            !int.TryParse(fraction, NumberStyles.None, CultureInfo.InvariantCulture, out var milliseconds))
        {
            time = default;
            return false;
        }
        try
        {
            time = TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes) +
                   TimeSpan.FromSeconds(seconds) + TimeSpan.FromMilliseconds(milliseconds);
            return true;
        }
        catch (OverflowException)
        {
            time = default;
            return false;
        }
    }
}

internal static class TextChunker
{
    public static List<string> Split(string text)
    {
        var elements = new List<string>();
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
            elements.Add((string)enumerator.Current!);

        var chunks = new List<string>();
        var current = new StringBuilder(300);
        foreach (var element in elements)
        {
            if (current.Length + element.Length > 300)
            {
                AddChunk(chunks, current);
                current.Clear();
            }
            current.Append(element);
        }
        AddChunk(chunks, current);
        return chunks;
    }

    private static void AddChunk(List<string> chunks, StringBuilder current)
    {
        var chunk = current.ToString().Trim();
        if (chunk.Length > 0)
            chunks.Add(chunk);
    }
}

internal static class VoiceParser
{
    private static readonly string[] ArrayNames = ["voices", "voiceList", "items", "list", "records"];
    private static readonly string[] WrapperNames = ["result", "data", "payload"];
    private static readonly string[] CodeNames = ["voiceCode", "code", "voiceId", "id"];
    private static readonly string[] NameNames = ["name", "voiceName", "displayName", "title"];

    public static List<Voice> Parse(string json)
        => ParsePage(json).Voices;

    public static VoiceCatalogPage ParsePage(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!TryFindVoiceArray(document.RootElement, 0, out var voices, out var container))
            throw new InvalidDataException("Không nhận diện được mảng danh sách giọng trong phản hồi Vbee.");

        var parsed = new List<Voice>();
        foreach (var item in voices.EnumerateArray())
        {
            if (!TryGetString(item, CodeNames, out var code))
                continue;
            var name = TryGetString(item, NameNames, out var parsedName) ? parsedName : code;
            parsed.Add(new Voice(name, code));
        }
        var sorted = parsed.OrderBy(voice => voice.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        var hasNextPage = false;
        string? nextCursor = null;
        if (TryGet(container, "pagination", out var pagination))
        {
            if (TryGet(pagination, "has_next_page", out var hasNext) ||
                TryGet(pagination, "hasNextPage", out hasNext))
                hasNextPage = hasNext.ValueKind == JsonValueKind.True;
            if (TryGet(pagination, "next_cursor", out var cursor) ||
                TryGet(pagination, "nextCursor", out cursor))
                nextCursor = cursor.ValueKind == JsonValueKind.String ? cursor.GetString() : null;
        }
        if (hasNextPage && string.IsNullOrWhiteSpace(nextCursor))
            throw new InvalidDataException("Vbee báo còn trang giọng tiếp theo nhưng không cung cấp con trỏ phân trang.");
        return new VoiceCatalogPage(sorted, hasNextPage, nextCursor);
    }

    private static bool TryFindVoiceArray(JsonElement element, int depth, out JsonElement voices, out JsonElement container)
    {
        voices = default;
        container = default;
        if (depth > 8)
            return false;
        if (element.ValueKind == JsonValueKind.Array)
        {
            if (element.GetArrayLength() == 0 || element.EnumerateArray().Any(IsVoiceObject))
            {
                voices = element;
                container = default;
                return true;
            }
            return false;
        }
        if (element.ValueKind != JsonValueKind.Object)
            return false;

        foreach (var propertyName in ArrayNames)
        {
            if (TryGet(element, propertyName, out var value) && value.ValueKind == JsonValueKind.Array)
            {
                voices = value;
                container = element;
                return true;
            }
        }
        foreach (var propertyName in WrapperNames)
        {
            if (TryGet(element, propertyName, out var value) &&
                TryFindVoiceArray(value, depth + 1, out voices, out container))
                return true;
        }
        return false;
    }

    private static bool IsVoiceObject(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object && TryGetString(element, CodeNames, out _);

    private static bool TryGetString(JsonElement element, IEnumerable<string> names, out string value)
    {
        foreach (var name in names)
        {
            if (!TryGet(element, name, out var property))
                continue;
            if (property.ValueKind == JsonValueKind.String)
                value = property.GetString() ?? "";
            else if (property.ValueKind == JsonValueKind.Number)
                value = property.GetRawText();
            else
                continue;
            if (value.Length > 0)
                return true;
        }
        value = "";
        return false;
    }

    public static bool TryGet(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }
        value = default;
        return false;
    }
}

internal static class WavReader
{
    public static short[] ReadMonoPcm16(byte[] wav, int targetSampleRate)
    {
        if (wav.Length < 44 || Encoding.ASCII.GetString(wav, 0, 4) != "RIFF" || Encoding.ASCII.GetString(wav, 8, 4) != "WAVE")
            throw new InvalidDataException("Phản hồi audio không phải WAV hợp lệ.");
        var offset = 12;
        ushort format = 0, channels = 0, bitsPerSample = 0;
        uint sampleRate = 0;
        byte[]? data = null;
        while (offset + 8 <= wav.Length)
        {
            var chunkId = Encoding.ASCII.GetString(wav, offset, 4);
            var length = BitConverter.ToUInt32(wav, offset + 4);
            offset += 8;
            if (length > wav.Length - offset)
                throw new InvalidDataException("Chunk WAV bị thiếu hoặc không đầy đủ.");
            if (chunkId == "fmt " && length >= 16)
            {
                format = BitConverter.ToUInt16(wav, offset);
                channels = BitConverter.ToUInt16(wav, offset + 2);
                sampleRate = BitConverter.ToUInt32(wav, offset + 4);
                bitsPerSample = BitConverter.ToUInt16(wav, offset + 14);
                if (format == 0xFFFE && length >= 40)
                    format = BitConverter.ToUInt16(wav, offset + 24);
            }
            else if (chunkId == "data")
                data = wav.AsSpan(offset, (int)length).ToArray();
            offset += (int)length + ((length & 1) == 1 ? 1 : 0);
        }

        if (data is null || channels is 0 or > 8 || sampleRate == 0 ||
            (format != 1 && format != 3) || (bitsPerSample != 8 && bitsPerSample != 16 && bitsPerSample != 24 && bitsPerSample != 32))
            throw new InvalidDataException("Định dạng WAV của Vbee không được hỗ trợ (cần PCM/float WAV).");
        if (format == 3 && bitsPerSample != 32)
            throw new InvalidDataException("WAV float không phải 32-bit.");
        var bytesPerSample = bitsPerSample / 8;
        var frameSize = bytesPerSample * channels;
        var frames = data.Length / frameSize;
        var mono = new short[frames];
        for (var frame = 0; frame < frames; frame++)
        {
            double sum = 0;
            for (var channel = 0; channel < channels; channel++)
                sum += ReadSample(data, frame * frameSize + channel * bytesPerSample, format, bitsPerSample);
            mono[frame] = (short)Math.Clamp((int)Math.Round(sum / channels), short.MinValue, short.MaxValue);
        }
        return Resample(mono, (int)sampleRate, targetSampleRate);
    }

    private static int ReadSample(byte[] data, int offset, ushort format, ushort bits)
    {
        if (format == 3)
        {
            var sample = BitConverter.ToSingle(data, offset);
            return float.IsFinite(sample) ? (int)Math.Clamp(Math.Round(sample * short.MaxValue), short.MinValue, short.MaxValue) : 0;
        }
        return bits switch
        {
            8 => (data[offset] - 128) << 8,
            16 => BitConverter.ToInt16(data, offset),
            24 => SignExtend24(data, offset) >> 8,
            32 => BitConverter.ToInt32(data, offset) >> 16,
            _ => throw new InvalidDataException("Bit depth WAV không được hỗ trợ.")
        };
    }

    private static int SignExtend24(byte[] data, int offset)
    {
        var value = data[offset] | data[offset + 1] << 8 | data[offset + 2] << 16;
        return (value & 0x800000) != 0 ? value | unchecked((int)0xFF000000) : value;
    }

    private static short[] Resample(short[] input, int sourceRate, int targetRate)
    {
        if (sourceRate == targetRate || input.Length == 0)
            return input;
        var outputLength = (int)Math.Ceiling(input.Length * (double)targetRate / sourceRate);
        var output = new short[outputLength];
        for (var i = 0; i < output.Length; i++)
        {
            var sourcePosition = i * (double)sourceRate / targetRate;
            var left = Math.Min((int)sourcePosition, input.Length - 1);
            var right = Math.Min(left + 1, input.Length - 1);
            var fraction = sourcePosition - left;
            output[i] = (short)Math.Clamp((int)Math.Round(input[left] * (1 - fraction) + input[right] * fraction), short.MinValue, short.MaxValue);
        }
        return output;
    }
}

internal static class WavWriter
{
    public static void WriteHeader(Stream stream, int sampleRate, long sampleCount)
    {
        var position = stream.Position;
        stream.Position = 0;
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        var dataBytes = checked((int)(sampleCount * 2));
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataBytes);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataBytes);
        stream.Position = Math.Max(position, 44);
    }

    public static long WriteSilence(Stream stream, long samples)
    {
        var zeroes = new byte[32 * 1024];
        var remaining = samples * 2;
        while (remaining > 0)
        {
            var count = (int)Math.Min(zeroes.Length, remaining);
            stream.Write(zeroes, 0, count);
            remaining -= count;
        }
        return samples;
    }

    public static void WriteSamples(Stream stream, short[] samples)
    {
        var bytes = new byte[32 * 1024];
        var index = 0;
        while (index < samples.Length)
        {
            var count = Math.Min(bytes.Length / 2, samples.Length - index);
            Buffer.BlockCopy(samples, index * 2, bytes, 0, count * 2);
            stream.Write(bytes, 0, count * 2);
            index += count;
        }
    }
}

internal sealed class VbeeApiException(string message) : Exception(message);

internal static class SelfTests
{
    public static int Run()
    {
        try
        {
            var cues = SrtParser.Parse("1\r\n00:00:02,000 --> 00:00:03,250\r\n<i>Xin chào</i>\r\n\r\n2\r\n00:00:05.000 --> 00:00:06.000\r\nVbee\r\n");
            Assert(cues.Count == 2 && cues[0].Text == "Xin chào" && cues[1].Start == TimeSpan.FromSeconds(5), "SRT parse and timing");
            var voices = VoiceParser.Parse("{\"result\":{\"voices\":[{\"voiceCode\":\"hn_female_001\",\"voiceName\":\"Giọng Hà Nội\"},{\"code\":\"sg_female_002\",\"name\":\"Giọng Sài Gòn\"}]}}");
            Assert(voices.Count == 2 && voices.Any(voice => voice.Name == "Giọng Hà Nội" && voice.Code == "hn_female_001"),
                "Vbee voice IDs and nested catalog shape");
            Assert(VoiceParser.Parse("{\"data\":{\"items\":[{\"id\":\"v2\",\"displayName\":\"Giọng 2\"}]}}")
                .Single().ToString() == "Giọng 2  ·  v2", "Voice ID display");
            var voicePage = VoiceParser.ParsePage("{\"result\":{\"pagination\":{\"has_next_page\":true,\"next_cursor\":\"next-page\"},\"voices\":[{\"code\":\"v3\",\"name\":\"Giọng trang sau\"}]}}");
            Assert(voicePage.HasNextPage && voicePage.NextCursor == "next-page" && voicePage.Voices.Single().Code == "v3",
                "Vbee cursor pagination");
            Assert(VoiceParser.Parse("{\"result\":{\"voices\":[]}}").Count == 0, "Empty voice catalog");
            var malformedVoiceJsonRejected = false;
            try { VoiceParser.Parse("{invalid"); }
            catch (JsonException) { malformedVoiceJsonRejected = true; }
            Assert(malformedVoiceJsonRejected, "Malformed voice catalog JSON");
            Assert(TextChunker.Split(new string('a', 301)).Select(s => s.Length).SequenceEqual(new[] { 300, 1 }), "300-character chunking");
            var wav = CreateSampleWav();
            var samples = WavReader.ReadMonoPcm16(wav, 8000);
            Assert(samples.Length == 4 && samples[0] == 1000 && samples[3] == -1000, "WAV PCM parsing");
            using var output = new MemoryStream();
            WavWriter.WriteHeader(output, 8000, 0);
            WavWriter.WriteSamples(output, samples);
            WavWriter.WriteHeader(output, 8000, samples.Length);
            Assert(output.Length == 44 + samples.Length * 2, "WAV writer header");
            Console.WriteLine("Self-tests passed: SRT parsing, voice catalog parsing/ID display, chunking, WAV parsing/writing.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Self-test failed: " + ex.Message);
            return 1;
        }
    }

    private static byte[] CreateSampleWav()
    {
        using var stream = new MemoryStream();
        WavWriter.WriteHeader(stream, 8000, 4);
        WavWriter.WriteSamples(stream, [1000, 500, -500, -1000]);
        return stream.ToArray();
    }

    private static void Assert(bool condition, string name)
    {
        if (!condition)
            throw new InvalidOperationException(name);
    }
}
