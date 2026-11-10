# Vbee SRT Studio

Ứng dụng Windows portable để chuyển file phụ đề SRT thành một file WAV bằng API Vbee. Không cần cài .NET hoặc phần mềm bổ sung. Mã nguồn không chứa khóa API; App ID và token chỉ ở trong bộ nhớ của phiên ứng dụng, không được ghi xuống file.

## Bắt đầu nhanh

**Chỉ cần dùng ứng dụng?** Tải [VbeeSrtStudio-portable.zip](https://github.com/huynhyn1/Vbee-SRT-Studio/releases/latest/download/VbeeSrtStudio-portable.zip), giải nén vào một thư mục rồi nhấp đúp `START.bat`. Trong thư mục dự án đã build, cũng có thể nhấp đúp `START.bat` ở ngay đây.

Ứng dụng cần Internet và App ID/token từ [Vbee Studio](https://studio.vbee.vn/apps). Khi cửa sổ mở:

1. Nhập App ID và token, rồi bấm **Tải giọng đọc**.
2. Chọn file SRT, giọng đọc, tốc độ và nơi lưu file WAV.
3. Bấm **Tạo file WAV**. Có thể hủy khi đang xử lý.

## Tự build trên Windows

Cần .NET 8 SDK. Mở Command Prompt hoặc PowerShell ở thư mục dự án và chạy:

```text
build.cmd
```

Lệnh này tạo ứng dụng portable cùng launcher và README trong `dist\win-x64\`, đồng thời tạo `VbeeSrtStudio-portable.zip` ngay tại thư mục dự án. Có thể chạy kiểm tra nội bộ bằng:

```text
dotnet run --project VbeeSrtStudio.csproj -- --self-test
```

## Ghi chú

- Dùng API đồng bộ `https://api.vbee.vn/v1/tts`, tối đa 300 ký tự mỗi yêu cầu; danh sách giọng lấy từ `https://vbee.vn/api/public/v1/voices`. Nếu Vbee trả 401, kiểm tra App ID/token và hạn sử dụng token.
- Hỗ trợ SRT UTF-8/UTF-8 BOM, UTF-16 BOM và Windows-1258. Đầu ra WAV PCM mono 44,1 kHz.
- Khoảng lặng theo mốc bắt đầu của phụ đề được giữ. Nếu một câu đọc dài hơn cửa sổ phụ đề hoặc chồng lấn câu kế tiếp, âm thanh không bị cắt mà được nối tiếp; vì vậy các mốc sau có thể trễ. Thời lượng đọc thực tế không thể khớp chính xác thời lượng phụ đề.
- Token chỉ được giữ trong bộ nhớ khi ứng dụng đang chạy. Dùng **Xóa token** hoặc đóng ứng dụng để xóa token khỏi phiên; thu hồi token trong Vbee Studio để chấm dứt hiệu lực.
- Mã nguồn build thành công, nhưng UI và API chưa được kiểm chứng trong môi trường build vì Windows Application Control đã chặn chạy assembly tự build.
