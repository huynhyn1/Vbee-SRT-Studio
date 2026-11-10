# Vbee SRT Studio

**Chuyển phụ đề SRT thành giọng nói Vbee — trực tiếp trên Windows, không cần cài đặt.**

Ứng dụng portable, đóng gói cùng .NET nên không cần cài runtime. API của bạn, App ID và token chỉ được dùng trong ứng dụng; thông tin xác thực chỉ lưu trong bộ nhớ khi ứng dụng đang chạy, không ghi vào tệp.

## Tải và mở ứng dụng

1. Tải [Vbee SRT Studio cho Windows 64-bit](https://github.com/huynhyn1/Vbee-SRT-Studio/releases/latest/download/Vbee-SRT-Studio-Windows-x64.zip).
2. Giải nén ZIP vào một thư mục.
3. Mở thư mục vừa giải nén và **nhấp đúp `Vbee SRT Studio.exe`**. Không cần trình cài đặt.

Ứng dụng cần Internet và App ID/token từ [Vbee Studio](https://studio.vbee.vn/apps).

## Tạo tệp âm thanh

1. Nhập App ID và token, sau đó chọn **Tải danh sách giọng**.
2. Chọn phụ đề SRT, giọng đọc, tốc độ và vị trí lưu WAV.
3. Chọn **Tạo file WAV**. Tiến độ được hiển thị và có thể hủy khi đang xử lý.

## Dùng PowerShell thay thế

Gói ZIP có sẵn **`Chay Vbee SRT.bat`** để nhấp đúp chạy PowerShell script — không cần tự gõ lệnh. Có thể mở PowerShell trong thư mục đã giải nén và chạy trực tiếp `VbeeSrtStudio.ps1` nếu muốn truyền tham số:

```powershell
powershell.exe -NoProfile -File .\VbeeSrtStudio.ps1
```

Chọn SRT; nhập **App ID** của ứng dụng trong danh sách Vbee Studio (đây không phải token), rồi nhập token riêng lấy trong chi tiết ứng dụng. Token không hiển thị khi gõ và không được lưu. Script tải danh sách giọng từ Vbee, hiện tên cùng ID để chọn, rồi tạo WAV. Có thể truyền trước đường dẫn và ID giọng:

```powershell
powershell.exe -NoProfile -File .\VbeeSrtStudio.ps1 -SrtPath "C:\Audio\video.srt" -OutputPath "C:\Audio\video_voice.wav" -VoiceCode "voice-code"
```

Script vẫn yêu cầu App ID/token, hiển thị danh sách ID giọng vừa tải và xác nhận ID đã chọn thuộc danh sách. Mỗi câu được gửi tuần tự qua API đồng bộ Vbee, chia phần tối đa 300 ký tự; khoảng lặng theo SRT được giữ. Câu đọc dài hoặc chồng lấn được nối tiếp chứ không cắt, nên khi đó không thể khớp chính xác toàn bộ mốc thời gian.
Nhấn **Ctrl+C** trong cửa sổ PowerShell để dừng; tệp tạm sẽ được dọn khi script kết thúc.

Không có endpoint số dư/hạn mức còn lại được công bố trong tài liệu API đã tham khảo. Kiểm tra điểm/hạn mức tại trang ứng dụng trong [Vbee Studio](https://studio.vbee.vn/apps); mỗi yêu cầu tổng hợp có thể tiêu hao credit và khi thiếu credit, Vbee sẽ trả lỗi cho yêu cầu. Script chỉ gửi App ID/token tới endpoint chính thức tải giọng `vbee.vn` và tổng hợp `api.vbee.vn`, không ghi credential vào tệp hay log.

Có thể chạy kiểm tra offline (không gọi API, không cần credential):

```powershell
powershell.exe -NoProfile -File .\VbeeSrtStudio.ps1 -SelfTest
```

Máy Windows hiện đã báo `running scripts is disabled` cho script này. Tệp BAT chỉ gọi PowerShell theo chính sách hiện có; **không bỏ qua execution policy**, nên trên máy đang chặn script, nhấp BAT vẫn không chạy được chức năng tạo audio. Không hạ hoặc bỏ qua cơ chế bảo vệ; hãy dùng ứng dụng EXE nếu chính sách cho phép hoặc hỏi quản trị viên về bản script đã ký/phương thức được phê duyệt.

## Định dạng và thời gian

- Hỗ trợ SRT UTF-8, UTF-8 BOM, UTF-16 BOM và Windows-1258.
- Xuất WAV PCM mono, 44,1 kHz; các khoảng lặng theo phụ đề được giữ.
- Nếu lời đọc dài hơn khoảng phụ đề hoặc các câu bị chồng lấn, audio được nối liên tiếp thay vì cắt lời. Khi đó âm thanh không thể khớp chính xác với mọi mốc thời gian phụ đề.
- Mỗi yêu cầu gửi tới API đồng bộ Vbee giới hạn 300 ký tự. Nếu gặp lỗi 401, kiểm tra App ID/token và thời hạn token.
- Danh sách giọng được lọc theo `voiceOwnership=VBEE` và `languageCode=vi-VN`, tự tải tiếp các trang theo con trỏ Vbee; mã giọng hiển thị ngay cạnh tên và có thể sao chép bằng nút **Sao chép ID**.
- Chọn **Xóa token** hoặc đóng ứng dụng để xóa thông tin khỏi phiên. Thu hồi token trong Vbee Studio để chấm dứt hiệu lực.
- Tệp phát hành portable hiện chưa có chữ ký số Authenticode; Windows có thể hiện cảnh báo nhà phát hành khi mở lần đầu.

## Build từ mã nguồn

Cần .NET 8 SDK trên Windows. Chạy `build.cmd`; tệp thực thi và hướng dẫn sẽ xuất hiện trong `dist\win-x64`, kèm gói ZIP trong thư mục dự án.

**Kiểm thử:** bản phát hành được build thành công. Giao diện và API (bao gồm danh sách giọng cần App ID/token hợp lệ) chưa được kiểm tra trực tiếp vì Windows Application Control chặn chạy EXE tự build; chính sách PowerShell trên máy build cũng chặn nạp script, nên chạy `-SelfTest` tại đây không khả dụng.
