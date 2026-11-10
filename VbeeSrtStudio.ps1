[CmdletBinding()]
param(
    [string]$SrtPath,
    [string]$OutputPath,
    [string]$VoiceCode,
    [switch]$SelfTest
)

$ErrorActionPreference = 'Stop'
$script:VbeeAppId = $null
$script:VbeeToken = $null
$script:VbeeHttpClient = $null
$script:OutputSampleRate = 44100

function Read-VbeeTextFile {
    param([Parameter(Mandatory = $true)][string]$Path)

    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
        return [Text.Encoding]::UTF8.GetString($bytes, 3, $bytes.Length - 3)
    }
    if ($bytes.Length -ge 2 -and $bytes[0] -eq 0xFF -and $bytes[1] -eq 0xFE) {
        return [Text.Encoding]::Unicode.GetString($bytes, 2, $bytes.Length - 2)
    }
    if ($bytes.Length -ge 2 -and $bytes[0] -eq 0xFE -and $bytes[1] -eq 0xFF) {
        return [Text.Encoding]::BigEndianUnicode.GetString($bytes, 2, $bytes.Length - 2)
    }
    try {
        $strictUtf8 = New-Object Text.UTF8Encoding($false, $true)
        return $strictUtf8.GetString($bytes)
    }
    catch [Text.DecoderFallbackException] {
        return [Text.Encoding]::GetEncoding(1258).GetString($bytes)
    }
}

function ConvertTo-VbeeMilliseconds {
    param([Parameter(Mandatory = $true)][string]$Timestamp)

    if ($Timestamp -notmatch '^(?<hours>\d{1,3}):(?<minutes>\d{2}):(?<seconds>\d{2})[,.](?<fraction>\d{1,3})$') {
        throw "Mốc thời gian SRT không hợp lệ: $Timestamp"
    }
    $milliseconds = $Matches['fraction'].PadRight(3, '0')
    return (((([long]$Matches['hours'] * 60 + [long]$Matches['minutes']) * 60 + [long]$Matches['seconds']) * 1000) + [long]$milliseconds)
}

function ConvertFrom-VbeeSrt {
    param([Parameter(Mandatory = $true)][string]$Text)

    $normalized = $Text.TrimStart([char]0xFEFF).Replace("`r`n", "`n").Replace("`r", "`n")
    $blocks = [regex]::Split($normalized, '\n\s*\n')
    $cues = New-Object 'System.Collections.Generic.List[object]'
    $fallbackIndex = 0
    foreach ($block in $blocks) {
        $lines = $block -split "`n"
        $timeLine = -1
        for ($i = 0; $i -lt $lines.Length; $i++) {
            if ($lines[$i] -match '(?<start>\d{1,3}:\d{2}:\d{2}[,.]\d{1,3})\s*-->\s*(?<end>\d{1,3}:\d{2}:\d{2}[,.]\d{1,3})') {
                $timeLine = $i
                $startText = $Matches['start']
                $endText = $Matches['end']
                break
            }
        }
        if ($timeLine -lt 0) { continue }
        if ($timeLine + 1 -ge $lines.Length) { continue }

        $startMs = ConvertTo-VbeeMilliseconds $startText
        $endMs = ConvertTo-VbeeMilliseconds $endText
        if ($endMs -lt $startMs) { continue }
        $captionLines = @($lines[($timeLine + 1)..($lines.Length - 1)])
        $caption = [string]::Join(' ', @($captionLines | ForEach-Object { $_.Trim() } | Where-Object { $_.Length -gt 0 }))
        $caption = [regex]::Replace($caption, '<[^>]*>|\{\\[^}]*\}', '')
        $caption = [Net.WebUtility]::HtmlDecode($caption).Trim()
        if ([string]::IsNullOrWhiteSpace($caption)) { continue }

        $index = 0
        if ($lines[0].Trim() -match '^\d+$') { $index = [int]$lines[0].Trim() }
        else { $index = $fallbackIndex + 1 }
        $cues.Add([pscustomobject]@{
            Index = $index
            StartMs = $startMs
            EndMs = $endMs
            Text = $caption
        })
        $fallbackIndex++
    }
    if ($cues.Count -eq 0) { throw 'Không tìm thấy đoạn phụ đề SRT hợp lệ.' }
    return @($cues | Sort-Object StartMs, Index)
}

