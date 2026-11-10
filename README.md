# Vbee SRT Studio

Ứng dụng Windows nhỏ gọn để chuyển phụ đề SRT thành một file WAV bằng API Vbee. Mã nguồn không chứa khóa API; App ID và token chỉ được giữ trong bộ nhớ của phiên ứng dụng, không ghi xuống file hay lưu cài đặt. Đóng ứng dụng hoặc bấm **Xóa token** để xóa thông tin khỏi phiên.

## Dùng bản portable

Chạy `dist\win-x64\VbeeSrtStudio.exe`. Bản phát hành được đóng gói self-contained, không cần cài .NET riêng. Cần kết nối Internet và App ID/token lấy từ [Vbee Studio](https://studio.vbee.vn/apps).

Trong ứng dụng:

1. Nhập App ID và token; bấm **Tải giọng đọc**.
2. Chọn file SRT, chọn giọng/tốc độ và nơi lưu WAV.
3. Bấm **Tạo file WAV**. Các yêu cầu TTS được gửi lần lượt; có thể hủy giữa chừng.

## Tự build trên Windows

Cần .NET 8 SDK. Mở PowerShell trong thư mục này và chạy:

```powershell
.\build.cmd
```

File portable được tạo tại `dist\win-x64\VbeeSrtStudio.exe`. Có thể chạy kiểm tra nội bộ bằng:

```powershell
dotnet run --project .\VbeeSrtStudio.csproj -- --self-test
```

## Ghi chú

- Dùng API đồng bộ `https://api.vbee.vn/v1/tts`, giới hạn 300 ký tự mỗi yêu cầu; danh sách giọng lấy từ `https://vbee.vn/api/public/v1/voices`.
- Hỗ trợ SRT UTF-8/UTF-8 BOM, UTF-16 BOM và Windows-1258. Đầu ra WAV PCM mono 44,1 kHz.
- Khoảng lặng theo mốc bắt đầu của phụ đề được giữ. Nếu một câu đọc dài hơn cửa sổ phụ đề hoặc chồng lấn câu kế tiếp, âm thanh không bị cắt mà được nối tiếp; vì vậy các mốc sau có thể trễ. Thời lượng đọc thực tế không thể khớp chính xác thời lượng phụ đề.
- Ứng dụng không lưu App ID/token. Để chấm dứt hiệu lực token, thu hồi token trong Vbee Studio.
- Mã nguồn build thành công, nhưng UI và API chưa được kiểm chứng trong môi trường build này vì chính sách Windows Application Control đã chặn chạy assembly tự build.
