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

## Định dạng và thời gian

- Hỗ trợ SRT UTF-8, UTF-8 BOM, UTF-16 BOM và Windows-1258.
- Xuất WAV PCM mono, 44,1 kHz; các khoảng lặng theo phụ đề được giữ.
- Nếu lời đọc dài hơn khoảng phụ đề hoặc các câu bị chồng lấn, audio được nối liên tiếp thay vì cắt lời. Khi đó âm thanh không thể khớp chính xác với mọi mốc thời gian phụ đề.
- Mỗi yêu cầu gửi tới API đồng bộ Vbee giới hạn 300 ký tự. Nếu gặp lỗi 401, kiểm tra App ID/token và thời hạn token.
- Chọn **Xóa token** hoặc đóng ứng dụng để xóa thông tin khỏi phiên. Thu hồi token trong Vbee Studio để chấm dứt hiệu lực.
- Tệp phát hành portable hiện chưa có chữ ký số Authenticode; Windows có thể hiện cảnh báo nhà phát hành khi mở lần đầu.

## Build từ mã nguồn

Cần .NET 8 SDK trên Windows. Chạy `build.cmd`; tệp thực thi và hướng dẫn sẽ xuất hiện trong `dist\win-x64`, kèm gói ZIP trong thư mục dự án.

**Kiểm thử:** bản phát hành được build thành công. Giao diện và API chưa được kiểm tra trực tiếp trong môi trường build vì Windows Application Control đã chặn chạy assembly tự build.