function Split-VbeeText {
    param([Parameter(Mandatory = $true)][string]$Text)

    $elements = [Globalization.StringInfo]::GetTextElementEnumerator($Text)
    $chunks = New-Object 'System.Collections.Generic.List[string]'
    $current = New-Object Text.StringBuilder
    while ($elements.MoveNext()) {
        $element = [string]$elements.GetTextElement()
        if ($current.Length + $element.Length -gt 300) {
            $part = $current.ToString().Trim()
            if ($part.Length -gt 0) { $chunks.Add($part) }
            [void]$current.Clear()
        }
        [void]$current.Append($element)
    }
    $part = $current.ToString().Trim()
    if ($part.Length -gt 0) { $chunks.Add($part) }
    return @($chunks)
}

function Get-VbeeAuthorizationHeader {
    if ([string]::IsNullOrWhiteSpace($script:VbeeToken)) { throw 'Chưa nhập token Vbee.' }
    if ($script:VbeeToken.StartsWith('Bearer ', [StringComparison]::OrdinalIgnoreCase)) {
        return $script:VbeeToken
    }
    return 'Bearer ' + $script:VbeeToken
}

function Get-VbeeErrorMessage {
    param(
        [Parameter(Mandatory = $true)][int]$StatusCode,
        [string]$Body
    )

    if ($StatusCode -eq 401) {
        return 'Vbee từ chối xác thực (401). Kiểm tra App ID, token và hạn dùng trong Vbee Studio.'
    }
    if ($StatusCode -eq 403) {
        return 'Vbee không cho phép yêu cầu (403). Kiểm tra quyền API hoặc quyền sử dụng giọng.'
    }
    if ($StatusCode -eq 429) {
        return 'Vbee đang giới hạn yêu cầu (429). Chờ một chút rồi chạy lại.'
    }

    $message = ''
    if (-not [string]::IsNullOrWhiteSpace($Body)) {
        try {
            $json = ConvertFrom-Json -InputObject $Body
            if ($json.error -and $json.error.message) { $message = [string]$json.error.message }
            elseif ($json.message) { $message = [string]$json.message }
        }
        catch {
            $message = ''
        }
    }
    if (-not [string]::IsNullOrWhiteSpace($script:VbeeToken)) {
        $tokenValue = $script:VbeeToken -replace '^Bearer\s+', ''
        if (-not [string]::IsNullOrWhiteSpace($tokenValue)) {
            $message = $message.Replace($tokenValue, '[token]')
        }
    }
    if (-not [string]::IsNullOrWhiteSpace($script:VbeeAppId)) {
        $message = $message.Replace($script:VbeeAppId, '[App ID]')
    }
    if ($StatusCode -eq 400 -and $message.IndexOf('This feature is not supported in user package', [StringComparison]::OrdinalIgnoreCase) -ge 0) {
        return 'Vbee từ chối yêu cầu TTS đồng bộ (mode=sync, HTTP 400). Lỗi này chưa đủ để kết luận App ID không dùng được mọi API TTS; hãy hỏi Vbee về quyền Realtime Sync hoặc giới hạn giọng/định dạng của ứng dụng. Script dừng, không tự gửi lại; Batch async cần webhook.'
    }
    if ([string]::IsNullOrWhiteSpace($message)) {
        return "Vbee trả lỗi HTTP $StatusCode."
    }
    return "Vbee trả lỗi HTTP ${StatusCode}: $message"
}

function Invoke-VbeeRequest {
    param(
        [Parameter(Mandatory = $true)][ValidateSet('Voices', 'Tts')][string]$Kind,
        [string]$JsonBody,
        [string]$Cursor
    )

    if ($Kind -eq 'Voices') {
        $uri = 'https://vbee.vn/api/public/v1/voices?voiceOwnership=VBEE&languageCode=vi-VN&limit=100'
        if (-not [string]::IsNullOrWhiteSpace($Cursor)) {
            $uri += '&cursor=' + [Uri]::EscapeDataString($Cursor)
        }
        $request = New-Object Net.Http.HttpRequestMessage([Net.Http.HttpMethod]::Get, $uri)
    }
    else {
        $request = New-Object Net.Http.HttpRequestMessage([Net.Http.HttpMethod]::Post, 'https://api.vbee.vn/v1/tts')
        $request.Content = New-Object Net.Http.StringContent($JsonBody, [Text.Encoding]::UTF8, 'application/json')
    }
    [void]$request.Headers.TryAddWithoutValidation('Authorization', (Get-VbeeAuthorizationHeader))
    [void]$request.Headers.TryAddWithoutValidation('App-Id', $script:VbeeAppId)

    try {
        $response = $script:VbeeHttpClient.SendAsync($request).GetAwaiter().GetResult()
        try {
            $content = $response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
            if (-not $response.IsSuccessStatusCode) {
                $body = [Text.Encoding]::UTF8.GetString($content)
                throw (Get-VbeeErrorMessage -StatusCode ([int]$response.StatusCode) -Body $body)
            }
            return ,$content
        }
        finally {
            $response.Dispose()
        }
    }
    catch [Net.Http.HttpRequestException] {
        throw 'Không thể kết nối Vbee. Kiểm tra Internet rồi thử lại.'
    }
    finally {
        $request.Dispose()
    }
}

function Get-VbeeVoices {
    $voices = New-Object 'System.Collections.Generic.List[object]'
    $seenCursors = New-Object 'System.Collections.Generic.HashSet[string]'
    $cursor = $null
    do {
        $responseBytes = Invoke-VbeeRequest -Kind Voices -Cursor $cursor
        $body = [Text.Encoding]::UTF8.GetString($responseBytes)
        $page = ConvertFrom-VbeeVoicePage -Json $body
        $voices.AddRange([object[]]$page.Voices)
        if ($page.HasNextPage) {
            $cursor = [string]$page.NextCursor
            if ([string]::IsNullOrWhiteSpace($cursor)) {
                throw 'Vbee báo còn trang giọng tiếp theo nhưng không cung cấp con trỏ.'
            }
            if (-not $seenCursors.Add($cursor)) {
                throw 'Vbee lặp lại con trỏ phân trang; đã dừng để tránh lặp vô hạn.'
            }
        }
        else {
            $cursor = $null
        }
    } while ($null -ne $cursor)
    return @($voices | Sort-Object Name)
}

function ConvertFrom-VbeeVoicePage {
    param([Parameter(Mandatory = $true)][string]$Json)

    try { $page = ConvertFrom-Json -InputObject $Json }
    catch { throw 'Phản hồi danh sách giọng Vbee không phải JSON hợp lệ.' }
    $result = $page.result
    if ($null -eq $result -or $result.PSObject.Properties.Name -notcontains 'voices') {
        throw 'Không nhận diện được danh sách giọng trong phản hồi Vbee.'
    }
    $voices = New-Object 'System.Collections.Generic.List[object]'
    foreach ($voice in @($result.voices)) {
        if (-not [string]::IsNullOrWhiteSpace([string]$voice.code)) {
            $name = [string]$voice.name
            if ([string]::IsNullOrWhiteSpace($name)) { $name = [string]$voice.code }
            $factor = $null
            if ($null -ne $voice.credit_factor) {
                $parsedFactor = [decimal]0
                if ([decimal]::TryParse([string]$voice.credit_factor, [Globalization.NumberStyles]::Number,
                        [Globalization.CultureInfo]::InvariantCulture, [ref]$parsedFactor) -and $parsedFactor -ge 0) {
                    $factor = $parsedFactor
                }
            }
            $voices.Add([pscustomobject]@{ Name = $name; Code = [string]$voice.code; CreditFactor = $factor })
        }
    }
    $pagination = $result.pagination
    $hasNextPage = $false
    $nextCursor = $null
    if ($pagination -and $pagination.has_next_page) {
        $hasNextPage = $true
        $nextCursor = [string]$pagination.next_cursor
    }
    return [pscustomobject]@{
        Voices = [object[]]$voices.ToArray()
        HasNextPage = $hasNextPage
        NextCursor = $nextCursor
    }
}

function Select-VbeeVoice {
    param([Parameter(Mandatory = $true)][object[]]$Voices)

    if ($Voices.Count -eq 0) { throw 'Vbee không trả về giọng tiếng Việt nào cho ứng dụng này.' }
    for ($i = 0; $i -lt $Voices.Count; $i++) {
        Write-Host ('{0,3}. {1}  [{2}]' -f ($i + 1), $Voices[$i].Name, $Voices[$i].Code)
    }
    while ($true) {
        $answer = Read-Host 'Nhập số giọng muốn dùng'
        $number = 0
        if ([int]::TryParse($answer, [ref]$number) -and $number -ge 1 -and $number -le $Voices.Count) {
            return [string]$Voices[$number - 1].Code
        }
        Write-Host "Nhập số từ 1 đến $($Voices.Count)." -ForegroundColor Yellow
    }
}

function Get-VbeeCreditEstimate {
    param(
        [Parameter(Mandatory = $true)][object[]]$Cues,
        [Parameter(Mandatory = $true)][decimal]$CreditFactor
    )
    $characterCount = [long]0
    foreach ($cue in $Cues) {
        foreach ($chunk in (Split-VbeeText -Text $cue.Text)) {
            $elements = [Globalization.StringInfo]::GetTextElementEnumerator($chunk)
            while ($elements.MoveNext()) { $characterCount++ }
        }
    }
    return [pscustomobject]@{
        Characters = $characterCount
        Points = [decimal][Math]::Ceiling($characterCount * $CreditFactor)
    }
}

function ConvertFrom-VbeeWav {
    param(
        [Parameter(Mandatory = $true)][byte[]]$WavBytes,
        [int]$TargetSampleRate = 44100
    )

    $stream = New-Object IO.MemoryStream(,$WavBytes)
    $reader = New-Object IO.BinaryReader($stream)
    try {
        if ($WavBytes.Length -lt 44 -or [Text.Encoding]::ASCII.GetString($reader.ReadBytes(4)) -ne 'RIFF') {
            throw 'Phản hồi audio không phải WAV RIFF hợp lệ.'
        }
        [void]$reader.ReadUInt32()
        if ([Text.Encoding]::ASCII.GetString($reader.ReadBytes(4)) -ne 'WAVE') {
            throw 'Phản hồi audio không phải WAV hợp lệ.'
        }
        $format = 0
        $channels = 0
        $sampleRate = 0
        $bits = 0
        $data = $null
        while ($stream.Position + 8 -le $stream.Length) {
            $chunkName = [Text.Encoding]::ASCII.GetString($reader.ReadBytes(4))
            $chunkLength = [long]$reader.ReadUInt32()
            $chunkStart = $stream.Position
            if ($chunkLength -gt ($stream.Length - $chunkStart)) {
                throw 'Dữ liệu chunk WAV bị thiếu.'
            }
            if ($chunkName -eq 'fmt ' -and $chunkLength -ge 16) {
                $format = [int]$reader.ReadUInt16()
                $channels = [int]$reader.ReadUInt16()
                $sampleRate = [int]$reader.ReadUInt32()
                [void]$reader.ReadUInt32()
                [void]$reader.ReadUInt16()
                $bits = [int]$reader.ReadUInt16()
                if ($format -eq 0xFFFE -and $chunkLength -ge 40) {
                    $stream.Position = $chunkStart + 24
                    $format = [int]$reader.ReadUInt16()
                }
            }
            elseif ($chunkName -eq 'data') {
                if ($chunkLength -gt [int]::MaxValue) { throw 'Đoạn WAV Vbee lớn hơn 2 GB và không thể xử lý trong bộ nhớ.' }
                $data = $reader.ReadBytes([int]$chunkLength)
            }
            $stream.Position = $chunkStart + $chunkLength + ($chunkLength % 2)
        }
        if ($null -eq $data -or ($format -ne 1 -and $format -ne 3) -or $channels -lt 1 -or $channels -gt 8 -or
            $sampleRate -lt 1 -or @(8, 16, 24, 32) -notcontains $bits -or ($format -eq 3 -and $bits -ne 32)) {
            throw 'Định dạng WAV không được hỗ trợ; cần WAV PCM 8/16/24/32-bit hoặc float 32-bit.'
        }
        $bytesPerSample = [int]($bits / 8)
        $frameSize = $bytesPerSample * $channels
        $frameCount = [int][Math]::Floor($data.Length / $frameSize)
        $mono = New-Object 'System.Collections.Generic.List[short]'
        for ($frame = 0; $frame -lt $frameCount; $frame++) {
            $sum = 0.0
            for ($channel = 0; $channel -lt $channels; $channel++) {
                $offset = $frame * $frameSize + $channel * $bytesPerSample
                switch ($bits) {
                    8 { $sample = ([int]$data[$offset] - 128) -shl 8 }
                    16 { $sample = [int][BitConverter]::ToInt16($data, $offset) }
                    24 {
                        $sample = [int]$data[$offset] -bor ([int]$data[$offset + 1] -shl 8) -bor ([int]$data[$offset + 2] -shl 16)
                        if ($sample -band 0x800000) { $sample -= 0x1000000 }
                        $sample = $sample -shr 8
                    }
                    32 {
                        if ($format -eq 3) {
                            $floatSample = [BitConverter]::ToSingle($data, $offset)
                            if ([float]::IsNaN($floatSample) -or [float]::IsInfinity($floatSample)) { $sample = 0 }
                            else { $sample = [int][Math]::Round($floatSample * [short]::MaxValue) }
                        }
                        else { $sample = [int][BitConverter]::ToInt32($data, $offset) -shr 16 }
                    }
                }
                $sum += $sample
            }
            $value = [int][Math]::Round($sum / $channels)
            $value = [Math]::Max([short]::MinValue, [Math]::Min([short]::MaxValue, $value))
            $mono.Add([short]$value)
        }
        $input = $mono.ToArray()
        if ($sampleRate -eq $TargetSampleRate -or $input.Length -eq 0) { return ,$input }
        $outputLength = [int][Math]::Ceiling($input.Length * [double]$TargetSampleRate / $sampleRate)
        $output = New-Object 'System.Collections.Generic.List[short]'
        for ($i = 0; $i -lt $outputLength; $i++) {
            $sourcePosition = $i * [double]$sampleRate / $TargetSampleRate
            $left = [Math]::Min([int][Math]::Floor($sourcePosition), $input.Length - 1)
            $right = [Math]::Min($left + 1, $input.Length - 1)
            $fraction = $sourcePosition - $left
            $value = [int][Math]::Round($input[$left] * (1 - $fraction) + $input[$right] * $fraction)
            $value = [Math]::Max([short]::MinValue, [Math]::Min([short]::MaxValue, $value))
            $output.Add([short]$value)
        }
        return ,$output.ToArray()
    }
    finally {
        $reader.Dispose()
        $stream.Dispose()
    }
}

function Write-VbeeSilence {
    param(
        [Parameter(Mandatory = $true)][IO.BinaryWriter]$Writer,
        [Parameter(Mandatory = $true)][long]$SampleCount
    )
    $buffer = New-Object byte[] 8192
    $remainingBytes = $SampleCount * 2
    while ($remainingBytes -gt 0) {
        $count = [int][Math]::Min($remainingBytes, $buffer.Length)
        $Writer.BaseStream.Write($buffer, 0, $count)
        $remainingBytes -= $count
    }
}

function Write-VbeeWavHeader {
    param(
        [Parameter(Mandatory = $true)][IO.BinaryWriter]$Writer,
        [Parameter(Mandatory = $true)][long]$SampleCount,
        [int]$SampleRate = 44100
    )
    $dataBytes = $SampleCount * 2
    if ($dataBytes -gt ([uint32]::MaxValue - 36)) {
        throw 'Tệp WAV dự kiến vượt giới hạn RIFF 4 GB; hãy chia phụ đề thành nhiều tệp.'
    }
    $position = $Writer.BaseStream.Position
    $Writer.BaseStream.Position = 0
    $Writer.Write([Text.Encoding]::ASCII.GetBytes('RIFF'))
    $Writer.Write([uint32](36 + $dataBytes))
    $Writer.Write([Text.Encoding]::ASCII.GetBytes('WAVEfmt '))
    $Writer.Write([uint32]16)
    $Writer.Write([uint16]1)
    $Writer.Write([uint16]1)
    $Writer.Write([uint32]$SampleRate)
    $Writer.Write([uint32]($SampleRate * 2))
    $Writer.Write([uint16]2)
    $Writer.Write([uint16]16)
    $Writer.Write([Text.Encoding]::ASCII.GetBytes('data'))
    $Writer.Write([uint32]$dataBytes)
    $Writer.BaseStream.Position = $position
}

function Invoke-VbeeSrt {
    param(
        [Parameter(Mandatory = $true)][object[]]$Cues,
        [Parameter(Mandatory = $true)][string]$TargetPath,
        [Parameter(Mandatory = $true)][string]$SelectedVoiceCode,
        [decimal]$Speed = 1.0
    )

    $directory = [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($TargetPath))
    if (-not [IO.Directory]::Exists($directory)) { [void][IO.Directory]::CreateDirectory($directory) }
    $temporaryPath = Join-Path $directory ('.' + [IO.Path]::GetFileName($TargetPath) + '.' + [Guid]::NewGuid().ToString('N') + '.tmp')
    $totalChunks = 0
    foreach ($cue in $Cues) { $totalChunks += (Split-VbeeText -Text $cue.Text).Count }
    $currentChunk = 0
    $completedSamples = 0L
    $delayedCues = 0
    $writer = $null
    try {
        $file = [IO.File]::Open($temporaryPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
        $writer = New-Object IO.BinaryWriter($file)
        Write-VbeeWavHeader -Writer $writer -SampleCount 0
        $writer.BaseStream.Position = 44
        foreach ($cue in $Cues) {
            $startSample = [long][Math]::Round($cue.StartMs * $script:OutputSampleRate / 1000.0)
            $delayed = $startSample -lt $completedSamples
            if ($startSample -gt $completedSamples) {
                $silenceSamples = $startSample - $completedSamples
                Write-VbeeSilence -Writer $writer -SampleCount $silenceSamples
                $completedSamples += $silenceSamples
            }
            $parts = Split-VbeeText -Text $cue.Text
            foreach ($part in $parts) {
                $currentChunk++
                $percent = [int][Math]::Floor(100 * $currentChunk / [Math]::Max(1, $totalChunks))
                Write-Progress -Activity 'Đang tạo giọng nói Vbee' -Status ("Đoạn SRT {0}/{1} · phần {2}/{3}" -f $cue.Index, $Cues.Count, $currentChunk, $totalChunks) -PercentComplete $percent
                $body = @{
                    text = $part
                    voiceCode = $SelectedVoiceCode
                    mode = 'sync'
                    outputFormat = 'wav'
                    speed = [double]$Speed
                } | ConvertTo-Json -Compress
                $audio = Invoke-VbeeRequest -Kind Tts -JsonBody $body
                $samples = ConvertFrom-VbeeWav -WavBytes $audio -TargetSampleRate $script:OutputSampleRate
                foreach ($sample in $samples) { $writer.Write([int16]$sample) }
                $completedSamples += $samples.Length
            }
            $endSample = [long][Math]::Round($cue.EndMs * $script:OutputSampleRate / 1000.0)
            if ($completedSamples -gt $endSample) { $delayed = $true }
            if ($delayed) { $delayedCues++ }
        }
        $subtitleEnd = [long][Math]::Round((($Cues | Measure-Object -Property EndMs -Maximum).Maximum) * $script:OutputSampleRate / 1000.0)
        if ($subtitleEnd -gt $completedSamples) {
            Write-VbeeSilence -Writer $writer -SampleCount ($subtitleEnd - $completedSamples)
            $completedSamples = $subtitleEnd
        }
        Write-VbeeWavHeader -Writer $writer -SampleCount $completedSamples
        $writer.Flush()
        $writer.Dispose()
        $writer = $null
        Move-Item -LiteralPath $temporaryPath -Destination $TargetPath -Force
        Write-Progress -Activity 'Đang tạo giọng nói Vbee' -Completed
        if ($delayedCues -eq 0) {
            Write-Host "Đã tạo WAV: $TargetPath. Các khoảng lặng theo SRT được giữ."
        }
        else {
            Write-Warning "$delayedCues đoạn chồng lấn hoặc lời đọc dài hơn thời lượng SRT; lời nói được nối tiếp, không bị cắt. Không thể khớp chính xác toàn bộ mốc thời gian."
            Write-Host "Đã tạo WAV: $TargetPath"
        }
    }
    finally {
        if ($null -ne $writer) { $writer.Dispose() }
        if ([IO.File]::Exists($temporaryPath)) { Remove-Item -LiteralPath $temporaryPath -Force }
        Write-Progress -Activity 'Đang tạo giọng nói Vbee' -Completed
    }
}

function New-VbeeTestWav {
    $stream = New-Object IO.MemoryStream
    $writer = New-Object IO.BinaryWriter($stream)
    $samples = [short[]]@(1000, -1000, 2000, -2000)
    $dataLength = $samples.Length * 2
    $writer.Write([Text.Encoding]::ASCII.GetBytes('RIFF'))
    $writer.Write([uint32](36 + $dataLength))
    $writer.Write([Text.Encoding]::ASCII.GetBytes('WAVEfmt '))
    $writer.Write([uint32]16)
    $writer.Write([uint16]1)
    $writer.Write([uint16]1)
    $writer.Write([uint32]44100)
    $writer.Write([uint32]88200)
    $writer.Write([uint16]2)
    $writer.Write([uint16]16)
    $writer.Write([Text.Encoding]::ASCII.GetBytes('data'))
    $writer.Write([uint32]$dataLength)
    foreach ($sample in $samples) { $writer.Write([int16]$sample) }
    $writer.Flush()
    $result = $stream.ToArray()
    $writer.Dispose()
    $stream.Dispose()
    return ,$result
}

function Invoke-VbeeSelfTest {
    $srt = @"
1
00:00:02,000 --> 00:00:03,250
<i>Xin chào</i> &amp; cảm ơn

2
00:00:05.000 --> 00:00:06.000
Vbee
"@
    $cues = ConvertFrom-VbeeSrt $srt
    if ($cues.Count -ne 2 -or $cues[0].Text -ne 'Xin chào & cảm ơn' -or $cues[1].StartMs -ne 5000) {
        throw 'Self-test thất bại: phân tích SRT.'
    }
    $chunks = Split-VbeeText ('a' * 301)
    if ($chunks.Count -ne 2 -or $chunks[0].Length -ne 300 -or $chunks[1].Length -ne 1) {
        throw 'Self-test thất bại: chia giới hạn 300 ký tự.'
    }
    $voicePage = ConvertFrom-VbeeVoicePage -Json '{"result":{"pagination":{"has_next_page":true,"next_cursor":"cursor-2"},"voices":[{"code":"hn_female_001","name":"Giọng Hà Nội","credit_factor":1.25}]}}'
    if (-not $voicePage.HasNextPage -or $voicePage.NextCursor -ne 'cursor-2' -or
        $voicePage.Voices.Count -ne 1 -or $voicePage.Voices[0].Code -ne 'hn_female_001' -or
        $voicePage.Voices[0].CreditFactor -ne 1.25) {
        throw 'Self-test thất bại: đọc tên/ID, hệ số và phân trang danh sách giọng.'
    }
    $creditEstimate = Get-VbeeCreditEstimate -Cues $cues -CreditFactor 1.25
    if ($creditEstimate.Characters -ne 22 -or $creditEstimate.Points -ne 28) {
        throw 'Self-test thất bại: ước lượng điểm theo hệ số giọng.'
    }
    $packageError = Get-VbeeErrorMessage -StatusCode 400 -Body '{"error":{"message":"This feature is not supported in user package"}}'
    if ($packageError -notmatch 'Realtime Sync' -or $packageError -notmatch 'không dùng được mọi API TTS') {
        throw 'Self-test thất bại: chẩn đoán lỗi package không được làm người dùng hiểu rằng mọi API TTS đều bị khóa.'
    }
    $samples = ConvertFrom-VbeeWav -WavBytes (New-VbeeTestWav) -TargetSampleRate 44100
    if ($samples.Length -ne 4 -or $samples[0] -ne 1000 -or $samples[1] -ne -1000) {
        throw 'Self-test thất bại: đọc WAV PCM.'
    }
    $stream = New-Object IO.MemoryStream
    $writer = New-Object IO.BinaryWriter($stream)
    Write-VbeeWavHeader -Writer $writer -SampleCount 0
    $writer.BaseStream.Position = 44
    $writer.Write([int16]1000)
    $writer.Write([int16]-1000)
    Write-VbeeSilence -Writer $writer -SampleCount 2
    $writer.Write([int16]2000)
    $writer.Write([int16]-2000)
    Write-VbeeWavHeader -Writer $writer -SampleCount 6
    $writer.Flush()
    $composedWav = $stream.ToArray()
    $writer.Dispose()
    $stream.Dispose()
    $composed = ConvertFrom-VbeeWav -WavBytes $composedWav -TargetSampleRate 44100
    if ($composed.Length -ne 6 -or $composed[0] -ne 1000 -or $composed[1] -ne -1000 -or
        $composed[2] -ne 0 -or $composed[3] -ne 0 -or $composed[4] -ne 2000) {
        throw 'Self-test thất bại: ghép audio và khoảng lặng.'
    }
    Write-Host 'Self-test passed: SRT UTF-8/HTML, voice IDs/pagination/credit factor, credit estimate, 300-character chunking, WAV decoding and silence composition.'
}

if ($SelfTest) {
    Invoke-VbeeSelfTest
    return
}

$secureToken = $null
$plainTokenPtr = [IntPtr]::Zero
try {
    if ([string]::IsNullOrWhiteSpace($SrtPath)) {
        $SrtPath = (Read-Host 'Kéo thả hoặc nhập đường dẫn file SRT').Trim().Trim('"')
    }
    if (-not [IO.File]::Exists($SrtPath)) { throw 'Không tìm thấy file SRT đã chọn.' }
    $cues = ConvertFrom-VbeeSrt (Read-VbeeTextFile -Path $SrtPath)
    Write-Host "Đã đọc $($cues.Count) đoạn phụ đề."

    if ([string]::IsNullOrWhiteSpace($OutputPath)) {
        $baseName = [IO.Path]::GetFileNameWithoutExtension($SrtPath) + '_voice.wav'
        $OutputPath = Join-Path ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($SrtPath))) $baseName
        $answer = Read-Host "File WAV đầu ra (Enter để dùng $OutputPath)"
        if (-not [string]::IsNullOrWhiteSpace($answer)) { $OutputPath = $answer.Trim().Trim('"') }
    }
    if ([string]::Equals([IO.Path]::GetFullPath($OutputPath), [IO.Path]::GetFullPath($SrtPath), [StringComparison]::OrdinalIgnoreCase)) {
        throw 'File WAV đầu ra không thể ghi đè lên SRT.'
    }
    if ([IO.File]::Exists($OutputPath)) {
        $overwrite = Read-Host "File '$OutputPath' đã tồn tại. Ghi đè? (y/N)"
        if ($overwrite -notmatch '^(y|yes)$') { throw 'Đã hủy để giữ nguyên file WAV hiện có.' }
    }

    $script:VbeeAppId = (Read-Host 'Nhập App ID của ứng dụng trong Vbee Studio (không phải token)').Trim()
    if ([string]::IsNullOrWhiteSpace($script:VbeeAppId)) { throw 'App ID không được để trống.' }
    $secureToken = Read-Host 'Nhập token Vbee (không hiển thị khi gõ)' -AsSecureString
    if ($secureToken.Length -eq 0) { throw 'Token không được để trống.' }
    $plainTokenPtr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureToken)
    $script:VbeeToken = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($plainTokenPtr)
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($plainTokenPtr)
    $plainTokenPtr = [IntPtr]::Zero

    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Add-Type -AssemblyName System.Net.Http
    $script:VbeeHttpClient = New-Object Net.Http.HttpClient
    $script:VbeeHttpClient.Timeout = [TimeSpan]::FromSeconds(90)

    Write-Host 'Lưu ý: hạn mức/điểm API không có endpoint số dư được công bố trong tài liệu API; kiểm tra mục API Points trong Vbee Studio.'
    Write-Host 'Đang tải danh sách giọng tiếng Việt từ Vbee...'
    $voices = Get-VbeeVoices
    Write-Host "Vbee trả về $($voices.Count) giọng (tên và ID):"
    if ([string]::IsNullOrWhiteSpace($VoiceCode)) {
        $VoiceCode = Select-VbeeVoice -Voices $voices
    }
    $selectedVoice = $voices | Where-Object { $_.Code -eq $VoiceCode } | Select-Object -First 1
    if ($null -eq $selectedVoice) {
        throw "ID giọng '$VoiceCode' không có trong danh sách Vbee vừa tải. Chạy lại và chọn từ danh sách hiển thị."
    }
    if ($null -eq $selectedVoice.CreditFactor) {
        throw 'Vbee không trả hệ số tiêu hao cho giọng này; không thể ước lượng điểm nên chưa gửi yêu cầu.'
    }

    $creditCap = [decimal]10000
    $creditCapText = Read-Host 'Trần ước tính điểm/lần (Enter = 10000)'
    if (-not [string]::IsNullOrWhiteSpace($creditCapText)) {
        $culture = [Globalization.CultureInfo]::InvariantCulture
        if (-not [decimal]::TryParse($creditCapText, [Globalization.NumberStyles]::Number, $culture, [ref]$creditCap) -or $creditCap -lt 1) {
            throw 'Trần điểm phải là số lớn hơn 0.'
        }
    }
    $creditEstimate = Get-VbeeCreditEstimate -Cues $cues -CreditFactor $selectedVoice.CreditFactor
    Write-Host ("Ước tính lần này: khoảng {0:N0} điểm ({1:N0} ký tự gửi, gồm dấu cách/dấu câu × hệ số {2}); trần {3:N0} điểm." -f
        $creditEstimate.Points, $creditEstimate.Characters, $selectedVoice.CreditFactor, $creditCap)
    if ($creditEstimate.Points -gt $creditCap) {
        Write-Warning 'Ước tính vượt trần. Đây không phải số dư; điểm thực tế có thể khác và tài liệu API không công bố endpoint số dư.'
        $continue = Read-Host 'Chưa gửi TTS. Cho phép lần chuyển đổi này vượt trần? (y/N)'
        if ($continue -notmatch '^(y|yes)$') {
            throw 'Đã dừng trước khi gửi yêu cầu TTS vì ước tính vượt trần điểm.'
        }
    }
    $speed = [decimal]1.0
    $speedText = Read-Host 'Tốc độ đọc (0.25-1.9, Enter để dùng 1.0)'
    if (-not [string]::IsNullOrWhiteSpace($speedText)) {
        $culture = [Globalization.CultureInfo]::InvariantCulture
        if (-not [decimal]::TryParse($speedText, [Globalization.NumberStyles]::Number, $culture, [ref]$speed)) {
            throw 'Tốc độ không hợp lệ. Dùng số từ 0.25 đến 1.9.'
        }
    }
    if ($speed -lt 0.25 -or $speed -gt 1.9) { throw 'Tốc độ phải nằm trong khoảng 0.25 đến 1.9.' }

    Invoke-VbeeSrt -Cues $cues -TargetPath $OutputPath -SelectedVoiceCode $VoiceCode -Speed $speed
}
catch {
    Write-Error $_.Exception.Message
    exit 1
}
finally {
    if ($plainTokenPtr -ne [IntPtr]::Zero) {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($plainTokenPtr)
    }
    if ($null -ne $secureToken) { $secureToken.Dispose() }
    if ($null -ne $script:VbeeHttpClient) { $script:VbeeHttpClient.Dispose() }
    $script:VbeeToken = $null
    $script:VbeeAppId = $null
}
